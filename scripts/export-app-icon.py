#!/usr/bin/env python3
"""Export Windows shell frames from the approved Notchling PNG master.

Development-only dependency: Pillow. The application needs no image tooling.
"""
from pathlib import Path

from PIL import Image


ASSETS = Path(__file__).resolve().parents[1] / "src/Notch.Windows/Assets"
SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)


def main() -> None:
    with Image.open(ASSETS / "Notchling.png") as source:
        if source.size != (512, 512) or source.mode != "RGBA":
            raise ValueError("The approved master must be a 512 × 512 RGBA PNG.")
        source.save(ASSETS / "Notchling.ico", format="ICO", sizes=[(size, size) for size in SIZES])
    print("Exported Notchling.ico: " + ", ".join(map(str, SIZES)) + " px")


if __name__ == "__main__":
    main()
