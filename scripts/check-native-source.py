#!/usr/bin/env python3
"""Compile native C# and XAML member/event projections without running Windows UI.

This deliberately replaces InitializeComponent with empty, never-executed stubs.
It complements the real Windows XAML build; it cannot verify layout or rendering.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "src/Notch.Windows"
OUT = ROOT / "artifacts/native-source-check"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"
SHAPES = {"Path", "Rectangle", "Ellipse", "Polyline", "Polygon", "Line"}
MEDIA = {"SolidColorBrush", "PathGeometry", "GeometryGroup", "TransformGroup", "TranslateTransform", "ScaleTransform", "RotateTransform"}
XAML_TYPES = {"Application", "Window", "ResourceDictionary", "Style", "Setter", "Thickness", "CornerRadius", "VisualState", "VisualStateGroup", "AdaptiveTrigger", "StateTrigger", "DataTemplate", }
PRIMITIVES = {"ToggleButton", "RepeatButton", "ButtonBase", "Popup", "ScrollBar", "Thumb"}
EVENTS = {"Click", "CloseButtonClick", "KeyDown", "PointerEntered", "PointerExited", "PointerPressed", "ValueChanged", "Checked", "Unchecked", "SelectionChanged", "TextChanged", "Loaded", "Unloaded"}
ATTACHED = {"Grid": "Microsoft.UI.Xaml.Controls.Grid", "ToolTipService": "Microsoft.UI.Xaml.Controls.ToolTipService", "AutomationProperties": "Microsoft.UI.Xaml.Automation.AutomationProperties"}


def type_name(tag: str) -> str:
    if tag.startswith("{using:"):
        namespace, tag = tag[7:].split("}", 1)
        return namespace + "." + tag
    tag = tag.split("}")[-1]
    category = "Shapes" if tag in SHAPES else "Media" if tag in MEDIA else "Controls.Primitives" if tag in PRIMITIVES else "" if tag in XAML_TYPES else "Controls"
    return "Microsoft.UI.Xaml." + (category + "." if category else "") + tag


def generate() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    for path in OUT.glob("*.generated.cs"):
        path.unlink()
    documents = [(path, ET.parse(path).getroot()) for path in sorted(BASE.rglob("*.xaml")) if "obj" not in path.parts and "bin" not in path.parts]
    keys = {element.attrib[X + "Key"] for _, root in documents for element in root.iter() if X + "Key" in element.attrib}
    errors = []
    for path, root in documents:
        cls = root.attrib.get(X + "Class")
        namespace, name = cls.rsplit(".", 1) if cls else ("Notch.Validation", path.stem + "Check")
        source = path.with_suffix(path.suffix + ".cs")
        source_text = source.read_text(encoding="utf-8") if source.exists() else ""
        fields, body = [], []
        for index, element in enumerate(root.iter()):
            tag = element.tag.split("}")[-1]
            if "." in tag:
                continue
            typ = type_name(element.tag)
            if field := element.attrib.get(X + "Name"):
                fields.append(f"    private {typ} {field} = null!;")
            var = f"x{index}"
            body.append(f"        var {var} = new {typ}();")
            for attr, value in element.attrib.items():
                if value.startswith("{StaticResource "):
                    key = value[len("{StaticResource "):-1]
                    if key not in keys:
                        errors.append(f"{path.relative_to(ROOT)}: undefined local StaticResource {key}")
                if attr.startswith("{"):
                    continue
                if attr in EVENTS:
                    if not re.search(r"\b" + re.escape(value) + r"\s*\(", source_text):
                        errors.append(f"{path.relative_to(ROOT)}: missing handler {value}")
                    body.append(f"        {var}.{attr} += {value};")
                elif "." in attr:
                    owner, prop = attr.split(".", 1)
                    if owner in ATTACHED:
                        body.append(f"        {ATTACHED[owner]}.Set{prop}({var}, default!);")
                else:
                    body.append(f"        {var}.{attr} = default!;")
        declaration = "sealed partial " if cls else ""
        code = f"namespace {namespace};\npublic {declaration}class {name}\n{{\n" + "\n".join(fields)
        code += "\n    private void InitializeComponent() { }\n    private void ValidateXamlMetadata()\n    {\n" + "\n".join(body) + "\n    }\n}\n"
        (OUT / (name + ".generated.cs")).write_text(code, encoding="utf-8")
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    print(f"Validated XML, local static resources and handlers in {len(documents)} XAML files; generated compile-only projections.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet", help="Path to a .NET 10 SDK host")
    parser.add_argument("--generate-only", action="store_true")
    args = parser.parse_args()
    if code := generate():
        return code
    if args.generate_only:
        return 0
    return subprocess.run([args.dotnet, "build", str(ROOT / "tests/Notch.Native.SourceChecks/Notch.Native.SourceChecks.csproj"), "--configuration", "Release"], cwd=ROOT, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
