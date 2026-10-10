"""Installer-readable complete legal text and safe, deterministic RTF output."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("installer_documents", ROOT / "scripts/render-installer-documents.py")
renderer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = renderer
spec.loader.exec_module(renderer)


def visible_rtf(document):
    """Independent minimal RTF reader: formatting groups do not become text."""
    output, stack = [], []
    skip, index, units = False, 0, []
    while index < len(document):
        char = document[index]
        if char == "{":
            stack.append(skip)
            if document.startswith(("{\\fonttbl", "{\\stylesheet", "{\\colortbl", "{\\*"), index):
                skip = True
            index += 1
        elif char == "}":
            skip = stack.pop()
            index += 1
        elif char == "\\":
            match = re.match(r"\\([a-zA-Z]+)(-?\d+)? ?", document[index:])
            if match:
                control, number = match[1], match[2]
                index += len(match[0])
                if not skip:
                    if control in ("par", "line"):
                        output.append("\n")
                    elif control == "tab":
                        output.append("\t")
                    elif control == "u":
                        unit = int(number) & 0xffff
                        output.append(chr(unit))
                        if index < len(document) and document[index] == "?":
                            index += 1
            else:
                if not skip and document[index + 1] in "\\{}":
                    output.append(document[index + 1])
                index += 2
        else:
            if not skip and char not in "\r\n":
                output.append(char)
            index += 1
    if stack:
        raise AssertionError("Unbalanced RTF groups")
    text = "".join(output)
    return text.encode("utf-16-le", errors="surrogatepass").decode("utf-16-le")


class InstallerDocumentTests(unittest.TestCase):
    def test_headings_emphasis_code_unicode_and_links_are_native_text(self):
        document = '# Notchling terms\n\n**Free testing** with *optional* services. Use `C:\\Notes\\{draft}` — 🐉.\n\n[Privacy](privacy.md) and [LICENSE](../LICENSE).\n'
        rtf, plain = renderer.render(document, "docs/product-terms.md")
        actual = visible_rtf(rtf)
        self.assertIn("Notchling terms\nFree testing with optional services. Use C:\\Notes\\{draft} — 🐉.", actual)
        self.assertIn("Privacy (https://github.com/Sury2797/Notchling/blob/main/docs/privacy.md)", actual)
        self.assertIn("LICENSE (https://github.com/Sury2797/Notchling/blob/main/LICENSE)", actual)
        self.assertIn(r"\f0 Segoe UI", rtf)
        self.assertIn(r"\fs34\b", rtf)
        self.assertIn(r"{\b Free testing}", rtf)
        self.assertIn(r"{\i optional}", rtf)
        self.assertIn(r"{\f1 C:", rtf)
        self.assertEqual(" ".join(actual.split()), " ".join(plain.split()))
        self.assertNotIn("**", actual)
        self.assertNotIn("[Privacy]", actual)

    def test_privacy_tables_become_readable_blocks_without_losing_cells(self):
        rtf, _ = renderer.render('| Data | Location / behavior |\n| --- | --- |\n| Clipboard | Off by default; clears on exit |\n| Paths | `%LOCALAPPDATA%\\Notch` |\n', "docs/privacy.md")
        actual = visible_rtf(rtf)
        self.assertIn("Data · Location / behavior\nClipboard\nOff by default; clears on exit\nPaths\n%LOCALAPPDATA%\\Notch", actual)
        self.assertNotIn("|", actual)
        self.assertNotIn("---", actual)

    def test_unsupported_blocks_and_unsafe_links_fail_instead_of_dropping_terms(self):
        for document in ('```\nsecret\n```', '<script>bad</script>', '> quoted obligations', '| A | B |\nno separator'):
            with self.subTest(document=document), self.assertRaises(ValueError):
                renderer.render(document, "docs/privacy.md")
        for target in ("javascript:alert", "http://example.com", "file:///C:/secret"):
            with self.subTest(target=target), self.assertRaises(ValueError):
                renderer.render(f'[Source]({target})', "docs/privacy.md")

    def test_numbered_clauses_nested_links_and_emphasis_preserve_their_meaning(self):
        source = "# Terms\n\n3. Keep **[Privacy](privacy.md)** available.\n4. Preserve *[License](../LICENSE)* and `4. original file`.\n"
        rtf, plain = renderer.render(source, "docs/product-terms.md")
        expected = ("Terms\n3. Keep Privacy (https://github.com/Sury2797/Notchling/blob/main/docs/privacy.md) available.\n"
                    "4. Preserve License (https://github.com/Sury2797/Notchling/blob/main/LICENSE) and 4. original file.")
        self.assertEqual(" ".join(visible_rtf(rtf).split()), " ".join(expected.split()))
        self.assertEqual(" ".join(plain.split()), " ".join(expected.split()))
        self.assertNotIn("[Privacy]", visible_rtf(rtf))

    def test_malformed_table_and_links_fail_before_normalization(self):
        with self.assertRaises(ValueError):
            renderer.render("| A | B |\n| --- | --- | --- |\n| Value | Kept |", "docs/privacy.md")
        for target in ("https://example.com/\tX", "https://example.com/\x00X", "https://example.com/\x7fX"):
            with self.subTest(target=repr(target)), self.assertRaises(ValueError):
                renderer.destination(target, "docs/privacy.md")

    def test_complete_current_terms_and_privacy_survive_native_rendering(self):
        for source in ("docs/product-terms.md", "docs/privacy.md"):
            with self.subTest(source=source):
                markdown = (ROOT / source).read_text(encoding="utf-8")
                rtf, plain = renderer.render(markdown, source)
                actual = visible_rtf(rtf)
                self.assertEqual(" ".join(actual.split()), " ".join(plain.split()))
                self.assertGreater(len(actual), 4000)
                self.assertNotRegex(actual, r"(?m)^#{1,6} |\*\*|\]\(|\| ---")
                # Require the policy and operational details visible in the
                # screenshot; neither a shortened summary nor draft copy may
                # replace the complete terms and data controls.
                required = ("Public testing", "US$2 per month", "14 calendar days", "mandatory consumer rights") if "terms" in source else ("Windows Credential Locker", "up to 50 text entries", "No browser URL", "SHA-256", "Your controls")
                for text in required:
                    self.assertIn(text, actual)

    def test_generation_records_exact_source_and_rtf_and_is_reproducible(self):
        with tempfile.TemporaryDirectory(prefix="Notchling.Installer.Documents-") as directory:
            output = Path(directory)
            manifest = renderer.generate(ROOT, output)
            before = {path.name: path.read_bytes() for path in output.iterdir()}
            renderer.generate(ROOT, output)
            self.assertEqual(before, {path.name: path.read_bytes() for path in output.iterdir()})
            self.assertEqual(manifest, json.loads((output / "installer-documents.json").read_text()))
            for document in manifest["documents"]:
                self.assertEqual(document["sourceSha256"], hashlib.sha256((ROOT / document["source"]).read_bytes()).hexdigest())
                self.assertEqual(document["rtfSha256"], hashlib.sha256((output / document["rtf"]).read_bytes()).hexdigest())
                self.assertTrue((output / document["rtf"]).read_bytes().startswith(b"{\\rtf1"))

    def test_inno_consumes_generated_rtf_and_approved_brand_assets(self):
        inno = (ROOT / "packaging/windows/Notch.iss").read_text()
        self.assertIn('LicenseFile={#InstallerDocumentsDirectory}\\product-terms.rtf', inno)
        self.assertIn('InfoBeforeFile={#InstallerDocumentsDirectory}\\privacy.rtf', inno)
        self.assertNotIn('LicenseFile=..\\..\\docs\\product-terms.md', inno)
        self.assertIn('WizardSmallImageFile=..\\..\\src\\Notch.Windows\\Assets\\Notchling.png', inno)
        self.assertIn('WizardStyle=modern dynamic windows11', inno)


if __name__ == "__main__":
    unittest.main()
