#!/usr/bin/env python3
"""Install the repository-pinned .NET SDK from verified Microsoft release data.

This is an optional, user-local bootstrap: no root privileges, trust bypasses,
package-manager changes, or credential access. Python 3.11.8+ is required.
"""

import argparse
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile
import urllib.parse
import urllib.request
import zipfile


def official_url(url: str) -> str:
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https" or parsed.hostname not in {
        "builds.dotnet.microsoft.com", "download.visualstudio.microsoft.com",
        "dotnetcli.azureedge.net", "ci.dot.net",
    }:
        raise ValueError("SDK metadata contained an unexpected download origin")
    return url


def main() -> None:
    repository = Path(__file__).resolve().parents[1]
    pinned = json.loads((repository / "global.json").read_text(encoding="utf-8"))["sdk"]["version"]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--install-dir", type=Path, default=repository / ".tools" / "dotnet")
    parser.add_argument("--cache-dir", type=Path, default=repository / ".tools" / "downloads")
    parser.add_argument("--version", default=pinned, help="Stable SDK version (default: global.json pin)")
    args = parser.parse_args()
    if not args.version.startswith("10.0.") or "-" in args.version:
        parser.error("This repository requires a stable .NET 10 SDK")

    machine = platform.machine().lower()
    arch = {"x86_64": "x64", "amd64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(machine)
    os_name = {"linux": "linux", "win32": "win", "darwin": "osx"}.get(sys.platform)
    if not arch or not os_name:
        raise RuntimeError(f"Unsupported SDK host: {sys.platform}/{machine}")
    rid = f"{os_name}-{arch}"
    executable = args.install_dir / ("dotnet.exe" if sys.platform == "win32" else "dotnet")
    if executable.exists():
        installed = subprocess.check_output([str(executable), "--list-sdks"], text=True)
        if any(line.startswith(args.version + " ") for line in installed.splitlines()):
            print(f".NET SDK {args.version} already installed at {args.install_dir}")
            return

    metadata_url = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"
    with urllib.request.urlopen(metadata_url, timeout=60) as response:
        releases = json.load(response)
    sdk = next((sdk for release in releases["releases"]
                for sdk in release.get("sdks", [release.get("sdk", {})])
                if sdk.get("version") == args.version), None)
    if sdk is None:
        raise RuntimeError(f"SDK {args.version} not found in official .NET 10 metadata")
    artifact = next((file for file in sdk["files"] if file["rid"] == rid
                     and file["name"].endswith(".zip" if os_name == "win" else ".tar.gz")), None)
    if artifact is None:
        raise RuntimeError(f"SDK {args.version} has no archive for {rid}")
    url = official_url(artifact["url"])
    expected_hash = artifact["hash"].lower()
    if len(expected_hash) != 128:
        raise ValueError("SDK artifact metadata did not provide a SHA-512 hash")
    args.cache_dir.mkdir(parents=True, exist_ok=True)
    archive = args.cache_dir / Path(urllib.parse.urlparse(url).path).name

    def checksum(path: Path) -> str:
        with path.open("rb") as source:
            return hashlib.file_digest(source, "sha512").hexdigest()

    if not archive.exists() or checksum(archive) != expected_hash:
        temporary = archive.with_suffix(archive.suffix + ".partial")
        print(f"Downloading official .NET SDK {args.version} ({rid})…", flush=True)
        with urllib.request.urlopen(url, timeout=120) as response, temporary.open("wb") as destination:
            shutil.copyfileobj(response, destination, length=1024 * 1024)
        if checksum(temporary) != expected_hash:
            temporary.unlink(missing_ok=True)
            raise RuntimeError("SHA-512 verification failed; the SDK was not installed")
        temporary.replace(archive)
    print("TLS download and SHA-512 artifact verification passed.", flush=True)

    args.install_dir.mkdir(parents=True, exist_ok=True)
    if archive.name.endswith(".zip"):
        with zipfile.ZipFile(archive) as source:
            for entry in source.infolist():
                target = (args.install_dir / entry.filename).resolve()
                if not target.is_relative_to(args.install_dir.resolve()):
                    raise RuntimeError("SDK archive contained an unsafe path")
            source.extractall(args.install_dir)
    else:
        with tarfile.open(archive, "r:gz") as source:
            source.extractall(args.install_dir, filter="data")
    subprocess.run([str(executable), "--version"], check=True)
    print(f"SDK installed at {args.install_dir.resolve()}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"SDK installation failed: {error}", file=sys.stderr)
        sys.exit(1)
