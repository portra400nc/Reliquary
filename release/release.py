#!/usr/bin/env python3
"""Publish a Reliquary release on GitHub.

Zips Reliquary's built files into "Reliquary X.Y.Z/", tags the version from
AssemblyInfo.cs, and creates the GitHub release with the zip and notes from git-cliff.

    python3 release/release.py                 # show the notes, ask, then publish
    python3 release/release.py --zip-only      # only write release/dist/Reliquary-X.Y.Z.zip
    python3 release/release.py --from <folder> # take the files from anywhere under <folder>

Without --from, the files come from the build output in this repository.
Needs git-cliff and gh.
"""
import argparse
import re
import subprocess
import sys
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
CLIFF = HERE / "cliff.toml"
BUILT = [
    REPO / "Reliquary/bin/Release/Reliquary.dll",
    REPO / "Reliquary/bin/Release/Reliquary.pdb",
]


def run(*args, capture=False):
    result = subprocess.run(args, cwd=REPO, check=True, text=True, capture_output=capture)
    return result.stdout.strip() if capture else None


def fail(message):
    sys.exit(f"release: {message}")


def main():
    parser = argparse.ArgumentParser(description="Publish a Reliquary release on GitHub.")
    parser.add_argument("--zip-only", action="store_true", help="only write the zip")
    parser.add_argument("--from", dest="source", type=Path, help="a folder to find the files in")
    args = parser.parse_args()

    info = (REPO / "Reliquary/Properties/AssemblyInfo.cs").read_text()
    version = re.search(r'MelonInfo\(.*"Reliquary", "([^"]+)"', info).group(1)
    tag = f"v{version}"

    files = BUILT
    if args.source:
        files = [next(args.source.rglob(f.name), f) for f in BUILT]
    missing = [str(f) for f in files if not f.is_file()]
    if missing:
        fail("missing " + ", ".join(missing))
    # The DLL's file version is stored as UTF-16, as in "0.1.0.0".
    if f"{version}.0".encode("utf-16-le") not in files[0].read_bytes():
        fail(f"{files[0]} is not version {version}. Build it first.")
    # Debug info must carry the mapped source paths, never this machine's.
    home = str(Path.home()).encode()
    leaks = [str(f) for f in files if home in f.read_bytes() or home.decode().encode("utf-16-le") in f.read_bytes()]
    if leaks:
        fail(f"{', '.join(leaks)} contain {home.decode()}")

    out = HERE / "dist" / f"Reliquary-{version}.zip"
    out.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for f in files:
            z.write(f, f"Reliquary {version}/{f.name}")
    print(f"Wrote {out}")
    if args.zip_only:
        return

    if run("git", "status", "--porcelain", "--untracked-files=no", capture=True):
        fail("commit your changes first")
    if run("git", "tag", "--list", tag, capture=True):
        fail(f"{tag} already exists. Raise the version in AssemblyInfo.cs.")
    run("git", "fetch", "-q", "origin")
    if subprocess.run(["git", "merge-base", "--is-ancestor", "HEAD", "origin/main"], cwd=REPO).returncode:
        fail("push main first")

    notes = run("git", "cliff", "--config", str(CLIFF), "--unreleased", "--tag", tag, "--strip", "all", capture=True)
    print(f"\n{notes}\n")
    if input(f"Publish Reliquary {tag}? [y/N] ").strip().lower() != "y":
        return

    run("git", "tag", tag)
    run("git", "push", "origin", tag)
    notes_file = out.with_suffix(".md")
    notes_file.write_text(notes + "\n")
    run("gh", "release", "create", tag, str(out), "--title", f"Reliquary {tag}", "--notes-file", str(notes_file))


if __name__ == "__main__":
    main()
