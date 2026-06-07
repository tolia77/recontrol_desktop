#!/usr/bin/env python3
"""
generate-icons.py — Generate ReControl app icon set from the square brand mark.

Source: recontrol_frontend/src/assets/img/logo.svg (square mark; NOT logo-full*)
Fallback: recontrol_frontend/src/assets/img/logo.png (Pillow-only, if cairosvg
          Cairo native libs are unavailable on Windows — Pitfall 4).

Outputs:
  ReControl.Desktop/Assets/recontrol.ico       — multi-size ICO (16/32/48/64/128/256)
  ReControl.Desktop/Assets/recontrol-{N}.png  — PNG set at 16/32/48/64/128/256/512 px

Run from: recontrol_desktop/ directory
  python scripts/generate-icons.py
"""

import io
import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DESKTOP_ROOT = os.path.dirname(SCRIPT_DIR)  # recontrol_desktop/
WORKSPACE_ROOT = os.path.dirname(DESKTOP_ROOT)  # COURSEWORK_NEW/

SVG_PATH = os.path.join(WORKSPACE_ROOT, "recontrol_frontend", "src", "assets", "img", "logo.svg")
PNG_PATH = os.path.join(WORKSPACE_ROOT, "recontrol_frontend", "src", "assets", "img", "logo.png")
OUT_ASSETS = os.path.join(DESKTOP_ROOT, "ReControl.Desktop", "Assets")

sizes = [16, 32, 48, 64, 128, 256, 512]
ico_sizes = [16, 32, 48, 64, 128, 256]

pngs = {}

# Primary path: cairosvg SVG rasterization
svg_ok = False
try:
    import cairosvg
    from PIL import Image

    print("cairosvg available — rasterizing from SVG (best quality)")
    for size in sizes:
        png_bytes = cairosvg.svg2png(url=SVG_PATH, output_width=size, output_height=size)
        img = Image.open(io.BytesIO(png_bytes)).convert("RGBA")
        pngs[size] = img
        out_path = os.path.join(OUT_ASSETS, f"recontrol-{size}.png")
        img.save(out_path)
        print(f"  wrote {out_path}")
    svg_ok = True

except ImportError:
    print("cairosvg not installed — falling back to PNG source")
except OSError as e:
    print(f"cairosvg Cairo native lib missing on this host ({e})")
    print("Falling back to logo.png + Pillow-only resize (Pitfall 4 fallback)")

# Fallback path: resize from logo.png using Pillow
if not svg_ok:
    from PIL import Image

    if not os.path.exists(PNG_PATH):
        print(f"ERROR: logo.png not found at {PNG_PATH}", file=sys.stderr)
        sys.exit(1)

    src = Image.open(PNG_PATH).convert("RGBA")
    src_w, src_h = src.size
    print(f"Source logo.png: {src_w}x{src_h} px")

    if src_w < 512 or src_h < 512:
        print(
            f"WARNING: logo.png is {src_w}x{src_h} — smaller than 512px target. "
            "The 512px PNG will be upscaled (quality limitation). "
            "Use cairosvg on a platform with Cairo native libs for lossless SVG rasterization."
        )

    for size in sizes:
        resample = Image.LANCZOS
        img = src.resize((size, size), resample)
        pngs[size] = img
        out_path = os.path.join(OUT_ASSETS, f"recontrol-{size}.png")
        img.save(out_path)
        print(f"  wrote {out_path}")

# Write multi-size ICO (16/32/48/64/128/256)
ico_path = os.path.join(OUT_ASSETS, "recontrol.ico")
ico_images = [(s, s) for s in ico_sizes]
pngs[ico_sizes[0]].save(
    ico_path,
    format="ICO",
    sizes=ico_images,
    append_images=[pngs[s] for s in ico_sizes[1:]],
)
print(f"  wrote {ico_path}")

print("Done.")
