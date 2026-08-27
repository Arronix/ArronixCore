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

wait_for_exit() {
    local pid="$1"
    local attempts="$2"
    for _ in $(seq 1 "$attempts"); do
        is_running "$pid" || return 0
        sleep .5
    done
    ! is_running "$pid"
}

wait_for_port_release() {
    for _ in $(seq 1 20); do
        port_listening "$port" || return 0
        sleep .5
    done
    ! port_listening "$port"
}

stop_api() {
    [[ -z "$api_pid" ]] && return 0
    local pid="$api_pid"
    if is_running "$pid"; then
        kill -TERM "$pid" 2>/dev/null || true
        wait_for_exit "$pid" 30 || true
    fi
    if is_running "$pid"; then
        kill -KILL "$pid" 2>/dev/null || true
        wait_for_exit "$pid" 20 || true
    fi
    if is_running "$pid"; then
        echo "error: proof-owned API pid $pid survived bounded shutdown." >&2
        return 1
    fi
    wait "$api_pid" 2>/dev/null || true
    if ! wait_for_port_release; then
        echo "error: API port $port remained bound after proof-owned pid $pid exited." >&2
        return 1
    fi
    api_pid=""
}

on_signal() {
    local signal="$1"
    trap - EXIT INT TERM HUP
    echo "error: received $signal; stopping the proof-owned API." >&2
    stop_api || true
    exit 128
}

on_exit() {
    local status="$1"
    trap - EXIT INT TERM HUP
    stop_api || true
    exit "$status"
}

trap 'on_exit $?' EXIT
trap 'on_signal INT' INT
trap 'on_signal TERM' TERM
trap 'on_signal HUP' HUP

rm -rf "$proof_root"
mkdir -p "$feed_root" "$cache_root" "$package_root" "$client_root" "$state_root" "$(dirname "$database")" "$evidence_root"

echo "== build and publish the public packages =="
"$dotnet_command" build "$repository_root/Arronix.sln" --configuration Release -warnaserror
for project in Arronix.Abstractions Arronix.Media.Movies; do
    "$dotnet_command" pack "$repository_root/src/$project/$project.csproj" --configuration Release --no-build --output "$feed_root"
done

echo "== build the proof-only cataloger against the packed contracts =="
provider_output_root="$proof_root/provider-build/"
provider_intermediate_root="$proof_root/provider-intermediate/"
provider_properties=(
    "-p:G07BProofOutputRoot=$provider_output_root"
    "-p:G07BProofIntermediateRoot=$provider_intermediate_root"
)
"$dotnet_command" restore "$provider_project" --source "$feed_root" --packages "$cache_root" "${provider_properties[@]}"
"$dotnet_command" build "$provider_project" --configuration Release --no-restore -warnaserror "${provider_properties[@]}"

echo "== stage only published payloads =="
"$dotnet_command" publish "$repository_root/src/Arronix.Plugin.Movies/Arronix.Plugin.Movies.csproj" --configuration Release --no-build --output "$package_root/movies"
"$dotnet_command" publish "$repository_root/src/Arronix.Format.Video/Arronix.Format.Video.csproj" --configuration Release --no-build --output "$package_root/arronix.format.video"
"$dotnet_command" publish "$provider_project" --configuration Release --no-build --output "$package_root/proof.movies.catalog" "${provider_properties[@]}"
"$dotnet_command" publish "$repository_root/src/Arronix.Client/Arronix.Client.csproj" --configuration Release --output "$client_root"

api="$repository_root/src/Arronix.Api/bin/Release/net11.0/Arronix.Api.dll"
ASPNETCORE_URLS="http://127.0.0.1:$port" \
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_CONTENTROOT="$repository_root/src/Arronix.Api" \
Arronix__Plugins__RootFolder="$package_root" \
Arronix__Plugins__StateFolder="$state_root" \
Arronix__Store__DataSource="$database" \
Arronix__Api__ClientRoot="$client_root/wwwroot" \
    "$dotnet_command" "$api" > "$evidence_root/server.log" 2>&1 &
api_pid=$!
address="http://127.0.0.1:$port"
ready=0
for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null "$address/api" 2>/dev/null; then ready=1; break; fi
    if ! is_running "$api_pid"; then echo "error: API stopped before readiness; see $evidence_root/server.log" >&2; exit 1; fi
    sleep 1
done
if [[ $ready -ne 1 ]]; then
    echo "error: API did not become ready within 60 seconds; see $evidence_root/server.log" >&2
    exit 1
fi
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
selected = kind_entries[0] if len(kind_entries) == 1 else None
missing = sorted(expected - set(selected)) if selected else sorted(expected)
mismatches = {} if selected is None else {
  field: selected.get(field) for field, expected_value in {
    'pairedMediaKind': 'movies',
    'catalogScheme': 'proof',
  }.items() if selected.get(field) != expected_value
}
satisfied = selected is not None and not missing and not mismatches
report = {
  'expectedProviderFields': sorted(expected),
  'kindFilteredEntries': len(kind_entries),
  'catalogerEntries': len(all_entries),
  'selectedProviderCatalogEntry': selected,
  'missingFromProviderCatalogEntry': missing,
  'fieldMismatches': mismatches,
  'dependencySatisfied': satisfied,
  'outcome': 'dependency satisfied' if satisfied else 'blocked',
}
if not satisfied:
  report['blocker'] = 'Provider discovery must select the proof cataloger for movies and disclose catalogScheme=proof.'
output.write_text(json.dumps(report, indent=2) + '\n')
if not satisfied:
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
definition=$(printf '{"id":0,"provider":"%s","family":3,"name":"G07B proof","enabled":true,"settings":{"revision":"1"},"mediaKinds":["movies"]}' "$provider")
curl -fsS -H 'content-type: application/json' -d "$definition" "$address/api/v1/providers/definitions" > "$evidence_root/provider-revision-1.json"
id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' < "$evidence_root/provider-revision-1.json")
python3 - "$evidence_root/provider-revision-1.json" <<'PY'
import json, sys
definition = json.load(open(sys.argv[1]))
if definition.get('family') not in (3, 'cataloger', 'Cataloger'):
    raise SystemExit('error: the configured proof definition is not a Cataloger family provider')
PY

# A snapshot is always taken after the phase it names.  In particular, a catalog search deliberately mints
# identity/allocation but must not materialize a record or user facet.  The UI phases use fresh ordinary
# browser contexts; this proof does not claim stale-tab continuity across the restart.
snapshot() {
    local phase="$1"
    local previous="${2:-}"
    local output="$evidence_root/store-$phase.json"
    local arguments=(
        --database "$database"
        --definition "$id"
        --phase "$phase"
        --contract-manifest "$evidence_root/client-contracts.json"
        --output "$output"
    )
    [[ -n "$previous" ]] && arguments+=(--previous "$evidence_root/store-$previous.json")
    python3 "$script_directory/g07b-read-store.py" "${arguments[@]}"
}

run_browser_phase() {
    local phase="$1"
    local item_ref="${2:-}"
    local arguments=(--address "$address" --evidence "$evidence_root" --phase "$phase")
    [[ -n "$item_ref" ]] && arguments+=(--item-ref "$item_ref")
    node "$script_directory/g07b-catalog-browser-proof.mjs" \
        "${arguments[@]}"
}

if [[ $browser -ne 1 ]]; then
    echo "G07B foundation staged. Re-run with --browser after the generic catalog UI is integrated." >&2
    exit 0
fi

run_browser_phase search
snapshot searched

# First add is a visible ordinary-Client action.  The separate API retry proves durable idempotence without
# requiring the UI to expose a redundant Add button after it has already succeeded.
run_browser_phase add
snapshot added searched
item_ref=$(python3 - "$evidence_root/browser-add.json" <<'PY'
import json, re, sys
value = json.load(open(sys.argv[1])).get('itemRef')
if not isinstance(value, str) or not re.fullmatch(r'movie:[1-9][0-9]*', value):
    raise SystemExit('error: first visible Add did not report one complete durable movie reference')
print(value)
PY
)
retry_status=$(curl -sS -o "$evidence_root/api-retry-add.json" -w '%{http_code}' \
    -H 'content-type: application/json' -d '{"catalogId":"proof:42"}' \
    "$address/api/v1/kinds/movies/catalog/items")
python3 - "$retry_status" "$item_ref" "$evidence_root/api-retry-add.json" "$evidence_root/api-retry-add-verification.json" <<'PY'
import json, re, sys
status, expected_ref, body_path, verification_path = sys.argv[1:]
if status != '200':
    raise SystemExit(f'error: idempotent API retry returned HTTP {status}, expected 200')
body = json.load(open(body_path))
try:
    reference = body['item']['ref']
    actual_ref = f"{reference['level']}:{reference['id']}"
except (KeyError, TypeError) as error:
    raise SystemExit(f'error: retry response did not carry the returned item reference: {error}') from error
if reference.get('kind') != 'movies' or not re.fullmatch(r'movie:[1-9][0-9]*', actual_ref) or actual_ref != expected_ref:
    raise SystemExit(f'error: retry response returned {actual_ref!r}, not first visible Add reference {expected_ref!r}')
with open(verification_path, 'w') as output:
    json.dump({'status': int(status), 'itemRef': actual_ref, 'matchesFirstVisibleAdd': True}, output, indent=2)
    output.write('\n')
PY
snapshot retried added

run_browser_phase monitor "$item_ref"
snapshot monitored retried

revision_two=$(printf '{"id":%s,"provider":"%s","family":3,"name":"G07B proof","enabled":true,"settings":{"revision":"2"},"mediaKinds":["movies"]}' "$id" "$provider")
curl -fsS -X PUT -H 'content-type: application/json' -d "$revision_two" \
    "$address/api/v1/providers/definitions/$id" > "$evidence_root/provider-revision-2.json"
run_browser_phase refresh "$item_ref"
snapshot refreshed monitored

stop_api
# A restart uses the persisted state but a new API process.  It never reuses a browser tab from before the
# restart, which keeps the proof claim intentionally narrower than stale-tab continuity.
ASPNETCORE_URLS="http://127.0.0.1:$port" \
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_CONTENTROOT="$repository_root/src/Arronix.Api" \
Arronix__Plugins__RootFolder="$package_root" \
Arronix__Plugins__StateFolder="$state_root" \
Arronix__Store__DataSource="$database" \
Arronix__Api__ClientRoot="$client_root/wwwroot" \
    "$dotnet_command" "$api" > "$evidence_root/server-restart.log" 2>&1 &
api_pid=$!
ready=0
for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null "$address/api" 2>/dev/null; then ready=1; break; fi
    if ! is_running "$api_pid"; then echo "error: API stopped before restart readiness; see $evidence_root/server-restart.log" >&2; exit 1; fi
    sleep 1
done
if [[ $ready -ne 1 ]]; then
    echo "error: API did not become ready after restart within 60 seconds; see $evidence_root/server-restart.log" >&2
    exit 1
fi
run_browser_phase restart "$item_ref"
snapshot restarted refreshed

# A completed run owns one G07B evidence identity.  Its browser count is derived from the separate visible
# phase reports; it is never folded into the independently accepted G07.3=137 or G07A=37 identities.
python3 - "$evidence_root" <<'PY'
import json, sys
from pathlib import Path

root = Path(sys.argv[1])
browser_phases = ['search', 'add', 'monitor', 'refresh', 'restart']
store_phases = ['searched', 'added', 'retried', 'monitored', 'refreshed', 'restarted']
reports = []
for phase in browser_phases:
    report = json.loads((root / f'browser-{phase}.json').read_text())
    if report.get('phase') != phase or report.get('phaseSucceeded') is not True or 'blocker' in report:
        raise SystemExit(f'error: browser evidence for {phase} is not a passing G07B phase')
    if any(entry.get('ok') is not True for entry in report.get('results', [])):
        raise SystemExit(f'error: browser evidence for {phase} contains a failed check')
    reports.append(report)
stores = []
for phase in store_phases:
    report = json.loads((root / f'store-{phase}.json').read_text())
    if report.get('phase') != phase:
        raise SystemExit(f'error: store evidence for {phase} is absent or mislabeled')
    stores.append(report)
retry = json.loads((root / 'api-retry-add-verification.json').read_text())
if retry.get('status') != 200 or retry.get('matchesFirstVisibleAdd') is not True:
    raise SystemExit('error: API retry evidence is not the required HTTP 200 matching-reference result')
discovery = json.loads((root / 'integration-dependency.json').read_text())
if discovery.get('dependencySatisfied') is not True or 'blocker' in discovery:
    raise SystemExit('error: provider discovery evidence is not the required satisfied projection')
summary = {
    'proofIdentity': 'G07B catalog browser proof',
    'browserCheckCount': sum(len(report['results']) for report in reports),
    'browserPhases': browser_phases,
    'storePhases': store_phases,
    'apiRetry': retry,
    'providerDiscovery': discovery,
    'phaseEvidencePassing': True,
}
(root / 'g07b-proof-summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(f"G07B catalog browser proof: {summary['browserCheckCount']} browser checks; provider, phase, and store evidence passing.")
PY
