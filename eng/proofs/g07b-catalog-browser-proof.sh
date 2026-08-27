#!/usr/bin/env bash

# G07B proof foundation. It owns its external cataloger, package root, client output, SQLite file and
# evidence. It deliberately stops at a documented integration dependency: provider discovery currently
# cannot return an independently contributed cataloger for a kind or disclose its catalog scheme.

set -euo pipefail

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/../.." && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
proof_root="$repository_root/artifacts/g07b-browser-proof"
feed_root="$proof_root/feed"
cache_root="$proof_root/nuget-cache"
package_root="$proof_root/packages"
client_root="$proof_root/client"
state_root="$proof_root/state"
database="$proof_root/store/arronix.db"
evidence_root="$proof_root/evidence"
provider_project="$repository_root/eng/proofs/fixtures/g07b-provider/Proof.Movies.Catalog/Proof.Movies.Catalog.csproj"
port=5225
browser=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --browser) browser=1; shift ;;
        --port) port="$2"; shift 2 ;;
        *) echo "error: unknown argument '$1'." >&2; exit 2 ;;
    esac
done

if [[ "$proof_root" != "$repository_root/artifacts/g07b-browser-proof" ]]; then
    echo "error: refusing to clean an unexpected proof root '$proof_root'." >&2
    exit 2
fi
if [[ ! -x "$dotnet_command" ]] && ! command -v "$dotnet_command" >/dev/null 2>&1; then
    echo "error: .NET command '$dotnet_command' is not executable." >&2
    exit 2
fi

port_listening() {
    python3 - "$1" <<'PY'
import socket, sys
s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.settimeout(.5)
code = s.connect_ex(('127.0.0.1', int(sys.argv[1])))
s.close()
raise SystemExit(0 if code == 0 else 1)
PY
}

if port_listening "$port"; then
    echo "error: port $port is already listening; this proof only drives the API process it starts." >&2
    exit 2
fi

api_pid=""
is_running() { [[ -n "$1" ]] && ps -o state= -p "$1" 2>/dev/null | tr -d ' ' | grep -qv '^Z'; }
stop_api() {
    [[ -z "$api_pid" ]] && return 0
    if is_running "$api_pid"; then kill -TERM "$api_pid" 2>/dev/null || true; fi
    for _ in $(seq 1 30); do is_running "$api_pid" || break; sleep .5; done
    if is_running "$api_pid"; then kill -KILL "$api_pid" 2>/dev/null || true; fi
    for _ in $(seq 1 20); do is_running "$api_pid" || break; sleep .5; done
    if is_running "$api_pid"; then echo "error: proof-owned API pid $api_pid survived shutdown." >&2; return 1; fi
    wait "$api_pid" 2>/dev/null || true
    api_pid=""
}
trap stop_api EXIT INT TERM HUP

rm -rf "$proof_root"
mkdir -p "$feed_root" "$cache_root" "$package_root" "$client_root" "$state_root" "$(dirname "$database")" "$evidence_root"

echo "== build and publish the public packages =="
"$dotnet_command" build "$repository_root/Arronix.sln" --configuration Release -warnaserror
for project in Arronix.Abstractions Arronix.Media.Movies; do
    "$dotnet_command" pack "$repository_root/src/$project/$project.csproj" --configuration Release --no-build --output "$feed_root"
done

echo "== build the proof-only cataloger against the packed contracts =="
rm -rf "$(dirname "$provider_project")/bin" "$(dirname "$provider_project")/obj"
"$dotnet_command" restore "$provider_project" --source "$feed_root" --packages "$cache_root"
"$dotnet_command" build "$provider_project" --configuration Release --no-restore -warnaserror

echo "== stage only published payloads =="
"$dotnet_command" publish "$repository_root/src/Arronix.Plugin.Movies/Arronix.Plugin.Movies.csproj" --configuration Release --no-build --output "$package_root/movies"
"$dotnet_command" publish "$repository_root/src/Arronix.Format.Video/Arronix.Format.Video.csproj" --configuration Release --no-build --output "$package_root/arronix.format.video"
"$dotnet_command" publish "$provider_project" --configuration Release --no-build --output "$package_root/proof.movies.catalog"
"$dotnet_command" publish "$repository_root/src/Arronix.Client/Arronix.Client.csproj" --configuration Release --output "$client_root"

api="$repository_root/src/Arronix.Api/bin/Release/net11.0/Arronix.Api.dll"
ASPNETCORE_URLS="http://127.0.0.1:$port" \
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_CONTENTROOT="$(dirname "$api")" \
Arronix__Plugins__RootFolder="$package_root" \
Arronix__Plugins__StateFolder="$state_root" \
Arronix__Store__DataSource="$database" \
Arronix__Api__ClientRoot="$client_root/wwwroot" \
    "$dotnet_command" "$api" > "$evidence_root/server.log" 2>&1 &
api_pid=$!
address="http://127.0.0.1:$port"
for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null "$address/api" 2>/dev/null; then break; fi
    if ! is_running "$api_pid"; then echo "error: API stopped before readiness; see $evidence_root/server.log" >&2; exit 1; fi
    sleep 1
done
curl -fsS "$address/api/v1/providers?family=Cataloger&kind=movies" > "$evidence_root/providers-for-movies.json"
curl -fsS "$address/api/v1/providers?family=Cataloger" > "$evidence_root/catalogers.json"
curl -fsS "$address/api/v1/client-contracts" > "$evidence_root/client-contracts.json"

# Centralized response contract. The next integration slice must make the kind-filter discover this external
# cataloger and publish CatalogScheme; the harness will not guess the provider id or scheme from internals.
python3 - "$evidence_root/providers-for-movies.json" "$evidence_root/catalogers.json" "$evidence_root/integration-dependency.json" <<'PY'
import json, sys
for_kind, all_catalogers, output = map(__import__('pathlib').Path, sys.argv[1:])
expected = {'provider', 'family', 'descriptor', 'pairedMediaKind', 'catalogScheme'}
all_entries = json.loads(all_catalogers.read_text())
kind_entries = json.loads(for_kind.read_text())
missing = sorted(expected - set(all_entries[0])) if all_entries else sorted(expected)
report = {
  'expectedProviderFields': sorted(expected),
  'kindFilteredEntries': len(kind_entries),
  'catalogerEntries': len(all_entries),
  'missingFromProviderCatalogEntry': missing,
  'blocker': 'Provider discovery must select independent catalogers for a paired media kind and disclose CatalogScheme.'
}
output.write_text(json.dumps(report, indent=2) + '\n')
if len(kind_entries) != 1 or missing:
    print('G07B integration dependency recorded: provider discovery cannot yet identify the proof cataloger.', file=sys.stderr)
    raise SystemExit(3)
PY

# Once discovery supplies the one provider and its scheme, ordinary POST/PUT routes configure revision 1 then
# revision 2. Keeping these request shapes here makes the pending integration explicit rather than encoding
# the missing provider id in the proof.
provider=$(python3 - "$evidence_root/providers-for-movies.json" <<'PY'
import json, sys
print(json.load(open(sys.argv[1]))[0]['provider'])
PY
)
definition=$(printf '{"id":0,"provider":"%s","family":0,"name":"G07B proof","enabled":true,"settings":{"revision":"1"},"mediaKinds":["movies"]}' "$provider")
curl -fsS -H 'content-type: application/json' -d "$definition" "$address/api/v1/providers/definitions" > "$evidence_root/provider-revision-1.json"
id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' < "$evidence_root/provider-revision-1.json")
python3 "$script_directory/g07b-read-store.py" --database "$database" --definition "$id" --phase searched --contract-manifest "$evidence_root/client-contracts.json" --output "$evidence_root/store-before-search.json"

if [[ $browser -eq 1 ]]; then
    node "$script_directory/g07b-catalog-browser-proof.mjs" --address "$address" --evidence "$evidence_root"
fi
