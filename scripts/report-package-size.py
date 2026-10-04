#!/usr/bin/env python3
"""Measure the published app and refuse accidentally bundled shared runtimes."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys


FORBIDDEN_RUNTIME_FILES = {
    "coreclr.dll", "clrjit.dll", "hostfxr.dll", "hostpolicy.dll", "system.private.corelib.dll",
    "microsoft.ui.xaml.dll", "microsoft.windowsappruntime.dll", "onnxruntime.dll", "directml.dll",
}
REQUIRED_FILES = {
    "Notchling.Windows.exe", "Notchling.Windows.dll", "Notch.Core.dll",
    "Microsoft.WindowsAppRuntime.Bootstrap.dll", "Microsoft.WindowsAppRuntime.Bootstrap.Net.dll",
    "resources.pri",
}


def measure(publish: Path, require_app_only: bool, installer: Path | None = None) -> dict:
    files = sorted(path for path in publish.rglob("*") if path.is_file())
    if not files or not (publish / "Notchling.Windows.exe").is_file():
        raise ValueError("A published Notchling.Windows.exe is required.")
    runtime_config = json.loads((publish / "Notchling.Windows.runtimeconfig.json").read_text(encoding="utf-8"))
    options = runtime_config["runtimeOptions"]
    frameworks = options.get("frameworks", [options["framework"]] if "framework" in options else [])
    bundled = sorted(path.relative_to(publish).as_posix() for path in files if path.name.lower() in FORBIDDEN_RUNTIME_FILES)
    if require_app_only:
        if bundled or options.get("includedFrameworks"):
            raise ValueError("App-only packaging contains bundled runtimes: " + ", ".join(bundled or ["includedFrameworks"]))
        if not any(item.get("name") == "Microsoft.NETCore.App" and item.get("version", "").startswith("10.") for item in frameworks):
            raise ValueError("App-only packaging must declare its shared .NET 10 runtime.")
        missing = sorted(name for name in REQUIRED_FILES if not (publish / name).is_file())
        if missing:
            raise ValueError("Required application/bootstrap files are missing: " + ", ".join(missing))
        if (publish / "resources.pri").stat().st_size == 0:
            raise ValueError("The compiled WinUI resources.pri is empty.")
    sizes = sorted(((path.relative_to(publish).as_posix(), path.stat().st_size) for path in files), key=lambda item: item[1], reverse=True)
    return {
        "schemaVersion": 1,
        "deployment": "framework-dependent" if frameworks else "self-contained",
        "payloadBytes": sum(size for _, size in sizes),
        "fileCount": len(files),
        "sharedFrameworks": frameworks,
        "bundledRuntimeFiles": bundled,
        "largestFiles": [{"path": name, "bytes": size} for name, size in sizes[:12]],
        "installerBytes": installer.stat().st_size if installer else None,
        "measurementScope": "On-disk published files and optional installer; not process memory or UI performance.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--installer", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--require-app-only", action="store_true")
    args = parser.parse_args()
    try:
        report = measure(args.publish.resolve(), args.require_app_only, args.installer)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report, indent=2))
        if os.environ.get("GITHUB_ACTIONS") == "true":
            print(f"::notice title=Measured Notchling package::payloadBytes={report['payloadBytes']}; installerBytes={report['installerBytes']}; shared runtimes bundled={bool(report['bundledRuntimeFiles'])}")
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            rows = ["## Notchling package measurements", "", "| Measurement | Actual bytes | MiB |", "| --- | ---: | ---: |",
                    f"| Installed app files | {report['payloadBytes']} | {report['payloadBytes'] / 1048576:.2f} |"]
            if report["installerBytes"] is not None:
                rows.append(f"| Downloadable setup EXE | {report['installerBytes']} | {report['installerBytes'] / 1048576:.2f} |")
            rows.extend(["", "Shared .NET and Windows App Runtime are installed separately by Setup only when missing.",
                         "No bundled CoreCLR, Windows App Runtime, ONNX, or DirectML payload was found." if args.require_app_only else "", ""])
            with open(summary, "a", encoding="utf-8") as target:
                target.write("\n".join(rows))
        return 0
    except (OSError, ValueError, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
