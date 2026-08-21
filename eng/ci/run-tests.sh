#!/usr/bin/env bash

set -uo pipefail

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/../.." && pwd)
solution="$repository_root/Arronix.sln"
dotnet_command=${DOTNET_COMMAND:-dotnet}
artifact_root="$repository_root/artifacts"
test_result_root="$artifact_root/test-results"
compatibility_output_root="$artifact_root/compatibility"
compile_input_root="$artifact_root/compile-inputs"
compile_input_log="$compile_input_root/solution.binlog"
sentinel_file="$repository_root/verification/ci/required-tests.tsv"
sentinel_validator="$repository_root/eng/ci/validate-required-tests.sh"
sentinel_relative_file="verification/ci/required-tests.tsv"
ledger_relative_root="verification/compatibility"
ledger_documents=(
    baseline.json
    sources.jsonl
    requirements.jsonl
    cases.jsonl
    replacements.jsonl
)

if ! command -v "$dotnet_command" >/dev/null 2>&1 && [[ ! -x "$dotnet_command" ]]; then
    echo "error: .NET command '$dotnet_command' is not executable." >&2
    exit 2
fi

if [[ "$artifact_root" != "$repository_root/artifacts" ]]; then
    echo "error: refusing to clean an unexpected artifact path '$artifact_root'." >&2
    exit 2
fi

rm -rf "$test_result_root" "$compatibility_output_root" "$compile_input_root"
mkdir -p "$test_result_root" "$compatibility_output_root" "$compile_input_root"

discovered_projects=$(mktemp "${TMPDIR:-/tmp}/arronix-discovered-tests.XXXXXX")
solution_projects=$(mktemp "${TMPDIR:-/tmp}/arronix-solution-tests.XXXXXX")
represented_projects=$(mktemp "${TMPDIR:-/tmp}/arronix-represented-tests.XXXXXX")
previous_ledger_root=$(mktemp -d "${TMPDIR:-/tmp}/arronix-previous-ledger.XXXXXX")

cleanup() {
    rm -f "$discovered_projects" "$solution_projects" "$represented_projects"
    rm -rf "$previous_ledger_root"
}

trap cleanup EXIT

cd "$repository_root"

# CI supplies the comparison revision. Locally, compare dirty proof registries with
# HEAD, or unchanged proof registries with HEAD's first parent.
previous_revision=""
previous_revision_source=""
requested_previous_revision=${ARRONIX_PREVIOUS_LEDGER_REVISION:-}
if [[ "$requested_previous_revision" =~ ^0+$ ]]; then
    echo "The supplied before revision is all zeroes; using the local comparison fallback."
    requested_previous_revision=""
fi

if [[ -n "$requested_previous_revision" ]]; then
    if ! command -v git >/dev/null 2>&1; then
        echo "error: cannot resolve supplied previous ledger revision without Git." >&2
        exit 2
    elif ! previous_revision=$(git rev-parse --verify "${requested_previous_revision}^{commit}" 2>/dev/null); then
        echo "error: supplied previous ledger revision '$requested_previous_revision' is not a commit." >&2
        exit 2
    else
        previous_revision_source="ARRONIX_PREVIOUS_LEDGER_REVISION"
    fi
elif command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    proof_registry_paths=()
    for document in "${ledger_documents[@]}"; do
        proof_registry_paths+=("$ledger_relative_root/$document")
    done
    proof_registry_paths+=("$sentinel_relative_file")

    if [[ -n "$(git status --porcelain --untracked-files=all -- "${proof_registry_paths[@]}")" ]]; then
        fallback_revision="HEAD"
        previous_revision_source="dirty proof registry versus HEAD"
    else
        fallback_revision="HEAD^"
        previous_revision_source="unchanged proof registries versus HEAD^"
    fi

    if ! previous_revision=$(git rev-parse --verify "${fallback_revision}^{commit}" 2>/dev/null); then
        previous_revision=""
        previous_revision_source=""
        echo "No previous commit is available for compatibility comparison."
    fi
fi

previous_ledger_arguments=()
previous_sentinel_file=""
if [[ -n "$previous_revision" ]]; then
    present_documents=0
    missing_documents=()
    for document in "${ledger_documents[@]}"; do
        ledger_object="$previous_revision:$ledger_relative_root/$document"
        if git cat-file -e "$ledger_object" 2>/dev/null; then
            present_documents=$((present_documents + 1))
        else
            missing_documents+=("$document")
        fi
    done

    if [[ $present_documents -eq 0 ]]; then
        echo "Revision $previous_revision contains no compatibility ledger; treating this as its first introduction."
    elif [[ ${#missing_documents[@]} -ne 0 ]]; then
        echo "error: revision $previous_revision contains only part of the compatibility ledger." >&2
        printf 'error: missing previous ledger document: %s\n' "${missing_documents[@]}" >&2
        exit 2
    else
        for document in "${ledger_documents[@]}"; do
            if ! git show "$previous_revision:$ledger_relative_root/$document" > "$previous_ledger_root/$document"; then
                echo "error: failed to materialize previous ledger document '$document'." >&2
                exit 2
            fi
        done
        previous_ledger_arguments=(--previous-ledger "$previous_ledger_root")
        echo "Comparing compatibility ledger with $previous_revision ($previous_revision_source)."
    fi

    sentinel_object="$previous_revision:$sentinel_relative_file"
    if git cat-file -e "$sentinel_object" 2>/dev/null; then
        previous_sentinel_file="$previous_ledger_root/required-tests.tsv"
        if ! git show "$sentinel_object" > "$previous_sentinel_file"; then
            echo "error: failed to materialize the previous required proof sentinel registry." >&2
            exit 2
        fi
        echo "Comparing required proof sentinels with $previous_revision ($previous_revision_source)."
    else
        echo "Revision $previous_revision contains no required proof sentinel registry; treating this as its first introduction."
    fi
fi

if [[ ! -x "$sentinel_validator" ]]; then
    echo "error: required proof sentinel validator '$sentinel_validator' is not executable." >&2
    exit 2
fi

sentinel_validation_arguments=("$sentinel_file" "$repository_root")
if [[ -n "$previous_sentinel_file" ]]; then
    sentinel_validation_arguments+=("$previous_sentinel_file")
fi
if ! "$sentinel_validator" "${sentinel_validation_arguments[@]}"; then
    exit 1
fi

find src -type f -name '*.Tests.csproj' -print | LC_ALL=C sort > "$discovered_projects"
"$dotnet_command" sln "$solution" list \
    | awk '/\.Tests\.csproj$/ { print }' \
    | LC_ALL=C sort > "$solution_projects"

if [[ ! -s "$discovered_projects" ]]; then
    echo "error: no test projects were discovered." >&2
    exit 1
fi

if ! diff -u "$discovered_projects" "$solution_projects"; then
    echo "error: every test project must be present in Arronix.sln." >&2
    exit 1
fi

echo "Using .NET SDK $("$dotnet_command" --version)"
echo "Restoring the locked dependency graph"
if ! "$dotnet_command" restore "$solution" --locked-mode; then
    exit 1
fi

echo "Building the complete solution and recording actual compiler inputs"
if ! "$dotnet_command" build "$solution" \
    --configuration Release \
    --no-restore \
    --no-incremental \
    -warnaserror \
    -p:ContinuousIntegrationBuild=true \
    "-bl:$compile_input_log;ProjectImports=None"; then
    exit 1
fi

test_status=0
while IFS= read -r project; do
    project_name=$(basename "$project" .csproj)
    project_result_root="$test_result_root/$project_name"
    mkdir -p "$project_result_root"

    echo "Testing $project"
    "$dotnet_command" test "$project" \
        --configuration Release \
        --no-build \
        --no-restore \
        --results-directory "$project_result_root" \
        --logger "trx;LogFileName=$project_name.trx" \
        --logger "nunit;LogFilePath=$project_result_root/$project_name.xml" \
        --blame-hang \
        --blame-hang-timeout 5m \
        2>&1 | tee "$project_result_root/console.log"

    project_status=${PIPESTATUS[0]}
    if [[ $project_status -ne 0 ]]; then
        test_status=1
    fi

    # dotnet test can exit successfully after a project silently stops discovering tests.
    expected_result_file="$project_result_root/$project_name.xml"
    if [[ ! -f "$expected_result_file" ]]; then
        echo "error: test project '$project' produced no NUnit result '$expected_result_file'." >&2
        test_status=1
        continue
    fi

    if ! grep -F '<test-case ' "$expected_result_file" >/dev/null; then
        echo "error: test project '$project' produced an NUnit result with no test cases." >&2
        test_status=1
        continue
    fi

    printf '%s\n' "$project" >> "$represented_projects"
done < "$discovered_projects"

if ! diff -u "$discovered_projects" "$represented_projects"; then
    echo "error: every discovered test project must produce a non-empty NUnit result." >&2
    test_status=1
fi

echo "Validating the compatibility ledger"
ratchet_arguments=(
    validate
    --ledger verification/compatibility
    --results "$test_result_root"
    --required-tests "$sentinel_file"
    --compile-inputs "$compile_input_root"
    --root "$repository_root"
)
if [[ ${#previous_ledger_arguments[@]} -ne 0 ]]; then
    ratchet_arguments+=("${previous_ledger_arguments[@]}")
fi
"$dotnet_command" run \
    --project src/Arronix.Compatibility.Ratchet/Arronix.Compatibility.Ratchet.csproj \
    --configuration Release \
    --no-build \
    --no-restore \
    -- \
    "${ratchet_arguments[@]}" \
    2>&1 | tee "$compatibility_output_root/summary.txt"
ratchet_status=${PIPESTATUS[0]}

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    {
        echo "## Arronix verification"
        echo
        echo '```text'
        cat "$compatibility_output_root/summary.txt"
        echo '```'
    } >> "$GITHUB_STEP_SUMMARY"
fi

if [[ $test_status -ne 0 || $ratchet_status -ne 0 ]]; then
    exit 1
fi
