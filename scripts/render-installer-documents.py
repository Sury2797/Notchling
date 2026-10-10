#!/usr/bin/env python3
"""Render the repository's complete legal Markdown into native installer RTF.

No third-party renderer or network access is required. Unsupported block syntax
fails the build instead of dropping terms. Tables become labelled text blocks so
privacy descriptions remain readable in a narrow, DPI-scaled native viewer.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re
from urllib.parse import urljoin, urlparse

REPOSITORY = "https://github.com/Sury2797/Notchling/blob/main/"
INLINE = re.compile(r"\[([^\]\n]+)\]\(([^)\n]+)\)|\*\*([^*\n]+)\*\*|`([^`\n]+)`|(?<!\*)\*([^*\n]+)\*(?!\*)")


@dataclass(frozen=True)
class Block:
    kind: str
    text: str = ""
    level: int = 0
    cells: tuple[str, ...] = ()
    marker: str = ""


def escape_rtf(value: str) -> str:
    """Use signed UTF-16 code units, including surrogate pairs, for RichEdit."""
    output = []
    for character in value:
        if character in "\\{}":
            output.append("\\" + character)
        elif character == "\n":
            output.append(r"\line ")
        elif 32 <= ord(character) < 127:
            output.append(character)
        elif character == "\t":
            output.append(r"\tab ")
        else:
            encoded = character.encode("utf-16-le")
            for offset in range(0, len(encoded), 2):
                unit = int.from_bytes(encoded[offset:offset + 2], "little")
                output.append(f"\\u{unit if unit < 32768 else unit - 65536}?")
    return "".join(output)


def destination(value: str, source: str) -> str:
    if any(ord(ch) < 32 or ord(ch) == 127 for ch in value):
        raise ValueError(f"Unsupported legal-document link: {value!r}")
    url = urljoin(REPOSITORY + source, value)
    parsed = urlparse(url)
    if parsed.scheme != "https" or not parsed.netloc or any(ord(ch) < 32 for ch in url):
        raise ValueError(f"Unsupported legal-document link: {value!r}")
    return url


def inline(value: str, source: str, rich: bool = True) -> str:
    output = []
    position = 0
    literal = escape_rtf if rich else lambda text: text
    for match in INLINE.finditer(value):
        output.append(literal(value[position:match.start()]))
        label, target, bold, code, emphasis = match.groups()
        if label is not None:
            # Show the complete destination as readable text as well as the
            # label. Every URL survives copy/paste and native UI Automation.
            output.append(inline(label, source, rich) + literal(" (" + destination(target, source) + ")"))
        elif bold is not None:
            output.append("{\\b " + inline(bold, source, rich) + "}" if rich else inline(bold, source, rich))
        elif code is not None:
            output.append("{\\f1 " + literal(code) + "}" if rich else code)
        else:
            output.append("{\\i " + inline(emphasis, source, rich) + "}" if rich else inline(emphasis, source, rich))
        position = match.end()
    output.append(literal(value[position:]))
    return "".join(output)


def table_cells(line: str) -> tuple[str, ...]:
    # Current documents do not contain escaped pipe characters. Preserve those
    # too rather than treating them as an extra privacy-table column.
    parts = re.split(r"(?<!\\)\|", line.strip().strip("|"))
    return tuple(part.strip().replace(r"\|", "|") for part in parts)


def parse(markdown: str) -> list[Block]:
    blocks: list[Block] = []
    paragraph: list[str] = []
    lines = markdown.splitlines()

    def flush() -> None:
        if paragraph:
            blocks.append(Block("paragraph", " ".join(paragraph)))
            paragraph.clear()

    index = 0
    while index < len(lines):
        line = lines[index].strip()
        if not line:
            flush()
        elif heading := re.fullmatch(r"(#{1,6})\s+(.+)", line):
            flush()
            blocks.append(Block("heading", heading[2], len(heading[1])))
        elif line.startswith("|"):
            flush()
            header = table_cells(line)
            if index + 1 >= len(lines) or len(table_cells(lines[index + 1])) != len(header) or not all(re.fullmatch(r":?-{3,}:?", item) for item in table_cells(lines[index + 1])):
                raise ValueError(f"Malformed Markdown table at line {index + 1}")
            blocks.append(Block("table-header", cells=header))
            index += 2
            while index < len(lines) and lines[index].strip().startswith("|"):
                cells = table_cells(lines[index])
                if len(cells) != len(header):
                    raise ValueError(f"Markdown table column mismatch at line {index + 1}")
                blocks.append(Block("table-row", cells=cells))
                index += 1
            continue
        elif item := re.fullmatch(r"([-+*]|\d+\.)\s+(.+)", line):
            flush()
            blocks.append(Block("list", item[2], marker="•" if item[1] in "-+*" else item[1]))
        elif line.startswith(("```", "~~~", ">", "<")) or re.fullmatch(r"(?:-{3,}|\*{3,}|_{3,})", line):
            raise ValueError(f"Unsupported legal-document Markdown at line {index + 1}: {line[:60]}")
        else:
            paragraph.append(line)
        index += 1
    flush()
    return blocks


def render(markdown: str, source: str) -> tuple[str, str]:
    rtf = [r"{\rtf1\ansi\ansicpg1252\deff0\uc1", r"{\fonttbl{\f0 Segoe UI;}{\f1 Consolas;}}", r"\viewkind4\pard\f0\fs20\li180\ri180\sl276\slmult1 "]
    plain = []
    header: tuple[str, ...] = ()
    for block in parse(markdown):
        if block.kind == "heading":
            size = 34 if block.level == 1 else 25 if block.level == 2 else 22
            before = 0 if block.level == 1 else 210
            rtf.append(f"\\pard\\f0\\fs{size}\\b\\li180\\ri180\\sb{before}\\sa140 " + inline(block.text, source) + r"\b0\par")
            plain.append(inline(block.text, source, False))
        elif block.kind == "table-header":
            header = block.cells
            text = " · ".join(header)
            rtf.append(r"\pard\f0\fs18\b\li180\ri180\sa110 " + inline(text, source) + r"\b0\par")
            plain.append(inline(text, source, False))
        elif block.kind == "table-row":
            for column, cell in enumerate(block.cells):
                label = cell if column == 0 or len(block.cells) == 2 else header[column] + ": " + cell
                weight = r"\b " if column == 0 else ""
                after = 30 if column < len(block.cells) - 1 else 160
                rtf.append(r"\pard\f0\fs20\li180\ri180\sl276\slmult1" + f"\\sa{after} " + weight + inline(label, source) + r"\b0\par")
                plain.append(inline(label, source, False))
        else:
            prefix = block.marker + " " if block.kind == "list" else ""
            rtf.append(r"\pard\f0\fs20\li180\ri180\sl276\slmult1\sa140 " + escape_rtf(prefix) + inline(block.text, source) + r"\par")
            plain.append(prefix + inline(block.text, source, False))
    rtf.append("}")
    return "\n".join(rtf) + "\n", "\n\n".join(plain) + "\n"


def generate(root: Path, output: Path) -> dict:
    output.mkdir(parents=True, exist_ok=True)
    documents = []
    for source, name in (("docs/product-terms.md", "product-terms"), ("docs/privacy.md", "privacy")):
        payload = (root / source).read_bytes()
        if len(payload) > 262144:
            raise ValueError(f"Installer legal document exceeds its 256 KiB limit: {source}")
        rtf, plain = render(payload.decode("utf-8-sig"), source)
        rich_path = output / (name + ".rtf")
        plain_path = output / (name + ".txt")
        rich_path.write_bytes(rtf.encode("ascii"))
        plain_path.write_bytes(plain.encode("utf-8"))
        documents.append({"source": source, "sourceSha256": hashlib.sha256(payload).hexdigest(), "rtf": rich_path.name, "rtfSha256": hashlib.sha256(rich_path.read_bytes()).hexdigest(), "plainText": plain_path.name})
    manifest = {"schemaVersion": 1, "application": "Notchling", "renderer": "Complete legal text, native Segoe UI RTF; tables presented as labelled blocks.", "documents": documents}
    (output / "installer-documents.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    generate(args.root, args.output)
    print(f"Notchling installer terms and privacy rendered in {args.output}")


if __name__ == "__main__":
    main()
