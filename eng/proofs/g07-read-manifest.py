#!/usr/bin/env python3

"""Reads the served client contract manifest and extension inventory into shell variables.

Kept out of the shell script rather than inlined, because a here-document holding JSON parsing is a
here-document nobody edits confidently. It prints assignments and nothing else, so the caller can eval it;
anything it cannot answer for is a non-zero exit with a sentence on stderr rather than an empty variable
the caller would go on to use.
"""

import json
import shlex
import sys


def one_assembly(package: dict, package_id: str) -> dict:
    """Returns the single client-safe assembly a package offers, or fails saying what it found."""
    assemblies = package["assemblies"]

    if len(assemblies) != 1:
        names = ", ".join(assembly["fileName"] for assembly in assemblies) or "nothing"
        print(
            f"error: '{package_id}' offers {len(assemblies)} client assemblies ({names}); one was expected.",
            file=sys.stderr,
        )
        raise SystemExit(1)

    return assemblies[0]


def main(manifest_path: str, plugins_path: str) -> int:
    with open(manifest_path, encoding="utf-8") as handle:
        manifest = json.load(handle)

    with open(plugins_path, encoding="utf-8") as handle:
        plugins = json.load(handle)

    order = [package["id"] for package in manifest["packages"]]
    packages = {package["id"]: package for package in manifest["packages"]}

    for required in ("arronix.format.video", "movies"):
        if required not in packages:
            print(f"error: the host published no client facet for '{required}'.", file=sys.stderr)
            return 1

    video = one_assembly(packages["arronix.format.video"], "arronix.format.video")
    movies = one_assembly(packages["movies"], "movies")

    values = {
        "package_order": ",".join(order),
        "video_hash": video["contentHash"],
        "video_file": video["fileName"],
        "video_identity": video["identity"],
        "movies_hash": movies["contentHash"],
        "movies_file": movies["fileName"],
        "published_packages": str(len(manifest["packages"])),
        "refused_packages": str(len(manifest["refused"])),
        "installation_hash": manifest["installationHash"],
        "contract_identity": manifest["contractIdentity"],
        "active": ",".join(sorted(plugin["id"] for plugin in plugins if plugin["state"] == "Active")),
        "not_active": ",".join(sorted(plugin["id"] for plugin in plugins if plugin["state"] != "Active")),
    }

    for name, value in values.items():
        print(f"{name}={shlex.quote(value)}")

    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("usage: g07-read-manifest.py <manifest.json> <plugins.json>", file=sys.stderr)
        raise SystemExit(2)

    raise SystemExit(main(sys.argv[1], sys.argv[2]))
