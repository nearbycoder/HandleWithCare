#!/usr/bin/env python3
"""Zips the built players for a release into Builds/Release/ (nothing is uploaded).

    python3 Tools/package.py            # every player that has been built
    python3 Tools/package.py linux mac  # just these

Unix permissions (the executables) and symlinks are kept, so the macOS .app still opens after unzipping.
The version comes from Assets/Editor/BuildScript.cs.
"""
import os
import re
import stat
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILDS = os.path.join(ROOT, "Builds")
OUT = os.path.join(BUILDS, "Release")
SKIP = "_BackUpThisFolder_ButDontShipItWithYourGame"     # Unity's debug symbols, never shipped


def version():
    src = open(os.path.join(ROOT, "Assets", "Editor", "BuildScript.cs")).read()
    return re.search(r'Version = "([^"]+)"', src).group(1)


def add_tree(zf, src, arc_root):
    """Adds src (a file or folder) under arc_root, keeping modes and symlinks."""
    count = 0
    for base, dirs, files in os.walk(src, followlinks=False):
        dirs[:] = sorted(d for d in dirs if SKIP not in d)
        for name in sorted(dirs) + sorted(files):
            path = os.path.join(base, name)
            arc = os.path.join(arc_root, os.path.relpath(path, src))
            st = os.lstat(path)
            if os.path.isdir(path) and not os.path.islink(path):
                info = zipfile.ZipInfo(arc + "/")
                info.external_attr = (stat.S_IFDIR | 0o755) << 16 | 0x10
                zf.writestr(info, b"")
                continue
            info = zipfile.ZipInfo.from_file(path, arc)
            if os.path.islink(path):
                info.external_attr = (stat.S_IFLNK | 0o777) << 16
                zf.writestr(info, os.readlink(path))
            else:
                info.external_attr = (stat.S_IFREG | stat.S_IMODE(st.st_mode)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(path, "rb") as f:
                    zf.writestr(info, f.read(), compresslevel=9)
            count += 1
    return count


def package(name, src, arc_root, zip_name):
    if not os.path.exists(src):
        print(f"[package] {name}: no build at {os.path.relpath(src, ROOT)}, skipped")
        return
    os.makedirs(OUT, exist_ok=True)
    dst = os.path.join(OUT, zip_name)
    with zipfile.ZipFile(dst, "w") as zf:
        n = add_tree(zf, src, arc_root)
    print(f"[package] {name}: {n} files -> {os.path.relpath(dst, ROOT)} ({os.path.getsize(dst) / 1e6:.0f} MB)")


def main():
    v = version()
    want = sys.argv[1:] or ["linux", "mac", "windows"]
    if "linux" in want:
        d = f"HandleWithCare-v{v}-linux-x86_64"
        package("linux", os.path.join(BUILDS, "Linux"), d, d + ".zip")
    if "mac" in want:
        package("mac", os.path.join(BUILDS, "Mac", "HandleWithCare.app"), "HandleWithCare.app", f"HandleWithCare-v{v}-macos-universal.zip")
    if "windows" in want:
        d = f"HandleWithCare-v{v}-windows-x86_64"
        package("windows", os.path.join(BUILDS, "Windows"), d, d + ".zip")


main()
