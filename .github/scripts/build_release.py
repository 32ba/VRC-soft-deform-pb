"""Build the VPM ZIP, UnityPackage, and manifest from tracked package files."""

import argparse
import gzip
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
from urllib.parse import quote
import zipfile


VERSION = re.compile(
    r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
    r"(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
)
GUID = re.compile(r"^guid: ([0-9a-fA-F]{32})\r?$", re.MULTILINE)
ROOT_FILES = {
    "Editor.meta", "Runtime.meta", "package.json", "package.json.meta",
    "README.md", "README.md.meta", "LICENSE", "LICENSE.meta",
}


def release_info(manifest, repository):
    match = VERSION.fullmatch(manifest.get("version", ""))
    if not match:
        raise ValueError("package.json must contain a valid SemVer version")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("repository must have the form owner/name")
    name = manifest.get("name", "")
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)+", name):
        raise ValueError("package.json must contain a valid package name")
    display_name = manifest.get("displayName", "")
    if not display_name or any(ord(char) < 32 for char in display_name):
        raise ValueError("package.json must contain a displayName without control characters")
    author = manifest.get("author", {})
    if not author.get("name") or not author.get("email"):
        raise ValueError("package.json author must include name and email")
    version = manifest["version"]
    base = f"{name}-{version}"
    return {
        "package_name": name,
        "display_name": display_name,
        "version": version,
        "prerelease": bool(match.group(4)),
        "zip_file": f"{base}.zip",
        "unitypackage_file": f"{base}.unitypackage",
        "url": f"https://github.com/{repository}/releases/download/"
               f"{quote(version, safe='')}/{quote(base + '.zip', safe='')}",
    }


def package_files(root):
    tracked = subprocess.check_output(
        ["git", "ls-files", "-z"], cwd=root
    ).decode("utf-8").split("\0")
    selected = sorted(path for path in tracked if path and (
        path in ROOT_FILES or path.startswith(("Editor/", "Runtime/"))
        or (path.startswith("Docs~/") and not path.startswith((
            "Docs~/Validation/", "Docs~/Experiments/",
        )))
    ))
    if not ROOT_FILES.issubset(selected):
        raise ValueError("Required package files or their Unity metadata are missing")
    for relative in selected:
        path = root / relative
        if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
            raise ValueError(f"Package file is a symlink or leaves the repository: {relative}")
        if not path.is_file():
            raise ValueError(f"Tracked package file is missing: {relative}")
        if relative.startswith(("Editor/", "Runtime/")) and not relative.endswith(".meta"):
            if relative + ".meta" not in selected:
                raise ValueError(f"Unity metadata is missing: {relative}.meta")
    return selected


def unity_assets(root, files, manifest_bytes, package_name):
    assets = []
    guids = set()
    for relative in files:
        if not relative.endswith(".meta"):
            continue
        metadata = (root / relative).read_bytes()
        match = GUID.search(metadata.decode("utf-8"))
        if not match:
            raise ValueError(f"Invalid Unity GUID in {relative}")
        guid = match.group(1).lower()
        if guid in guids:
            raise ValueError(f"Duplicate Unity GUID in {relative}")
        guids.add(guid)
        asset_path = relative[:-5]
        source = root / asset_path
        if not source.exists():
            raise ValueError(f"Unity metadata has no matching asset: {relative}")
        if source.is_file() and asset_path not in files:
            raise ValueError(f"Unity asset is not tracked: {asset_path}")
        data = None if source.is_dir() else (
            manifest_bytes if asset_path == "package.json" else source.read_bytes()
        )
        assets.append((guid, f"Packages/{package_name}/{asset_path}", metadata, data))
    return assets


def build(root, output, repository):
    root, output = Path(root), Path(output)
    if output.resolve() == root.resolve():
        raise ValueError("Release output must use a separate directory")
    manifest = json.loads((root / "package.json").read_text(encoding="utf-8"))
    info = release_info(manifest, repository)
    manifest["url"] = info["url"]
    manifest.pop("zipSHA256", None)
    manifest_bytes = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    files = package_files(root)
    assets = unity_assets(root, files, manifest_bytes, info["package_name"])
    output.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(output / info["zip_file"], "w") as archive:
        for relative in files:
            entry = zipfile.ZipInfo(relative)
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            data = manifest_bytes if relative == "package.json" else (root / relative).read_bytes()
            archive.writestr(entry, data)

    with (output / info["unitypackage_file"]).open("wb") as stream:
        with gzip.GzipFile(filename="", fileobj=stream, mode="wb", mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w") as archive:
                for guid, pathname, metadata, data in assets:
                    entries = [("pathname", pathname.encode("utf-8")), ("asset.meta", metadata)]
                    if data is not None:
                        entries.append(("asset", data))
                    for filename, contents in entries:
                        entry = tarfile.TarInfo(f"{guid}/{filename}")
                        entry.size, entry.mode = len(contents), 0o644
                        archive.addfile(entry, io.BytesIO(contents))

    (output / "package.json").write_bytes(manifest_bytes)
    checksums = []
    for filename in (info["zip_file"], info["unitypackage_file"], "package.json"):
        digest = hashlib.sha256((output / filename).read_bytes()).hexdigest()
        checksums.append(f"{digest}  {filename}\n")
    (output / "SHA256SUMS").write_text("".join(checksums), encoding="utf-8", newline="\n")
    return info


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("Artifacts~"))
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY"))
    parser.add_argument("--metadata-only", action="store_true")
    arguments = parser.parse_args()
    if not arguments.repository:
        parser.error("--repository or GITHUB_REPOSITORY is required")
    info = release_info(
        json.loads((arguments.root / "package.json").read_text(encoding="utf-8")), arguments.repository
    ) if arguments.metadata_only else build(arguments.root, arguments.output, arguments.repository)
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a", encoding="utf-8", newline="\n") as stream:
            for name, value in info.items():
                stream.write(f"{name}={str(value).lower() if isinstance(value, bool) else value}\n")
    print(json.dumps(info, indent=2))


if __name__ == "__main__":
    main()
