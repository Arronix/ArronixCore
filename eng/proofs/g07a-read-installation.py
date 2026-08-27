#!/usr/bin/env python3

"""Reads what a running host says about the two external packages, into shell variables.

Kept out of the shell script for the same reason as g07-read-manifest.py: a here-document holding JSON
parsing is a here-document nobody edits confidently. It prints assignments and nothing else, so the caller
can eval it, and anything it cannot answer for is a non-zero exit with a sentence on stderr rather than an
empty variable the caller would go on to use.
"""

import json
import shlex
import sys

MEDIA = "northmark.shorts"
PROVIDER = "northmark.shorts.catalog"


def load(path: str):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def one_assembly(package: dict, package_id: str) -> dict:
    assemblies = package["assemblies"]

    if len(assemblies) != 1:
        names = ", ".join(assembly["fileName"] for assembly in assemblies) or "nothing"
        print(
            f"error: '{package_id}' offers {len(assemblies)} client assemblies ({names}); one was expected.",
            file=sys.stderr,
        )
        raise SystemExit(1)

    return assemblies[0]


def main(manifest_path: str, plugins_path: str, kinds_path: str, providers_path: str) -> int:
    manifest = load(manifest_path)
    plugins = load(plugins_path)
    kinds = load(kinds_path)
    providers = load(providers_path)

    packages = {package["id"]: package for package in manifest["packages"]}

    if MEDIA not in packages:
        print(f"error: the host published no client facet for '{MEDIA}'.", file=sys.stderr)
        return 1

    media = one_assembly(packages[MEDIA], MEDIA)
    declarations = media["declarations"]

    if len(declarations) != 1:
        print(
            f"error: '{MEDIA}' declares {len(declarations)} client contract entry points; one was expected.",
            file=sys.stderr,
        )
        return 1

    declaration = declarations[0]
    shorts = [kind for kind in kinds if kind["kind"] == "shorts"]

    if len(shorts) != 1:
        print(f"error: the host published {len(shorts)} media kinds called 'shorts'.", file=sys.stderr)
        return 1

    levels = shorts[0]["shape"]["levels"]
    fields = sorted({field["fieldId"] for level in levels for field in level["fields"]})

    values = {
        "media_file": media["fileName"],
        "media_hash": media["contentHash"],
        "media_identity": media["identity"],
        "media_closure": ",".join(packages[MEDIA]["closure"]),
        "entry_point": declaration["entryPointType"],
        "entity_type": declaration["entityTypeName"],
        "metadata_hash": declaration["generatedMetadataHash"],
        "projection_hash": declaration["projectionSchemaHash"],
        "refused_packages": str(len(manifest["refused"])),
        "contract_identity": manifest["contractIdentity"],
        "active": ",".join(sorted(plugin["id"] for plugin in plugins if plugin["state"] == "Active")),
        "not_active": ",".join(sorted(plugin["id"] for plugin in plugins if plugin["state"] != "Active")),
        "kind_names": ",".join(sorted(kind["kind"] for kind in kinds)),
        "owned_field": "premiere" if "premiere" in fields else "",
        "catalogers": ",".join(
            sorted(
                provider["provider"]
                for provider in providers
                if provider["family"] == "cataloger" and provider["provider"].startswith(PROVIDER + ":")
            )
        ),
    }

    for name, value in values.items():
        print(f"{name}={shlex.quote(value)}")

    return 0


def refusal(plugins_path: str, package_id: str) -> int:
    """Prints one quarantined package's state and defects, for the mutation runs."""
    plugins = load(plugins_path)
    found = [plugin for plugin in plugins if plugin["id"] == package_id]

    if len(found) != 1:
        print(f"error: the host reported {len(found)} packages called '{package_id}'.", file=sys.stderr)
        return 1

    values = {
        "state": found[0]["state"],
        "error_code": str(found[0].get("errorCode") or ""),
        "defects": " | ".join(found[0]["defects"]),
        "message": found[0].get("message") or "",
    }

    for name, value in values.items():
        print(f"{name}={shlex.quote(value)}")

    return 0


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "--refusal":
        raise SystemExit(refusal(sys.argv[2], sys.argv[3]))

    if len(sys.argv) != 5:
        print(
            "usage: g07a-read-installation.py <manifest.json> <plugins.json> <kinds.json> <providers.json>\n"
            "       g07a-read-installation.py --refusal <plugins.json> <package-id>",
            file=sys.stderr,
        )
        raise SystemExit(2)

    raise SystemExit(main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]))
