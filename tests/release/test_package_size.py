"""Packaging regressions for stale runtimes and required shared-runtime bootstrapping."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("package_size", ROOT / "scripts/report-package-size.py")
package_size = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package_size)


class AppOnlyPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="Notchling.Package.Tests-")
        self.publish = Path(self.temporary.name)
        for name in package_size.REQUIRED_FILES:
            (self.publish / name).write_bytes(b"fixture")
        self.config = self.publish / "Notchling.Windows.runtimeconfig.json"
        self.config.write_text(json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))

    def tearDown(self):
        self.temporary.cleanup()

    def test_measures_actual_files_and_installer_separately(self):
        installer = self.publish / "setup.exe"
        installer.write_bytes(b"compressed installer fixture")
        report = package_size.measure(self.publish, True, installer)
        self.assertEqual(report["payloadBytes"], sum(path.stat().st_size for path in self.publish.iterdir()))
        self.assertEqual(report["installerBytes"], installer.stat().st_size)
        self.assertEqual(report["deployment"], "framework-dependent")

    def test_leftover_runtime_from_an_earlier_publish_is_refused(self):
        for name in ("CoreClr.dll", "Microsoft.UI.Xaml.dll", "onnxruntime.dll"):
            with self.subTest(name=name):
                path = self.publish / name
                path.write_bytes(b"stale runtime")
                with self.assertRaisesRegex(ValueError, "bundled runtimes"):
                    package_size.measure(self.publish, True)
                path.unlink()

    def test_removing_the_required_bootstrapper_is_refused(self):
        (self.publish / "Microsoft.WindowsAppRuntime.Bootstrap.dll").unlink()
        with self.assertRaisesRegex(ValueError, "bootstrap files are missing"):
            package_size.measure(self.publish, True)

    def test_missing_or_empty_compiled_window_resources_are_refused(self):
        resources = self.publish / "resources.pri"
        resources.unlink()
        with self.assertRaisesRegex(ValueError, "resources.pri"):
            package_size.measure(self.publish, True)
        resources.write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "resources.pri is empty"):
            package_size.measure(self.publish, True)

    def test_self_contained_runtime_config_is_refused(self):
        self.config.write_text(json.dumps({"runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.0"}]}}))
        with self.assertRaisesRegex(ValueError, "bundled runtimes"):
            package_size.measure(self.publish, True)


if __name__ == "__main__":
    unittest.main()
