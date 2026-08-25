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


def main(manifest_path: str, plugins_path: str) -> int:
    with open(manifest_path, encoding="utf-8") as handle:
        manifest = json.load(handle)

    with open(plugins_path, encoding="utf-8") as handle:
        plugins = json.load(handle)

    packages = {package["id"]: package for package in manifest["packages"]}

    if "arronix.format.video" not in packages:
        print(
            "error: the host published no client facet for 'arronix.format.video'.",
            file=sys.stderr,
        )
        return 1

    video = packages["arronix.format.video"]["assemblies"][0]
    quarantined = sorted(
        plugin["id"] for plugin in plugins if plugin["state"] != "Active"
    )

    values = {
        "video_hash": video["contentHash"],
        "video_file": video["fileName"],
        "video_identity": video["identity"],
        "published_packages": str(len(manifest["packages"])),
        "installation_hash": manifest["installationHash"],
        "contract_identity": manifest["contractIdentity"],
        "quarantined": ",".join(quarantined),
    }

    for name, value in values.items():
        print(f"{name}={shlex.quote(value)}")

    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("usage: g07-read-manifest.py <manifest.json> <plugins.json>", file=sys.stderr)
        raise SystemExit(2)

    raise SystemExit(main(sys.argv[1], sys.argv[2]))
