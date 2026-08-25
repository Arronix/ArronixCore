#!/usr/bin/env bash

# G07.1 — publish, cache, and load the exact client-safe contract package.
#
# Stands up the real thing: the real packages, staged by the real publish; the real server, with its real
# admission pipeline and no test composition anywhere in it; and the real browser client, published for
# WebAssembly. Nothing here is a fixture host, a synthetic package folder or a test-only route.
#
# It proves the server half over HTTP, then leaves both halves running for the browser half. The browser
# half is operator-run because this repository's hermetic test rail carries no browser toolchain: point a
# browser at the printed client address and read the text of '#contract-proof', which holds the client's
# complete load report verbatim - the same report the page itself renders.
#
#   DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07-client-contracts.sh --serve
#
# Options:
#   --serve      leave both halves running until interrupted, for the browser half.
#   --port N     listen on N instead of 5223. The client is served on N+1.
#
# The client is served on its own origin by the WebAssembly development host, and the API is told to admit
# that origin. That is a configuration this server already supports on purpose - a client behind a content
# delivery network or inside a native shell is the same shape - and it is used here because the PUBLISHED
# client cannot currently start at all on this SDK: .NET 11 preview 7 throws an InvalidCastException from
# Microsoft.AspNetCore.Components.Infrastructure.PersistentServicesRegistry during
# WebAssemblyHost.RunAsyncCore, before any Arronix code runs. That failure predates this work, reproduces
# with every Arronix service registration removed, and is recorded in docs/research/g07. The routes under
# proof are served by the real API either way.
#
# What this proof can and cannot reach today is a property of the host, not of this script. A package with
# an entry assembly cannot be activated by any production Arronix host yet, because no ICacheProvider,
# ITelemetryEmitter, IEventPublisher, IHostRuntimeInfo or IOperatingSystemInfo implementation exists
# outside the test projects. So the installation below publishes the contract-only video package and
# quarantines movies, and the script asserts exactly that rather than papering over it. The equivalent
# proof over a complete Movies-and-video installation lives in Arronix.Host.Tests, which supplies those
# five services.

set -uo pipefail

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/../.." && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
proof_root="$repository_root/artifacts/g07"
package_root="$proof_root/packages"
client_root="$proof_root/client"
evidence_root="$proof_root/evidence"
port=5223
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

if [[ "$proof_root" != "$repository_root/artifacts/g07" ]]; then
    echo "error: refusing to clean an unexpected artifact path '$proof_root'." >&2
    exit 2
fi

if ! command -v python3 >/dev/null 2>&1; then
    echo "error: python3 is required to read the published manifest." >&2
    exit 2
fi

cd "$repository_root"
rm -rf "$proof_root"
mkdir -p "$package_root" "$evidence_root"

server_pid=""
client_pid=""
client_override=""
cleanup() {
    for pid in "$client_pid" "$server_pid"; do
        if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null
            wait "$pid" 2>/dev/null
        fi
    done

    if [[ -n "$client_override" && -f "$client_override" ]]; then
        rm -f "$client_override"
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

echo "== building =="
if ! "$dotnet_command" build "$repository_root/Arronix.sln" --configuration Release -warnaserror; then
    exit 1
fi

# Staged by publishing, never by copying a build directory. MSBuild does not prune an assembly a removed
# reference stopped producing, so a recursive copy can carry a file the package no longer depends on into
# the payload - and here that file would be one a browser could be offered.
echo "== staging packages =="
for package in "movies:src/Arronix.Plugin.Movies/Arronix.Plugin.Movies.csproj" \
               "arronix.format.video:src/Arronix.Format.Video/Arronix.Format.Video.csproj"; do
    id="${package%%:*}"
    project="${package#*:}"
    if ! "$dotnet_command" publish "$repository_root/$project" \
        --configuration Release --no-build \
        --output "$package_root/$id"; then
        exit 1
    fi
done

echo "== publishing the browser client =="
if ! "$dotnet_command" publish "$repository_root/src/Arronix.Client/Arronix.Client.csproj" \
    --configuration Release --output "$client_root"; then
    exit 1
fi

client_port=$((port + 1))
client_address="http://127.0.0.1:$client_port"

# The one thing a browser cannot be told any other way: which host to talk to. Written beside the client's
# own settings, removed on exit, and ignored by Git, so a proof run leaves the tree as it found it.
client_override="$repository_root/src/Arronix.Client/wwwroot/appsettings.Development.json"
cat > "$client_override" <<JSON
{
  "Arronix": {
    "Client": {
      "ServerAddress": "http://127.0.0.1:$port/"
    }
  }
}
JSON

echo "== starting the server =="
export Arronix__Api__AllowedOrigins__0="$client_address"
export ASPNETCORE_URLS="http://127.0.0.1:$port"
export ASPNETCORE_ENVIRONMENT=Production
export Arronix__Plugins__RootFolder="$package_root"
export Arronix__Plugins__StateFolder="$proof_root/state"
export Arronix__Api__ClientRoot="$client_root/wwwroot"

"$dotnet_command" run \
    --project "$repository_root/src/Arronix.Api/Arronix.Api.csproj" \
    --configuration Release --no-build --no-restore \
    > "$evidence_root/server.log" 2>&1 &
server_pid=$!

address="http://127.0.0.1:$port"
for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null "$address/api" 2>/dev/null; then
        break
    fi
    sleep 1
done

if ! curl -fsS -o /dev/null "$address/api" 2>/dev/null; then
    echo "error: the server did not come up. See $evidence_root/server.log." >&2
    exit 1
fi

echo
echo "== server half =="
curl -fsS "$address/api/v1/client-contracts" > "$evidence_root/manifest.json"
curl -fsS "$address/api/v1/plugins" > "$evidence_root/plugins.json"

summary=$(python3 "$script_directory/g07-read-manifest.py" \
    "$evidence_root/manifest.json" "$evidence_root/plugins.json") || exit 1
eval "$summary"

status_of() { curl -s -o /dev/null -w '%{http_code}' "$1"; }
header_of() { curl -s -D - -o /dev/null "$1" | tr -d '\r' | awk -v key="$2" 'tolower($1)==key { $1=""; sub(/^ /, ""); print }'; }

check "the video client contract is served at its content address" \
    "$(status_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file")" "200"

# The facet rule, from the outside. Every file named below exists in a package folder this host read.
check "a file the package does not offer is not served" \
    "$(status_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/Arronix.Abstractions.dll")" "404"
check "an entry assembly is not offered to a client" \
    "$(status_of "$address/api/v1/client-contracts/movies/$video_hash/Arronix.Plugin.Movies.dll")" "404"
check "an installed package that is not Active offers nothing" \
    "$(status_of "$address/api/v1/client-contracts/movies/$video_hash/Arronix.Media.Movies.dll")" "404"
check "a package this host never installed offers nothing" \
    "$(status_of "$address/api/v1/client-contracts/books/$video_hash/$video_file")" "404"

# A superseded address is Gone rather than Not Found, because the file is still published - at a different
# address - and re-reading the manifest is the recovery.
check "a stale content address is refused as superseded" \
    "$(status_of "$address/api/v1/client-contracts/arronix.format.video/0000000000000000000000000000000000000000000000000000000000000000/$video_file")" "410"

served_hash=$(curl -fsS "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file" \
    | shasum -a 256 | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')
check "the served bytes hash to the published content hash" "$served_hash" "$video_hash"

staged_hash=$(shasum -a 256 "$package_root/arronix.format.video/$video_file" | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')
check "the served bytes are the staged package's bytes" "$staged_hash" "$video_hash"

check "the manifest is served uncacheable" \
    "$(header_of "$address/api/v1/client-contracts" "cache-control:")" \
    "no-cache, no-store, must-revalidate"
check "a content address is served immutable" \
    "$(header_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file" "cache-control:")" \
    "public, max-age=31536000, immutable"
check "a content address states the identity it will bind as" \
    "$(header_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file" "arronix-assembly-identity:")" \
    "$video_identity"

# What a browser downloads is the published output, so the rule is read from the output rather than from
# the project file.
client_media_assemblies=$(find "$client_root/wwwroot/_framework" \
    \( -name 'Arronix.Media.*' -o -name 'Arronix.Format.*' -o -name 'Arronix.Plugin.*' \) 2>/dev/null | wc -l | tr -d ' ')
check "the published client carries no media or format assembly" "$client_media_assemblies" "0"

{
    echo "address=$address"
    echo "contractIdentity=$contract_identity"
    echo "installationHash=$installation_hash"
    echo "publishedPackages=$published_packages"
    echo "video=$video_file $video_hash $video_identity"
    echo "quarantined=$quarantined"
} > "$evidence_root/server-half.txt"

echo
cat "$evidence_root/server-half.txt"
echo
echo "Evidence: $evidence_root"

if [[ $failures -ne 0 ]]; then
    echo "error: $failures server-half check(s) failed." >&2
    exit 1
fi

if [[ $serve -eq 1 ]]; then
    echo
    echo "== starting the client =="
    "$dotnet_command" run \
        --project "$repository_root/src/Arronix.Client/Arronix.Client.csproj" \
        --configuration Debug --urls "$client_address" \
        > "$evidence_root/client.log" 2>&1 &
    client_pid=$!

    for _ in $(seq 1 90); do
        if curl -fsS -o /dev/null "$client_address/" 2>/dev/null; then
            break
        fi
        sleep 1
    done

    if ! curl -fsS -o /dev/null "$client_address/" 2>/dev/null; then
        echo "error: the client did not come up. See $evidence_root/client.log." >&2
        exit 1
    fi

    echo
    echo "Browser half: open $client_address/contracts and read the text of '#contract-proof'."
    echo "Press Ctrl+C to stop both."
    wait "$client_pid"
fi
