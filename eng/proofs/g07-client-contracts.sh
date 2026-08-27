#!/usr/bin/env bash

# G07 — publish, cache and load the exact client-safe contract package (G07.1), read one serialized entity
# through it and render what it projected (G07.2), then update, remove and hold a tab open across both
# (G07.3).
#
# Stands up the real thing: the real packages, staged by the real publish; the real server, with its real
# admission pipeline and no test composition anywhere in it; and the real browser client, published for
# WebAssembly. Nothing here is a fixture host, a synthetic package folder or a test-only route.
#
# It proves the server half over HTTP, then leaves the server running for the browser half. The browser
# half is driven by eng/proofs/g07-browser-proof.mjs, which needs a browser toolchain this repository's
# hermetic test rail does not carry; --browser runs it when one is installed, and without it the same
# checks can be read by hand from the identifiers the page exposes.
#
#   DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07-client-contracts.sh --serve
#   DOTNET_COMMAND=/usr/local/share/dotnet/dotnet bash eng/proofs/g07-client-contracts.sh --browser --lifecycle
#
# What the browser reads, all of it rendered rather than exposed through script interop:
#   #contract-proof              the complete load report (G07.1)
#   #contract-payload            the payload address, as a path on this host
#   #project-contract-payload    fetches it and projects it through the admitted contract
#   #projection-status           the outcome, by name
#   #projected-entity-type       the entity type the contract read the payload into
#   #projection-proof            the whole projection as a proof harness reads it
#   [data-field-id]              one element per projected field, carrying its rendered value
#   #reload-contracts            the ordinary in-page reload (G07.3)
#   #contract-observation        which observation is on screen, so a driven reload can be waited for
#   #contract-orphans            what this page holds which the installation no longer names
#   [data-assembly]              one element per assembly, carrying its outcome and byte source
#
# Options:
#   --serve      leave the server running until interrupted, for the browser half.
#   --browser    run the G07.2 browser half against the server this script started.
#   --lifecycle  run the G07.3 lifecycle matrix, which restarts the API over three installations itself.
#   --port N     listen on N instead of 5223.
#
# Everything this script generates lives under artifacts/g07. It writes nothing into src, and the client it
# serves is the client the API serves in a real deployment: one origin, no cross-origin allowance, no
# development host.
#
# The client published here is the client an ordinary `dotnet publish` produces. This script passes no
# publish property of its own, deliberately: an artifact that only starts when a proof script builds it is
# not the artifact anybody deploys. Trimming is disabled in Arronix.Client.csproj, where the decision and
# its evidence live; see docs/research/g07.
#
# The installation is a complete one: the contract-only video package and the movies package, which has an
# entry assembly and activates. Both are expected Active and both publish a client facet. The manifest
# lists packages by identifier; the load order is each package's own closure.
#
# Payloads are staged once and the installation is composed from them, so the lifecycle half can recompose
# it between restarts without republishing. The movies update is a second build of the same source -
# identical CLR identity and schema, different module identifier and bytes - made by rebuilding the shared
# contract assembly alone with determinism off. The deterministic rebuild afterwards is asserted to
# reproduce the first build exactly, so v2 is a rebuild rather than an edit.
#
# The projection half needs one serialized entity, and it is served as an ordinary static file from the
# published client tree - proof-only input, staged here rather than published by an API route this product
# does not have. eng/proofs/fixtures/g07/movie.json is written by the movies contract's own Serialize and
# held to it by MovieFixtureTests, so what the browser reads is what the contract writes. Neither the Host
# nor the Client gains a dependency on it: the client asks for a path, and any package's fixture would do.

set -uo pipefail

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/../.." && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
proof_root="$repository_root/artifacts/g07"
package_root="$proof_root/packages"
stage_root="$proof_root/stage"
client_root="$proof_root/client"
evidence_root="$proof_root/evidence"
port=5223
serve=0
browser=0
lifecycle=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --serve) serve=1; shift ;;
        --browser) browser=1; shift ;;
        --lifecycle) lifecycle=1; shift ;;
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

# Whether anything holds the port, using bash's own /dev/tcp so no lsof, ss or netstat is needed. That the
# probe works at all is asserted in the server half against this script's own server.
port_is_held() {
    (exec 3<>"/dev/tcp/127.0.0.1/$port") >/dev/null 2>&1
}

# Refused, never cleared, and before anything is built: this proof is not entitled to kill what is there.
if port_is_held; then
    echo "error: port $port is already in use. This proof owns that origin for the length of a run, so it" >&2
    echo "       will not touch what is there. Stop it, or re-run with --port on a free one." >&2
    exit 2
fi

cd "$repository_root"
rm -rf "$proof_root"
mkdir -p "$package_root" "$stage_root" "$evidence_root"

server_pid=""
server_started=0

# How far this script has already escalated against its server: 0 none, 1 SIGTERM sent, 2 SIGKILL sent. A
# retry after a failed stop resumes from here rather than spending the graceful budget a second time.
server_escalation=0

# Whether the owned pid is still running. `kill -0` is not that question: it stays true for a child that has
# exited and is waiting to be reaped, so a loop built on it burns its budget on an ordinary shutdown and
# then escalates against a corpse. The process state answers it - anything starting Z is finished.
owned_is_running() {
    local state
    state=$(ps -o state= -p "$1" 2>/dev/null | tr -d '[:space:]')

    [[ -n "$state" && "$state" != Z* ]]
}

# Stops exactly the process this script started, bounded at every step, and leaves the port free.
#
# Bounded matters: a blocking `wait` on a process that ignores SIGTERM never returns, so the escalation
# after it would never run. TERM, poll a fixed budget, KILL, poll again, and only reap once it has exited.
# The pid is always the one this script launched; a listener it did not start is never signalled.
stop_server() {
    local owned="$server_pid"
    local waited

    if [[ -n "$owned" ]]; then
        if owned_is_running "$owned"; then
            # A signal that fails against a process which is still there is a real failure; one that fails
            # because it has just exited is the answer we wanted. The handle is kept on every failure path
            # below, so the trap - or a later call - still has something to escalate against.
            if [[ $server_escalation -eq 0 ]]; then
                if ! kill -TERM "$owned" 2>/dev/null && owned_is_running "$owned"; then
                    echo "error: could not signal the server this script started (pid $owned)." >&2
                    return 1
                fi

                server_escalation=1

                waited=0
                while [[ $waited -lt 30 ]] && owned_is_running "$owned"; do
                    sleep 0.5
                    waited=$((waited + 1))
                done
            fi

            if owned_is_running "$owned"; then
                if ! kill -KILL "$owned" 2>/dev/null && owned_is_running "$owned"; then
                    echo "error: could not kill the server this script started (pid $owned)." >&2
                    return 1
                fi

                server_escalation=2

                waited=0
                while [[ $waited -lt 20 ]] && owned_is_running "$owned"; do
                    sleep 0.5
                    waited=$((waited + 1))
                done
            fi
        fi

        # A surviving owned process is a failure whatever the port says, and the handle stays set.
        if owned_is_running "$owned"; then
            echo "error: the server this script started (pid $owned) is still running." >&2
            return 1
        fi

        # Reaps a process already known to have exited, so this cannot block. Only now is the handle spent.
        wait "$owned" 2>/dev/null
        server_pid=""
        server_escalation=0
    fi

    # Nothing was started, so there is no port of ours to free and whatever holds it is not ours to report.
    if [[ $server_started -eq 0 ]]; then
        return 0
    fi

    waited=0
    while [[ $waited -lt 60 ]] && port_is_held; do
        sleep 0.5
        waited=$((waited + 1))
    done

    if port_is_held; then
        echo "error: port $port is still held after this script's server was stopped." >&2
        return 1
    fi

    return 0
}

# The same helper on the way out. An EXIT trap otherwise leaves the status that entered it, so a run whose
# checks passed would report success while its server was still up. A failure that arrived first is kept,
# and the trap is disarmed before exiting so it cannot re-enter itself.
#
# Bounded retries, because a signal that failed once may be delivered on the next attempt and the handle is
# retained across a failed stop. What this does not promise is a reap: a process this script may not signal
# at all keeps running, and the most that is promised is a bounded number of attempts and a non-zero exit
# naming what was left.
on_exit() {
    local status=$?
    local attempt
    local had_stop_failure=0

    trap - EXIT

    for attempt in 1 2 3; do
        if stop_server; then
            break
        fi

        had_stop_failure=1
    done

    # A retry that reaps still leaves a failed attempt behind, and a run that could not stop its own server
    # first time is not a clean run. The transient failure is what this reports; the retry is what stops it
    # becoming a leak.
    if [[ $had_stop_failure -ne 0 && $status -eq 0 ]]; then
        status=1
    fi

    exit "$status"
}
trap on_exit EXIT

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
movies_project="src/Arronix.Plugin.Movies/Arronix.Plugin.Movies.csproj"
movies_domain="src/Arronix.Media.Movies/Arronix.Media.Movies.csproj"
movies_contract="src/Arronix.Media.Movies/bin/Release/net11.0/Arronix.Media.Movies.dll"

for package in "movies-v1:$movies_project" \
               "arronix.format.video:src/Arronix.Format.Video/Arronix.Format.Video.csproj"; do
    id="${package%%:*}"
    project="${package#*:}"
    if ! "$dotnet_command" publish "$repository_root/$project" \
        --configuration Release --no-build \
        --output "$stage_root/$id"; then
        exit 1
    fi
done

# The update: a second build of the same source. Only the shared contract assembly is rebuilt, and with
# determinism off, so its module identifier and bytes move while everything else in the payload stays put.
echo "== staging the movies update =="
if ! "$dotnet_command" build "$repository_root/$movies_domain" --configuration Release \
    -t:Rebuild -p:Deterministic=false -p:BuildProjectReferences=false; then
    exit 1
fi

if ! "$dotnet_command" publish "$repository_root/$movies_project" \
    --configuration Release --no-build --output "$stage_root/movies-v2"; then
    exit 1
fi

# Put the working tree back. A deterministic rebuild of unchanged source reproduces the first build exactly,
# which is asserted below rather than assumed.
if ! "$dotnet_command" build "$repository_root/$movies_domain" --configuration Release \
    -t:Rebuild -p:BuildProjectReferences=false; then
    exit 1
fi

# The installation the server reads. The lifecycle half recomposes it from these same staged payloads.
echo "== composing the installation =="
cp -R "$stage_root/movies-v1" "$package_root/movies"
cp -R "$stage_root/arronix.format.video" "$package_root/arronix.format.video"

# Published from a clean intermediate directory. Blazor's static web asset and trimming state is cached in
# obj/, and a directory that has seen several publishes with different properties produces an output that
# matches none of them - which is exactly how a trimming failure once looked like a code failure.
echo "== publishing the browser client =="
rm -rf "$repository_root/src/Arronix.Client/obj" "$repository_root/src/Arronix.Client/bin"
if ! "$dotnet_command" publish "$repository_root/src/Arronix.Client/Arronix.Client.csproj" \
    --configuration Release --output "$client_root"; then
    exit 1
fi

# Proof input, served as an ordinary static file. Copied rather than generated here: the file is written by
# the contract's own writer and held to it by a test, so a second way of producing it would be a second
# answer nothing checks.
echo "== staging the projection fixture =="
fixture_source="$repository_root/eng/proofs/fixtures/g07/movie.json"
fixture_served="$client_root/wwwroot/fixtures/g07/movie.json"

if [[ ! -f "$fixture_source" ]]; then
    echo "error: $fixture_source is missing. Regenerate it with ARRONIX_REGENERATE_G07_FIXTURE=1." >&2
    exit 1
fi

mkdir -p "$(dirname "$fixture_served")"
cp "$fixture_source" "$fixture_served"

# Again, because minutes of building passed since the first check and the port is shared with everything
# else on this machine.
if port_is_held; then
    echo "error: port $port was taken while this proof was building. Re-run with --port on a free one." >&2
    exit 2
fi

# The built application, launched directly. `dotnet run` is a launcher whose child holds the port, so $!
# would be a parent whose death leaves the server behind. Running the assembly makes the pid the server.
api_dll="$repository_root/src/Arronix.Api/bin/Release/net11.0/Arronix.Api.dll"

if [[ ! -f "$api_dll" ]]; then
    echo "error: $api_dll is missing. The solution build above should have produced it." >&2
    exit 1
fi

echo "== starting the server =="
export ASPNETCORE_URLS="http://127.0.0.1:$port"
export ASPNETCORE_ENVIRONMENT=Production
# The application reads its own appsettings.json from the content root, which is the working directory
# unless it is told otherwise. Stated, because this launches the assembly rather than the project.
export ASPNETCORE_CONTENTROOT="$repository_root/src/Arronix.Api"
export Arronix__Plugins__RootFolder="$package_root"
export Arronix__Plugins__StateFolder="$proof_root/state"
export Arronix__Api__ClientRoot="$client_root/wwwroot"

"$dotnet_command" "$api_dll" > "$evidence_root/server.log" 2>&1 &
server_pid=$!
server_started=1

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

# The probe the preflight rests on, against a socket this script knows is held: a shell whose /dev/tcp did
# not work would report every port free and the preflight would pass vacuously.
check "the port probe sees this script's own server" "$(port_is_held && echo held || echo free)" "held"

check "both installed packages are active" "$active" "arronix.format.video,movies"
check "nothing is quarantined" "$not_active" ""
check "the manifest publishes both facets, by identifier" "$package_order" "arronix.format.video,movies"

# The load order is each package's own closure, which is where dependency-first is the contract.
check "the video closure is itself" "$video_closure" "arronix.format.video"
check "the movies closure names its dependency first" "$movies_closure" "arronix.format.video,movies"
check "the manifest refuses no facet" "$refused_packages" "0"
check "video offers its contract assembly" "$video_file" "Arronix.Format.Video.dll"
check "movies offers its contract assembly" "$movies_file" "Arronix.Media.Movies.dll"

check "the video client contract is served at its content address" \
    "$(status_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file")" "200"
check "the movies client contract is served at its content address" \
    "$(status_of "$address/api/v1/client-contracts/movies/$movies_hash/$movies_file")" "200"

# The facet rule, from the outside. Every file named below exists in a package folder this host read.
check "a file the package does not offer is not served" \
    "$(status_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/Arronix.Abstractions.dll")" "404"
check "an entry assembly is not offered to a client" \
    "$(status_of "$address/api/v1/client-contracts/movies/$movies_hash/Arronix.Plugin.Movies.dll")" "404"
check "one package's address does not serve another package's assembly" \
    "$(status_of "$address/api/v1/client-contracts/movies/$movies_hash/$video_file")" "404"
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

served_movies_hash=$(curl -fsS "$address/api/v1/client-contracts/movies/$movies_hash/$movies_file" \
    | shasum -a 256 | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')
check "the served movies bytes hash to the published content hash" "$served_movies_hash" "$movies_hash"

check "the manifest is served uncacheable" \
    "$(header_of "$address/api/v1/client-contracts" "cache-control:")" \
    "no-cache, no-store, must-revalidate"
check "a content address is served immutable" \
    "$(header_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file" "cache-control:")" \
    "public, max-age=31536000, immutable"
check "a content address states the identity it will bind as" \
    "$(header_of "$address/api/v1/client-contracts/arronix.format.video/$video_hash/$video_file" "arronix-assembly-identity:")" \
    "$video_identity"

# The publish posture is the project's, not this script's. Read from MSBuild rather than from the text of
# the file, so a property set in an import or overridden elsewhere is still what this asserts.
declared_trimming=$("$dotnet_command" msbuild "$repository_root/src/Arronix.Client/Arronix.Client.csproj" \
    -getProperty:PublishTrimmed -p:Configuration=Release -p:RuntimeIdentifier=browser-wasm 2>/dev/null | tr -d '[:space:]')
check "the client project declares its own publish posture" "$declared_trimming" "false"

# What a browser downloads is the published output, so the rule is read from the output rather than from
# the project file.
client_media_assemblies=$(find "$client_root/wwwroot/_framework" \
    \( -name 'Arronix.Media.*' -o -name 'Arronix.Format.*' -o -name 'Arronix.Plugin.*' \) 2>/dev/null | wc -l | tr -d ' ')
check "the published client carries no media or format assembly" "$client_media_assemblies" "0"

# What the update actually is, read from the two staged payloads rather than asserted in prose. A
# deterministic rebuild of unchanged source reproduces the first build exactly, so the working tree is back
# where it started and the second payload's difference is the build it came from.
movies_v1_contract=$(shasum -a 256 "$stage_root/movies-v1/Arronix.Media.Movies.dll" | cut -d' ' -f1)
movies_v2_contract=$(shasum -a 256 "$stage_root/movies-v2/Arronix.Media.Movies.dll" | cut -d' ' -f1)
rebuilt_contract=$(shasum -a 256 "$repository_root/$movies_contract" | cut -d' ' -f1)

# Every name in either payload, then every name's content. Comparing only v1's files would let the update
# add one and still report that nothing but the shared contract changed. Symbols move with a build and are
# not offered to a client, so they are excluded by name.
payload_names() {
    (cd "$1" && ls -1) | grep -v '\.pdb$' | sort
}

v1_names=$(payload_names "$stage_root/movies-v1" | paste -sd, -)
v2_names=$(payload_names "$stage_root/movies-v2" | paste -sd, -)

differing=$(cd "$stage_root" && {
    payload_names movies-v1
    payload_names movies-v2
} | sort -u | while read -r name; do
    cmp -s "movies-v1/$name" "movies-v2/$name" 2>/dev/null || echo "$name"
done | sort | paste -sd, -)

check "the update is a different build of the shared contract" \
    "$([[ "$movies_v1_contract" != "$movies_v2_contract" ]] && echo different || echo same)" "different"
check "a deterministic rebuild reproduces the first build" "$rebuilt_contract" "$movies_v1_contract"
check "the update offers the same files as the installation it replaces" "$v2_names" "$v1_names"
check "and the update changes nothing else a client is offered" "$differing" "Arronix.Media.Movies.dll"

# The browser reads this file, so what it reads has to be the file the drift test guards - byte for byte,
# through the real static-file path rather than from the copy on disk.
fixture_hash=$(shasum -a 256 "$fixture_source" | cut -d' ' -f1)
served_fixture_hash=$(curl -fsS "$address/fixtures/g07/movie.json" | shasum -a 256 | cut -d' ' -f1)
check "the projection fixture is served as the bytes the contract wrote" \
    "$served_fixture_hash" "$fixture_hash"
check "the projection fixture is served as JSON" \
    "$(header_of "$address/fixtures/g07/movie.json" "content-type:")" "application/json"

{
    echo "address=$address"
    echo "contractIdentity=$contract_identity"
    echo "installationHash=$installation_hash"
    echo "publishedPackages=$published_packages"
    echo "video=$video_file $video_hash $video_identity"
    echo "movies=$movies_file $movies_hash"
    echo "active=$active"
    echo "fixture=fixtures/g07/movie.json $fixture_hash"
} > "$evidence_root/server-half.txt"

echo
cat "$evidence_root/server-half.txt"
echo
echo "Evidence: $evidence_root"

if [[ $failures -ne 0 ]]; then
    echo "error: $failures server-half check(s) failed." >&2
    exit 1
fi

if [[ $browser -eq 1 || $lifecycle -eq 1 ]]; then
    if ! command -v node >/dev/null 2>&1; then
        echo "error: the browser halves need node. Re-run with --serve and drive them yourself." >&2
        exit 1
    fi
fi

if [[ $browser -eq 1 ]]; then
    echo
    echo "== browser half (G07.2) =="

    if ! node "$script_directory/g07-browser-proof.mjs" \
        --address "$address" --evidence "$evidence_root" --fixture "$fixture_source"; then
        echo "error: the browser half failed. See $evidence_root." >&2
        exit 1
    fi
fi

if [[ $lifecycle -eq 1 ]]; then
    echo
    echo "== lifecycle half (G07.3) =="

    # The lifecycle half owns the API across three restarts at this origin, so this server must be gone.
    if ! stop_server; then
        exit 1
    fi

    if ! node "$script_directory/g07-lifecycle-proof.mjs" \
        --address "$address" \
        --stage "$stage_root" \
        --packages "$package_root" \
        --evidence "$evidence_root" \
        --state "$proof_root/state" \
        --client "$client_root/wwwroot" \
        --api "$api_dll" \
        --content-root "$repository_root/src/Arronix.Api" \
        --dotnet "$dotnet_command" \
        --payload "fixtures/g07/movie.json"; then
        echo "error: the lifecycle half failed. See $evidence_root." >&2
        exit 1
    fi
fi

if [[ $browser -eq 1 || $lifecycle -eq 1 ]]; then
    exit 0
fi

if [[ $serve -eq 1 ]]; then
    echo
    echo "Browser half: run"
    echo "  node eng/proofs/g07-browser-proof.mjs --address $address --evidence $evidence_root \\"
    echo "       --fixture $fixture_source"
    echo "or open $address/contracts and read '#contract-proof', '#projection-status',"
    echo "'#projected-entity-type' and '#projection-proof' after pressing '#project-contract-payload'."
    echo "Press Ctrl+C to stop the server."
    wait "$server_pid"
fi
