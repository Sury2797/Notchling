"""Fixture tests for actual publisher text and strict release failure behavior."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("bundle_notices", ROOT / "scripts/bundle-notices.py")
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class NoticeBundleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="Notchling.Release.Tests-")
        self.root = Path(self.temporary.name)
        self.publish = self.root / "publish"
        self.publish.mkdir()
        (self.publish / "Notchling.Windows.exe").write_bytes(b"fixture, not an executable")
        self.package = self.root / "packages/vendor/1.0.0"
        self.package.mkdir(parents=True)
        self.assets = self.root / "assets.json"
        self.assets.write_text(json.dumps({"packageFolders": {str(self.root / "packages"): {}}, "libraries": {"Vendor/1.0.0": {"type": "package", "path": "vendor/1.0.0"}}, "project": {"frameworks": {}}}))

    def tearDown(self):
        self.temporary.cleanup()

    def test_exact_publisher_text_and_published_file_hashes_are_retained(self):
        license_text = "Publisher terms\n© Vendor. Retain this exact text.\n".encode("utf-8")
        (self.package / "LICENSE.txt").write_bytes(license_text)
        (self.package / "Vendor.dll").write_bytes(b"library fixture")
        (self.publish / "Vendor.dll").write_bytes(b"library fixture")
        inventory = bundle.generate(self.publish, self.assets, True)
        self.assertEqual(inventory["unresolved"], [])
        document = self.publish / inventory["packages"][0]["publisherDocuments"][0]
        self.assertEqual(document.read_bytes(), license_text)
        sbom = json.loads((self.publish / "sbom.spdx.json").read_text())
        self.assertEqual(sbom["spdxVersion"], "SPDX-2.3")
        file = next(item for item in sbom["files"] if item["fileName"] == "./Vendor.dll")
        self.assertEqual(file["checksums"][0]["checksumValue"], hashlib.sha256(b"library fixture").hexdigest())

    def test_strict_release_refuses_a_shipped_dependency_without_terms(self):
        (self.package / "Vendor.dll").write_bytes(b"library fixture")
        (self.publish / "Vendor.dll").write_bytes(b"library fixture")
        with self.assertRaisesRegex(ValueError, "notice review is incomplete"):
            bundle.generate(self.publish, self.assets, True)
        inventory = json.loads((self.publish / "publish-inventory.json").read_text())
        self.assertTrue(inventory["unresolved"])

    def test_build_only_package_is_not_mislabeled_as_shipped_runtime(self):
        (self.package / "Compiler.dll").write_bytes(b"build-only")
        inventory = bundle.generate(self.publish, self.assets, True)
        self.assertEqual(inventory["packages"][0]["scope"], "build-or-reference")

    def test_missing_restore_dependency_blocks_strict_release(self):
        self.package.rmdir()
        with self.assertRaisesRegex(ValueError, "notice review is incomplete"):
            bundle.generate(self.publish, self.assets, True)

    def test_installer_engine_license_and_provenance_are_in_the_release_inventory(self):
        license_file = self.root / "InnoSetup-license.txt"
        license_file.write_text("Publisher installer-engine terms\n")
        inventory = bundle.generate(self.publish, self.assets, True, license_file, "6.4.3")
        engine = next(package for package in inventory["packages"] if package["id"] == "InnoSetup")
        self.assertEqual(engine["scope"], "installer-engine")
        self.assertEqual((self.publish / engine["publisherDocuments"][0]).read_text(), license_file.read_text())
        sbom = json.loads((self.publish / "sbom.spdx.json").read_text())
        package = next(item for item in sbom["packages"] if item["name"] == "InnoSetup")
        self.assertEqual(package["downloadLocation"], "https://jrsoftware.org/isdl.php")


if __name__ == "__main__":
    unittest.main()
