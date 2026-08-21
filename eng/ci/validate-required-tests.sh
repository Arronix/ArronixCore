#!/usr/bin/env bash

set -uo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
    echo "usage: $0 <current-registry> <repository-root> [previous-registry]" >&2
    exit 2
fi

current_registry=$1
repository_root=$2
previous_registry=${3:-}

validate_registry() {
    registry=$1
    label=$2

    if [[ ! -f "$registry" ]]; then
        echo "error: $label required proof sentinel registry '$registry' is missing." >&2
        return 1
    fi

    awk -F '\t' -v label="$label" '
        function fail(message) {
            printf "error: %s required proof sentinel registry line %d: %s\n", label, FNR, message > "/dev/stderr"
            invalid = 1
        }

        FNR == 1 {
            expected = "# id\tproject\tfull name\tfixture\tmethod\tsource file\tSHA-256 source digest\tsupport source files"
            if ($0 != expected) {
                fail("the canonical header is missing or changed")
            }
        }

        /\r$/ {
            fail("CRLF input is not canonical")
        }

        /^[[:space:]]*$/ || /^#/ {
            next
        }

        {
            if (NF != 8) {
                fail("expected exactly eight tab-separated fields")
                next
            }

            for (field = 1; field <= 8; field++) {
                if ($field == "") {
                    fail("fields must not be empty")
                }
                if ($field ~ /^[[:space:]]/ || $field ~ /[[:space:]]$/) {
                    fail("fields must not have leading or trailing whitespace")
                }
            }

            id = $1
            project = $2
            full_name = $3
            fixture = $4
            method = $5
            source_file = $6
            digest = $7
            support_sources = $8

            if (id !~ /^[a-z0-9]+([.-][a-z0-9]+)*$/) {
                fail("the sentinel id is not canonical: " id)
            }
            if (seen_id[id]++) {
                fail("duplicate sentinel id: " id)
            }
            if (last_id != "" && id <= last_id) {
                fail("sentinel ids must be strictly sorted; " id " follows " last_id)
            }
            last_id = id

            identity = project "\t" full_name
            if (seen_identity[identity]++) {
                fail("duplicate project and full-name identity: " identity)
            }

            if (project !~ /^src\// || project !~ /[.]Tests[.]csproj$/ ||
                project ~ /(^|\/)\.{1,2}($|\/)/ || project ~ /\/\//) {
                fail("the project is not a canonical repository-relative test project path: " project)
            }
            if (full_name !~ /^[A-Za-z_][A-Za-z0-9_.+`]*$/) {
                fail("the NUnit full name is not canonical: " full_name)
            }
            if (fixture !~ /^[A-Za-z_][A-Za-z0-9_.+]*$/) {
                fail("the fixture name is not canonical: " fixture)
            }
            if (method !~ /^[A-Za-z_][A-Za-z0-9_]*$/) {
                fail("the method name is not canonical: " method)
            }
            if (full_name != fixture "." method) {
                fail("the full name does not identify the declared fixture and method: " full_name)
            }
            if (source_file !~ /^src\// || source_file ~ /(^|\/)\.{1,2}($|\/)/ || source_file ~ /\/\//) {
                fail("the source file is not a canonical repository-relative path: " source_file)
            }
            if (length(digest) != 71 || substr(digest, 1, 7) != "sha256:" ||
                substr(digest, 8) ~ /[^0-9a-f]/) {
                fail("the source digest must be a lowercase sha256 value")
            }
            if (support_sources != "-") {
                support_count = split(support_sources, support_entries, ";")
                last_support_path = ""
                delete seen_support_path
                for (support_index = 1; support_index <= support_count; support_index++) {
                    support_entry = support_entries[support_index]
                    separator = index(support_entry, "=")
                    support_path = substr(support_entry, 1, separator - 1)
                    support_digest = substr(support_entry, separator + 1)
                    if (separator <= 1 || support_path !~ /^src\// ||
                        support_path ~ /(^|\/)\.{1,2}($|\/)/ || support_path ~ /\/\// ||
                        support_path ~ /[;=]/) {
                        fail("the support source path is not canonical: " support_entry)
                    }
                    if (length(support_digest) != 71 ||
                        substr(support_digest, 1, 7) != "sha256:" ||
                        substr(support_digest, 8) ~ /[^0-9a-f]/) {
                        fail("the support source digest must be a lowercase sha256 value: " support_entry)
                    }
                    if (seen_support_path[support_path]++) {
                        fail("duplicate support source path: " support_path)
                    }
                    if (support_path == source_file) {
                        fail("the primary source file cannot also be a support source: " support_path)
                    }
                    if (last_support_path != "" && support_path <= last_support_path) {
                        fail("support source paths must be strictly sorted: " support_path)
                    }
                    last_support_path = support_path
                }
            }

            rows++
        }

        END {
            if (FNR == 0) {
                printf "error: %s required proof sentinel registry is empty.\n", label > "/dev/stderr"
                invalid = 1
            }
            if (rows == 0) {
                printf "error: %s required proof sentinel registry contains no stable rows.\n", label > "/dev/stderr"
                invalid = 1
            }
            exit invalid
        }
    ' "$registry"
}

status=0
if ! validate_registry "$current_registry" "current"; then
    status=1
fi

if [[ -n "$previous_registry" ]] && ! validate_registry "$previous_registry" "previous"; then
    status=1
fi

if [[ $status -ne 0 ]]; then
    exit 1
fi

while IFS=$'\t' read -r sentinel_id project full_name fixture method source_file expected_digest support_sources; do
    if [[ -z "$sentinel_id" || "$sentinel_id" == \#* ]]; then
        continue
    fi

    if [[ ! -f "$repository_root/$project" ]]; then
        echo "error: sentinel '$sentinel_id' project '$project' does not exist." >&2
        status=1
    fi
    if [[ ! -f "$repository_root/$source_file" ]]; then
        echo "error: sentinel '$sentinel_id' source file '$source_file' does not exist." >&2
        status=1
    fi
    if [[ "$support_sources" != "-" ]]; then
        IFS=';' read -r -a support_entries <<< "$support_sources"
        for support_entry in "${support_entries[@]}"; do
            support_source=${support_entry%%=*}
            if [[ ! -f "$repository_root/$support_source" ]]; then
                echo "error: sentinel '$sentinel_id' support source file '$support_source' does not exist." >&2
                status=1
            fi
        done
    fi
done < "$current_registry"

if [[ -n "$previous_registry" ]]; then
    if ! awk -F '\t' '
        FNR == NR {
            if ($0 !~ /^#/ && $0 !~ /^[[:space:]]*$/) {
                current[$1] = $0
            }
            next
        }

        $0 !~ /^#/ && $0 !~ /^[[:space:]]*$/ {
            if (!($1 in current)) {
                printf "error: stable proof sentinel %s was removed from the current registry.\n", $1 > "/dev/stderr"
                invalid = 1
            } else if (current[$1] != $0) {
                printf "error: stable proof sentinel %s changed after publication.\n", $1 > "/dev/stderr"
                invalid = 1
            }
            previous_rows++
        }

        END {
            exit invalid
        }
    ' "$current_registry" "$previous_registry"; then
        status=1
    fi
fi

if [[ $status -ne 0 ]]; then
    exit 1
fi

current_rows=$(awk -F '\t' '$0 !~ /^#/ && $0 !~ /^[[:space:]]*$/ { count++ } END { print count + 0 }' "$current_registry")
if [[ -n "$previous_registry" ]]; then
    previous_rows=$(awk -F '\t' '$0 !~ /^#/ && $0 !~ /^[[:space:]]*$/ { count++ } END { print count + 0 }' "$previous_registry")
    echo "Required proof sentinel ratchet: $previous_rows stable row(s), $current_rows current row(s)."
else
    echo "Required proof sentinel bootstrap: $current_rows non-empty stable row(s)."
fi
