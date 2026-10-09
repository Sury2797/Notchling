"""Packaging regressions for stale runtimes and required shared-runtime bootstrapping."""
import importlib.util
import json
from pathlib import Path
import struct
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
        self.write_architecture("x64")
        self.config = self.publish / "Notchling.Windows.runtimeconfig.json"
        self.config.write_text(json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))

    def tearDown(self):
        self.temporary.cleanup()

    def write_architecture(self, architecture):
        image = bytearray(152)
        image[:2] = b"MZ"
        struct.pack_into("<I", image, 0x3C, 128)
        image[128:132] = b"PE\0\0"
        machine = {"x86": 0x014C, "x64": 0x8664, "arm64": 0xAA64}[architecture]
        struct.pack_into("<H", image, 132, machine)
        for name in ("Notchling.Windows.exe", "Microsoft.WindowsAppRuntime.Bootstrap.dll"):
            (self.publish / name).write_bytes(image)
        (self.publish / "Notchling.Windows.deps.json").write_text(json.dumps({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/win-" + architecture}}))

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

    def test_each_native_architecture_requires_matching_bootstrap_and_runtime_target(self):
        for architecture in ("x64", "x86", "arm64"):
            with self.subTest(architecture=architecture):
                self.write_architecture(architecture)
                self.assertEqual(package_size.measure(self.publish, True, architecture=architecture)["architecture"], architecture)
                other = "x64" if architecture != "x64" else "arm64"
                with self.assertRaisesRegex(ValueError, "does not match requested"):
                    package_size.measure(self.publish, True, architecture=other)

    def test_wrong_native_bootstrap_and_restored_runtime_are_refused(self):
        original = (self.publish / "Microsoft.WindowsAppRuntime.Bootstrap.dll").read_bytes()
        changed = bytearray(original)
        struct.pack_into("<H", changed, 132, 0xAA64)
        (self.publish / "Microsoft.WindowsAppRuntime.Bootstrap.dll").write_bytes(changed)
        with self.assertRaisesRegex(ValueError, "bootstrap architecture"):
            package_size.measure(self.publish, True)
        (self.publish / "Microsoft.WindowsAppRuntime.Bootstrap.dll").write_bytes(original)
        (self.publish / "Notchling.Windows.deps.json").write_text(json.dumps({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/win-x86"}}))
        with self.assertRaisesRegex(ValueError, "runtime target"):
            package_size.measure(self.publish, True)

    def test_non_executable_or_out_of_bounds_pe_header_is_refused(self):
        executable = self.publish / "Notchling.Windows.exe"
        executable.write_bytes(b"not an executable")
        with self.assertRaisesRegex(ValueError, "native Windows PE"):
            package_size.measure(self.publish, True)
        self.write_architecture("x64")
        changed = bytearray(executable.read_bytes())
        struct.pack_into("<I", changed, 0x3C, 0xFFFFFFFF)
        executable.write_bytes(changed)
        with self.assertRaisesRegex(ValueError, "PE header offset"):
            package_size.measure(self.publish, True)


if __name__ == "__main__":
    unittest.main()
