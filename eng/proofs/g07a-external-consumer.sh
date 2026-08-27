#!/usr/bin/env bash

# G07A - the package-only external consumer.
#
# Builds two extensions written outside this solution, against nothing but packages, and runs them in the
# real host. Everything they compile against arrives through a NuGet feed this script fills by packing,
# into a package cache per consumer that this script creates empty: no project reference, no source
# include, no InternalsVisibleTo, no warm cache, no Host or Client dependency.
#
#   package 1  eng/proofs/fixtures/g07a-media      a short-film media kind, in two assemblies: the domain
#                                                  half a browser is offered, and the entry half
#   package 2  eng/proofs/fixtures/g07a-provider   a cataloger for it, which has only ever seen package 1's
#                                                  published domain package
#
#   DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07a-external-consumer.sh --serve
#
# Options:
#   --serve      leave the server running until interrupted, for the browser half.
#   --port N     listen on N instead of 5224. The mutation run uses N+1.
#
# Everything generated lives under artifacts/g07a and is rebuilt on each run; only the two consumers'
# sources are retained. DOTNET_COMMAND must be the pinned SDK - both consumers carry their own global.json
# so a wrong one fails rather than retargeting.
#
# The browser half stops at an explicit seam: the client-safe assembly is published, served and loadable,
# and the host publishes the client contract this consumer's own domain assembly declares. A serialized
# payload of that consumer's item is G07.2's to define and is not invented here. See
# docs/research/g07/g07a-external-consumer.md.

set -uo pipefail

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/../.." && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
proof_root="$repository_root/artifacts/g07a"
feed_root="$proof_root/feed"
media_cache="$proof_root/nuget-cache/media"
provider_cache="$proof_root/nuget-cache/provider"
package_root="$proof_root/packages"
mutation_root="$proof_root/mutation"
client_root="$proof_root/client"
evidence_root="$proof_root/evidence"
fixture_root="$repository_root/eng/proofs/fixtures"
media_domain="$fixture_root/g07a-media/Northmark.Shorts.Domain/Northmark.Shorts.Domain.csproj"
media_entry="$fixture_root/g07a-media/Northmark.Shorts/Northmark.Shorts.csproj"
provider_project="$fixture_root/g07a-provider/Northmark.Shorts.Catalog/Northmark.Shorts.Catalog.csproj"
port=5224
serve=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --serve) serve=1; shift ;;
        --port) port="$2"; shift 2 ;;
        *) echo "error: unknown argument '$1'." >&2; exit 2 ;;
    esac
done

if ! command -v "$dotnet_command" >/dev/null 2>&1 && [[ ! -x "$dotnet_command" ]]; then
    echo "error: .NET command '$dotnet_command' is not executable." >&2
    exit 2
fi

if [[ "$proof_root" != "$repository_root/artifacts/g07a" ]]; then
    echo "error: refusing to clean an unexpected artifact path '$proof_root'." >&2
    exit 2
fi

if ! command -v python3 >/dev/null 2>&1; then
    echo "error: python3 is required to read what the host published." >&2
    exit 2
fi

cd "$repository_root"
rm -rf "$proof_root"
mkdir -p "$feed_root" "$media_cache" "$provider_cache" "$package_root" "$evidence_root"

server_pid=""
cleanup() {
    if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
        kill "$server_pid" 2>/dev/null
        wait "$server_pid" 2>/dev/null
    fi
}
trap cleanup EXIT

failures=0
check() {
    local description="$1"
    local actual="$2"
    local expected="$3"

    if [[ "$actual" == "$expected" ]]; then
        printf 'ok    %s\n' "$description"
    else
        printf 'FAIL  %s\n      expected: %s\n      actual:   %s\n' "$description" "$expected" "$actual"
        failures=$((failures + 1))
    fi
}

contains() {
    local description="$1"
    local haystack="$2"
    local needle="$3"

    if [[ "$haystack" == *"$needle"* ]]; then
        printf 'ok    %s\n' "$description"
    else
        printf 'FAIL  %s\n      expected to contain: %s\n      actual:   %s\n' \
            "$description" "$needle" "$haystack"
        failures=$((failures + 1))
    fi
}

start_server() {
    local root="$1"
    local listen="$2"
    local log="$3"

    ASPNETCORE_URLS="http://127.0.0.1:$listen" \
    ASPNETCORE_ENVIRONMENT=Production \
    Arronix__Plugins__RootFolder="$root" \
    Arronix__Plugins__StateFolder="$proof_root/state-$listen" \
    Arronix__Api__ClientRoot="$client_root/wwwroot" \
        "$dotnet_command" run \
            --project "$repository_root/src/Arronix.Api/Arronix.Api.csproj" \
            --configuration Release --no-build --no-restore \
            > "$log" 2>&1 &
    server_pid=$!

    for _ in $(seq 1 60); do
        if curl -fsS -o /dev/null "http://127.0.0.1:$listen/api" 2>/dev/null; then
            return 0
        fi
        sleep 1
    done

    echo "error: the server did not come up on $listen. See $log." >&2
    return 1
}

stop_server() {
    if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
        kill "$server_pid" 2>/dev/null
        wait "$server_pid" 2>/dev/null
    fi
    server_pid=""
}

echo "== building this repository =="
if ! "$dotnet_command" build "$repository_root/Arronix.sln" --configuration Release -warnaserror; then
    exit 1
fi

# Read from the consumers' own project files and sources, because the escape hatches this gate rules out
# are declarations rather than behavior: a project reference, a source include reaching into this tree, or
# a visibility grant would each make the rest of this script pass while proving nothing.
echo
echo "== the consumers' declarations =="
declaring() { grep -rlE "$1" "$fixture_root" --include='*.csproj' 2>/dev/null | wc -l | tr -d ' '; }

check "both consumers declare no project reference" "$(declaring '<ProjectReference')" "0"
check "both consumers include no file from outside themselves" \
    "$(declaring '<(Compile|None|Content) Include="[^"]*\.\.')" "0"
check "both consumers name no project in this repository" \
    "$(declaring 'Arronix\.(Host|Client|Api|Common|Plugins|Generators)')" "0"
check "both consumers grant no internals visibility" \
    "$(grep -rl 'InternalsVisibleTo' "$fixture_root" --include='*.csproj' --include='*.cs' 2>/dev/null | wc -l | tr -d ' ')" "0"
check "no assembly in this repository grants either consumer internals visibility" \
    "$(grep -rl 'InternalsVisibleTo.*Northmark' "$repository_root/src" 2>/dev/null | wc -l | tr -d ' ')" "0"
check "each consumer pins the same SDK this repository does" \
    "$(find "$fixture_root" -name global.json | wc -l | tr -d ' ')" "2"

echo
echo "== packing this repository's published packages =="
for project in Arronix.Abstractions Arronix.Sdk Arronix.Format.Video; do
    if ! "$dotnet_command" pack "$repository_root/src/$project/$project.csproj" \
        --configuration Release --no-build --output "$feed_root"; then
        exit 1
    fi
done

# Every consumer build starts from a cleared intermediate directory. A stale obj/ carries the last
# restore's asset file, which is how a run that should have failed for a missing package succeeds instead.
for project in "$media_domain" "$media_entry" "$provider_project"; do
    rm -rf "$(dirname "$project")/obj" "$(dirname "$project")/bin"
done

# Before the domain package exists, the provider must have no way to reach the type it closes over. Its
# own package cache is empty and its feed holds no domain package, so a success here means some other
# source answered - which is the warm-cache fallback this proof exists to rule out.
echo
echo "== the provider cannot reach the domain before it is published =="
early=$("$dotnet_command" restore "$provider_project" --packages "$provider_cache" 2>&1)
early_status=$?
check "restoring the provider first fails" "$early_status" "1"
contains "and fails because the domain package is not there" "$early" "Northmark.Shorts.Domain"
rm -rf "$(dirname "$provider_project")/obj"

echo
echo "== package 1: the media package =="
if ! "$dotnet_command" restore "$media_domain" --packages "$media_cache" \
    || ! "$dotnet_command" build "$media_domain" --configuration Release --no-restore -warnaserror \
    || ! "$dotnet_command" pack "$media_domain" --configuration Release --no-build --output "$feed_root" \
    || ! "$dotnet_command" restore "$media_entry" --packages "$media_cache" \
    || ! "$dotnet_command" build "$media_entry" --configuration Release --no-restore -warnaserror; then
    exit 1
fi

echo
echo "== package 2: the provider package =="
if ! "$dotnet_command" restore "$provider_project" --packages "$provider_cache" \
    || ! "$dotnet_command" build "$provider_project" --configuration Release --no-restore -warnaserror; then
    exit 1
fi

# Staged by publishing, never by copying a build directory, for the reason the G07.1 script already gives:
# MSBuild does not prune an assembly a removed reference stopped producing.
echo
echo "== staging the installation =="
if ! "$dotnet_command" publish "$media_entry" --configuration Release --no-build \
        --output "$package_root/northmark.shorts" \
    || ! "$dotnet_command" publish "$provider_project" --configuration Release --no-build \
        --output "$package_root/northmark.shorts.catalog" \
    || ! "$dotnet_command" publish "$repository_root/src/Arronix.Format.Video/Arronix.Format.Video.csproj" \
        --configuration Release --no-build --output "$package_root/arronix.format.video"; then
    exit 1
fi

payload_of() { ls "$package_root/$1" | tr '\n' ',' ; }

echo
echo "== what each payload carries =="
check "the media package publishes its own domain assembly" \
    "$(test -f "$package_root/northmark.shorts/Northmark.Shorts.Domain.dll" && echo yes || echo no)" "yes"
check "the media package carries no copy of the format it depends on" \
    "$(test -e "$package_root/northmark.shorts/Arronix.Format.Video.dll" && echo yes || echo no)" "no"
check "the provider carries no copy of the domain it pairs with" \
    "$(test -e "$package_root/northmark.shorts.catalog/Northmark.Shorts.Domain.dll" && echo yes || echo no)" "no"
check "the provider carries no copy of the format either" \
    "$(test -e "$package_root/northmark.shorts.catalog/Arronix.Format.Video.dll" && echo yes || echo no)" "no"

# The identity itself, not only the absence of a duplicate. The provider compiled against the assembly its
# own package cache received; the installation carries the one the media package published. If those are
# not the same bytes, ICataloger<ShortFilm> closed over a type the admitted kind does not supply.
sha_of() { shasum -a 256 "$1" | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]'; }
compiled_against=$(sha_of \
    "$provider_cache/northmark.shorts.domain/1.0.0/lib/net11.0/Northmark.Shorts.Domain.dll")
installed_domain=$(sha_of "$package_root/northmark.shorts/Northmark.Shorts.Domain.dll")
check "the provider compiled against the exact assembly the media package installs" \
    "$compiled_against" "$installed_domain"

echo
echo "== publishing the browser client =="
rm -rf "$repository_root/src/Arronix.Client/obj" "$repository_root/src/Arronix.Client/bin"
if ! "$dotnet_command" publish "$repository_root/src/Arronix.Client/Arronix.Client.csproj" \
    --configuration Release --output "$client_root"; then
    exit 1
fi

echo
echo "== the installation =="
if ! start_server "$package_root" "$port" "$evidence_root/server.log"; then
    exit 1
fi

address="http://127.0.0.1:$port"
curl -fsS "$address/api/v1/client-contracts" > "$evidence_root/manifest.json"
curl -fsS "$address/api/v1/plugins" > "$evidence_root/plugins.json"
curl -fsS "$address/api/v1/kinds" > "$evidence_root/kinds.json"
curl -fsS "$address/api/v1/providers" > "$evidence_root/providers.json"

summary=$(python3 "$script_directory/g07a-read-installation.py" \
    "$evidence_root/manifest.json" "$evidence_root/plugins.json" \
    "$evidence_root/kinds.json" "$evidence_root/providers.json") || exit 1
eval "$summary"

status_of() { curl -s -o /dev/null -w '%{http_code}' "$1"; }

check "every installed package is active" "$active" \
    "arronix.format.video,northmark.shorts,northmark.shorts.catalog"
check "nothing is quarantined" "$not_active" ""
check "the media package's closure names its dependency first" "$media_closure" \
    "arronix.format.video,northmark.shorts"
check "the host refuses no facet" "$refused_packages" "0"

# The kind, its owned field and its cataloger: three things Host could only have from the generated shape,
# the compiled declaration and the closed contract, in a package it has no compile-time knowledge of.
check "the external kind is the only one installed" "$kind_names" "shorts"
check "the field the external item owns reached Host's own descriptor" "$owned_field" "premiere"
check "the separately built cataloger is registered against it" "$catalogers" \
    "northmark.shorts.catalog:northmark-shorts"

# The generated client contract, read back from what the host publishes rather than from the build that
# produced it. This is the external package's own generated metadata, not a first-party package's.
check "the media package offers exactly its domain assembly" "$media_file" "Northmark.Shorts.Domain.dll"
check "and offers it under the shared identity both packages bound to" "$media_identity" \
    "Northmark.Shorts.Domain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
check "and the identity it offers is the assembly the provider compiled against" \
    "$media_hash" "$compiled_against"
check "the client-safe assembly declares its generated entry point" "$entry_point" \
    "Northmark.Shorts.ShortFilmClientContractEntryPointAttribute"
check "the entry point names the external item type" "$entity_type" "Northmark.Shorts.ShortFilm"
check "the entry point publishes a generated-metadata hash" "${#metadata_hash}" "64"
check "the entry point publishes a projection-schema hash" "${#projection_hash}" "64"

check "the client-safe assembly is served at its content address" \
    "$(status_of "$address/api/v1/client-contracts/northmark.shorts/$media_hash/$media_file")" "200"
check "the entry assembly is not offered to a client" \
    "$(status_of "$address/api/v1/client-contracts/northmark.shorts/$media_hash/Northmark.Shorts.dll")" "404"
check "the provider package offers nothing to a client" \
    "$(status_of "$address/api/v1/client-contracts/northmark.shorts.catalog/$media_hash/$media_file")" "404"

served_hash=$(curl -fsS "$address/api/v1/client-contracts/northmark.shorts/$media_hash/$media_file" \
    | shasum -a 256 | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')
check "the served bytes hash to the published content hash" "$served_hash" "$media_hash"

staged_hash=$(shasum -a 256 "$package_root/northmark.shorts/$media_file" \
    | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')
check "the served bytes are the staged package's bytes" "$staged_hash" "$media_hash"

client_external_assemblies=$(find "$client_root/wwwroot/_framework" \
    \( -name 'Northmark.*' -o -name 'Arronix.Format.*' -o -name 'Arronix.Media.*' \) 2>/dev/null \
    | wc -l | tr -d ' ')
check "the published client carries no media or format assembly of its own" "$client_external_assemblies" "0"

{
    echo "address=$address"
    echo "contractIdentity=$contract_identity"
    echo "clientSafe=$media_file $media_hash $media_identity"
    echo "entryPoint=$entry_point"
    echo "entityType=$entity_type"
    echo "generatedMetadataHash=$metadata_hash"
    echo "projectionSchemaHash=$projection_hash"
    echo "active=$active"
    echo "cataloger=$catalogers"
} > "$evidence_root/server-half.txt"

if [[ $serve -eq 0 ]]; then
    stop_server
fi

# One mutation, on a copy of the staged installation: the provider carrying the contract it is supposed to
# consume through its dependency. Without it, every check above would also pass for a provider that shipped
# its own copy of the domain assembly and closed its cataloger over a second, unrelated CLR type.
echo
echo "== mutation: the provider carries a private copy of the domain =="
mutation_port=$((port + 1))
rm -rf "$mutation_root"
cp -R "$package_root" "$mutation_root"
cp "$package_root/northmark.shorts/Northmark.Shorts.Domain.dll" "$mutation_root/northmark.shorts.catalog/"

mutation_pid=""
if [[ $serve -eq 1 ]]; then
    mutation_pid=$server_pid
    server_pid=""
fi

if start_server "$mutation_root" "$mutation_port" "$evidence_root/mutation.log"; then
    curl -fsS "http://127.0.0.1:$mutation_port/api/v1/plugins" > "$evidence_root/mutation-plugins.json"
    mutation=$(python3 "$script_directory/g07a-read-installation.py" \
        --refusal "$evidence_root/mutation-plugins.json" northmark.shorts.catalog) || exit 1
    eval "$mutation"

    check "the provider is refused" "$state" "Quarantined"
    contains "and the refusal names the private copy" "$defects" "is a private copy of shared contract"
    contains "and names the assembly it duplicated" "$defects" "Northmark.Shorts.Domain"
    contains "and names the package that publishes it" "$defects" "published by 'northmark.shorts'"

    media_state=$(python3 "$script_directory/g07a-read-installation.py" \
        --refusal "$evidence_root/mutation-plugins.json" northmark.shorts | sed -n 's/^state=//p')
    check "and the package it duplicated is unaffected" "$media_state" "Active"

    stop_server
else
    failures=$((failures + 1))
fi

if [[ -n "$mutation_pid" ]]; then
    server_pid=$mutation_pid
fi

echo
cat "$evidence_root/server-half.txt"
echo
echo "Evidence: $evidence_root"

if [[ $failures -ne 0 ]]; then
    echo "error: $failures check(s) failed." >&2
    exit 1
fi

if [[ $serve -eq 1 ]]; then
    echo
    echo "Browser half: open $address/contracts and read the text of '#contract-proof'."
    echo "It reports whether this browser verified and loaded Northmark.Shorts.Domain.dll from its own"
    echo "bytes. Projecting a serialized short film is G07.2 work and is deliberately absent."
    echo "Press Ctrl+C to stop the server."
    wait "$server_pid"
fi
