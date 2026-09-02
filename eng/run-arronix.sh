#!/usr/bin/env bash

# Build, install and run a real Arronix installation.
#
# This file resolves the pinned .NET SDK and hands every argument to the composer. It contains no
# installation logic on purpose: what an installation is made of, where it lives, which port it takes and
# how it is stopped are owned by src/Arronix.Installation, so that the product can be installed and run
# without this script and so that this script can never drift from the thing it launches.

set -euo pipefail

script_directory=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/.." && pwd)
pinned_sdk=$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$repository_root/global.json")

# The SDK is pinned by global.json with rollForward disabled, so a machine whose PATH resolves 'dotnet' to
# another installation cannot build this repository at all. Rather than fail inside MSBuild with a message
# about a missing SDK, find one that has the pinned version and say so if there is none.
has_pinned_sdk() {
    local candidate="$1"
    command -v "$candidate" >/dev/null 2>&1 || [[ -x "$candidate" ]] || return 1
    "$candidate" --list-sdks 2>/dev/null | grep -qF "$pinned_sdk "
}

resolve_dotnet() {
    if [[ -n "${DOTNET_COMMAND:-}" ]]; then
        has_pinned_sdk "$DOTNET_COMMAND" || {
            echo "error: DOTNET_COMMAND='$DOTNET_COMMAND' does not provide SDK $pinned_sdk." >&2
            return 1
        }
        echo "$DOTNET_COMMAND"
        return 0
    fi

    local candidate
    for candidate in dotnet "$HOME/.dotnet/dotnet" /usr/local/share/dotnet/dotnet /usr/share/dotnet/dotnet /opt/dotnet/dotnet; do
        if has_pinned_sdk "$candidate"; then
            echo "$candidate"
            return 0
        fi
    done

    echo "error: .NET SDK $pinned_sdk was not found. Install it, or point DOTNET_COMMAND at it." >&2
    return 1
}

dotnet_command=$(resolve_dotnet)
export DOTNET_COMMAND="$dotnet_command"

cd "$repository_root"
exec "$dotnet_command" run \
    --project "$repository_root/src/Arronix.Installation/Arronix.Installation.csproj" \
    --configuration Release \
    -- "$@"
