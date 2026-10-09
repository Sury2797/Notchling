#!/usr/bin/env python3
"""Bundle publisher licenses/notices and produce an SPDX 2.3 publish inventory.

Uses the exact restored graph, including SDK runtime downloads. It makes no
network requests and never substitutes a summary for publisher license text.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NOTICE = re.compile(r"^(?:licen[cs]e|copying|copyright|notice|third[_. -]?party[_. -]?notices?)(?:[_. -].*)?$", re.I)
BINARY_SUFFIXES = {".dll", ".exe", ".winmd"}


def digest(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def generate(publish: Path, assets_path: Path, strict: bool, installer_license: Path | None = None, installer_version: str | None = None) -> dict:
    if not publish.is_dir() or not (publish / "Notchling.Windows.exe").is_file():
        raise ValueError("The publish folder must contain Notchling.Windows.exe.")
    graph = json.loads(assets_path.read_text(encoding="utf-8"))
    roots = [Path(path) for path in graph["packageFolders"]]
    packages = {name: info["path"] for name, info in graph["libraries"].items() if info["type"] == "package"}
    for framework in graph["project"]["frameworks"].values():
        for dependency in framework.get("downloadDependencies", []):
            version = dependency["version"].strip("[]()").split(",")[0].strip()
            packages[dependency["name"] + "/" + version] = dependency["name"].lower() + "/" + version
    notices = publish / "ThirdPartyNotices"
    if notices.exists():
        shutil.rmtree(notices)
    notices.mkdir()
    for name in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(ROOT / name, publish / name)
    published_files = [path for path in publish.rglob("*") if path.is_file() and notices not in path.parents and path.name not in {"sbom.spdx.json", "publish-inventory.json"}]
    published_binary_names = {path.name.lower() for path in published_files if path.suffix.lower() in BINARY_SUFFIXES}
    records, unresolved = [], []
    for identifier, relative in sorted(packages.items()):
        name, version = identifier.rsplit("/", 1)
        directory = next((root / relative for root in roots if (root / relative).is_dir()), None)
        if directory is None:
            unresolved.append(f"Missing restored package: {identifier}")
            continue
        contents = [path for path in directory.rglob("*") if path.is_file()]
        binary_names = {path.name.lower() for path in contents if path.suffix.lower() in BINARY_SUFFIXES}
        matching = sorted(binary_names & published_binary_names)
        publisher_documents = [path for path in contents if NOTICE.match(path.name) and path.suffix.lower() not in BINARY_SUFFIXES]
        folder = notices / name / version
        folder.mkdir(parents=True)
        copied = []
        for source in publisher_documents:
            rel = source.relative_to(directory)
            destination = folder / rel
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
            copied.append(destination.relative_to(publish).as_posix())
        license_id, license_url = "NOASSERTION", None
        nuspecs = list(directory.glob("*.nuspec"))
        if nuspecs:
            metadata = ET.parse(nuspecs[0]).getroot()
            for element in metadata.iter():
                tag = element.tag.split("}")[-1]
                if tag == "license" and element.attrib.get("type") == "expression":
                    license_id = element.text or "NOASSERTION"
                if tag == "licenseUrl":
                    license_url = element.text
        # Build-only packages are inventoried too; they do not require shipped
        # license text merely because the compiler restored them.
        if matching and not publisher_documents:
            unresolved.append(f"Published binary names match {identifier}, but no publisher license/notice file was found ({license_url or 'no metadata license URL'}).")
        records.append({"id": name, "version": version, "licenseDeclared": license_id, "licenseUrl": license_url, "publisherDocuments": copied, "publishedBinaryNames": matching, "scope": "candidate-runtime" if matching else "build-or-reference"})
    if installer_license is not None:
        if not installer_version or not installer_license.is_file():
            raise ValueError("The installer engine requires its actual publisher license file and compiler version.")
        destination = notices / "InnoSetup" / installer_version / installer_license.name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(installer_license, destination)
        records.append({"id": "InnoSetup", "version": installer_version, "licenseDeclared": "NOASSERTION", "licenseUrl": "https://jrsoftware.org/isinfo.php", "publisherDocuments": [destination.relative_to(publish).as_posix()], "publishedBinaryNames": [], "scope": "installer-engine", "downloadLocation": "https://jrsoftware.org/isdl.php", "purl": f"pkg:generic/InnoSetup@{installer_version}"})
    inventory = {"schemaVersion": 1, "mappingMethod": "Published binary basename matched against exact restored package files; candidates can overlap and are not proof of redistribution rights.", "packages": records, "unresolved": unresolved}
    (publish / "publish-inventory.json").write_text(json.dumps(inventory, indent=2) + "\n", encoding="utf-8")
    files = []
    relations = []
    spdx_packages = []
    for index, record in enumerate(records):
        spdx_id = f"SPDXRef-Package-{index}"
        record["spdxId"] = spdx_id
        spdx_packages.append({"SPDXID": spdx_id, "name": record["id"], "versionInfo": record["version"], "downloadLocation": record.get("downloadLocation", f"https://www.nuget.org/api/v2/package/{record['id']}/{record['version']}"), "filesAnalyzed": False, "licenseConcluded": "NOASSERTION", "licenseDeclared": record["licenseDeclared"], "copyrightText": "NOASSERTION", "externalRefs": [{"referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": record.get("purl", f"pkg:nuget/{record['id']}@{record['version']}")}]})
        relations.append({"spdxElementId": "SPDXRef-Notchling", "relationshipType": "DEPENDS_ON", "relatedSpdxElement": spdx_id} if record["scope"] != "build-or-reference" else {"spdxElementId": spdx_id, "relationshipType": "BUILD_DEPENDENCY_OF", "relatedSpdxElement": "SPDXRef-Notchling"})
    for index, path in enumerate(sorted(publish.rglob("*"))):
        if not path.is_file() or path.name == "sbom.spdx.json":
            continue
        spdx_id = f"SPDXRef-File-{index}"
        files.append({"SPDXID": spdx_id, "fileName": "./" + path.relative_to(publish).as_posix(), "checksums": [{"algorithm": "SHA256", "checksumValue": digest(path)}], "licenseConcluded": "NOASSERTION", "licenseInfoInFiles": ["NOASSERTION"], "copyrightText": "NOASSERTION"})
        relations.append({"spdxElementId": "SPDXRef-Notchling", "relationshipType": "CONTAINS", "relatedSpdxElement": spdx_id})
    sbom = {"spdxVersion": "SPDX-2.3", "dataLicense": "CC0-1.0", "SPDXID": "SPDXRef-DOCUMENT", "name": "Notchling Windows publish inventory", "documentNamespace": "https://github.com/Sury2797/Notchling/sbom/" + str(uuid.uuid4()), "creationInfo": {"creators": ["Tool: Notchling bundle-notices.py"], "created": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")}, "packages": [{"SPDXID": "SPDXRef-Notchling", "name": "Notchling", "downloadLocation": "https://github.com/Sury2797/Notchling/releases", "filesAnalyzed": False, "licenseDeclared": "NOASSERTION", "licenseConcluded": "NOASSERTION", "copyrightText": "Copyright (c) 2026 SuryaK999"}] + spdx_packages, "files": files, "relationships": [{"spdxElementId": "SPDXRef-DOCUMENT", "relationshipType": "DESCRIBES", "relatedSpdxElement": "SPDXRef-Notchling"}] + relations}
    (publish / "sbom.spdx.json").write_text(json.dumps(sbom, indent=2) + "\n", encoding="utf-8")
    print(f"Bundled publisher documents for {len(records)} dependencies and inventoried {len(files)} shipped files.")
    for problem in unresolved:
        print(problem, file=sys.stderr)
    if strict and unresolved:
        raise ValueError("Release notice review is incomplete; resolve every entry in publish-inventory.json before signing.")
    return inventory


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--strict", action="store_true", help="Refuse a release when a restored dependency or candidate-runtime notice is missing")
    parser.add_argument("--installer-license", type=Path, help="Actual installed Inno Setup publisher license file")
    parser.add_argument("--installer-version", help="Actual installed Inno Setup compiler version")
    args = parser.parse_args()
    try:
        generate(args.publish.resolve(), args.assets.resolve(), args.strict, args.installer_license, args.installer_version)
        return 0
    except (ValueError, OSError, KeyError, ET.ParseError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
