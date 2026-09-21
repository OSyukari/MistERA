"""
Extracts the real color glyphs for a fixed set of emoji directly out of a color-emoji
font and packs them into a single atlas PNG, plus a JSON grid manifest describing
where each one landed.

Color-emoji fonts come in different container formats and each needs a different
extraction strategy:
  - CBDT/CBLC (embedded bitmap glyphs - e.g. the classic "NotoColorEmoji.ttf"): each
    glyph is a literal embedded PNG. Pulled straight out of the CBDT table via
    fontTools and decoded by Pillow - this deliberately avoids FreeType's own CBDT
    rendering path, which requires the underlying libfreetype build to have libpng
    support compiled in; the prebuilt freetype-py wheel does not.
  - COLR/COLRv1 (vector, layered color glyphs - e.g. Google Fonts' newer
    "NotoColorEmoji-Regular.ttf"): no embedded image data exists to extract, so these
    are rasterized via FreeType (through freetype-py) with FT_LOAD_COLOR, which
    performs the layer compositing natively.
  - OpenType-SVG (raw embedded SVG documents): needs an external SVG rasterizer
    FreeType doesn't ship. Not handled here - if every glyph comes back SVG-ONLY,
    switch to a CBDT or COLR build of the font instead.

The font is inspected once at startup (presence of a CBDT table) to pick which of the
two working strategies to use for every glyph.

Requires: fontTools, freetype-py, Pillow  (pip install fonttools freetype-py pillow)

Usage:
    python extract_noto_color_emoji.py <font.ttf> <out_atlas.png> <out_grid.json>

Each sprite is named "0x<HEX>" after its base codepoint - TMPro's own Sprite Asset
creation (TMP_SpriteAssetMenu.PopulateSpriteTables) auto-fills TMP_SpriteCharacter.unicode
from exactly that naming convention, so no manual/custom unicode assignment is needed
on the Unity side.
"""
import sys
import io
import json
import math
import freetype
from fontTools.ttLib import TTFont
from PIL import Image

# (label, base codepoint, variation selector or None)
# The VS16 (0xFE0F) entries are characters with default TEXT presentation - the color
# glyph in the font is only reachable via the (codepoint, FE0F) variation sequence,
# matching how they actually appear (with the VS16 suffix) in LLM_Preset.json.
TARGETS = [
    ("diamond",   0x1F4A0, None),
    ("heart",     0x1F497, None),
    ("magnifier", 0x1F50D, None),
    ("clapper",   0x1F3AC, None),
    ("vhs",       0x1F4FC, None),
    ("pen",       0x2712,  0xFE0F),
    ("bookmark",  0x1F516, None),
    ("ruler",     0x1F4D0, None),
    ("anchor",    0x2693,  None),
    ("scales",    0x2696,  0xFE0F),
    ("bulb",      0x1F4A1, None),
    ("robot",     0x1F916, None),
    ("film",      0x1F39E, 0xFE0F),
    ("projector", 0x1F4BD, 0xFE0F),
    ("speech",    0x1F4AC, None),
    ("megaphone", 0x1F4E3, None),
    ("cardbox",   0x1F5C3, 0xFE0F),
    ("hammer",    0x1F528, None),
    ("cd",        0x1F4BF, None),
]

DESIRED_PIXEL_SIZE = 136  # only matters for scalable (COLR/COLRv1) fonts

# A result is (label, cp, img_or_None, is_color_or_None).
# img is None with is_color True/False/None distinguishing NOT-FOUND / present-but-empty /
# SVG-only-no-fallback respectively - see per-strategy functions below.


def resolve_glyph_name_tt(font, cp, vs):
    """fontTools-side glyph resolution: cmap format-14 (UVS) first, then plain cmap."""
    if vs is not None:
        for t in font["cmap"].tables:
            if t.format == 14:
                for uni, gname in t.uvsDict.get(vs, []):
                    if uni == cp and gname:
                        return gname
    return font.getBestCmap().get(cp)


def best_cbdt_strike(font):
    cblc = font["CBLC"]
    return max(range(len(cblc.strikes)), key=lambda i: cblc.strikes[i].bitmapSizeTable.ppemY)


def extract_via_cbdt(font_path, targets):
    font = TTFont(font_path)
    strike = font["CBDT"].strikeData[best_cbdt_strike(font)]

    results = []
    for label, cp, vs in targets:
        gname = resolve_glyph_name_tt(font, cp, vs)
        glyph = strike.get(gname) if gname else None
        if glyph is None or not hasattr(glyph, "imageData"):
            results.append((label, cp, None, False))
            continue
        img = Image.open(io.BytesIO(glyph.imageData)).convert("RGBA")
        results.append((label, cp, img, True))
    return results


def configure_size(face):
    if face.is_scalable:
        face.set_pixel_sizes(DESIRED_PIXEL_SIZE, DESIRED_PIXEL_SIZE)
    else:
        # Bitmap-only fonts ship fixed strikes - select the largest by index rather
        # than requesting it via set_pixel_sizes(), which validates against an exact
        # internal match and rejects values read back from available_sizes.
        sizes = face.available_sizes
        if sizes:
            best_index = max(range(len(sizes)), key=lambda i: sizes[i].height)
            face.select_size(best_index)
        else:
            face.set_pixel_sizes(0, 0)


def glyph_index_for_ft(face, cp, vs):
    if vs is not None:
        # Raw FT call: freetype-py's high-level Face doesn't wrap this one. Returns 0
        # (not an error) when the font has no cmap format-14 entry for this sequence.
        idx = freetype.FT_Face_GetCharVariantIndex(face._FT_Face, cp, vs)
        if idx:
            return idx
    return face.get_char_index(cp)


def render_glyph_ft(face, glyph_index):
    try:
        face.load_glyph(glyph_index, freetype.FT_LOAD_COLOR | freetype.FT_LOAD_RENDER)
    except freetype.FT_Exception as e:
        if "SVG" not in str(e):
            raise
        # This glyph is defined only as OpenType-SVG, which needs an external SVG
        # rasterizer FreeType doesn't ship (see module docstring). Retry without it in
        # case the font also carries a COLR/sbix representation for this glyph.
        try:
            face.load_glyph(glyph_index, freetype.FT_LOAD_NO_SVG | freetype.FT_LOAD_RENDER)
        except freetype.FT_Exception:
            # No non-SVG representation exists for this glyph at all - nothing more we
            # can do without an actual SVG rasterizer.
            return None, None
    bmp = face.glyph.bitmap
    if bmp.width == 0 or bmp.rows == 0:
        return None, False

    buf = bmp.buffer  # flat list of ints, `pitch` bytes per row (may exceed width*bpp)

    if bmp.pixel_mode == freetype.FT_PIXEL_MODE_BGRA:
        img = Image.new("RGBA", (bmp.width, bmp.rows))
        px = img.load()
        for y in range(bmp.rows):
            row = buf[y * bmp.pitch: y * bmp.pitch + bmp.width * 4]
            for x in range(bmp.width):
                b, g, r, a = row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]
                if a:
                    # FreeType's BGRA color bitmaps are alpha-premultiplied - undo that
                    # so the PNG uses ordinary straight alpha, matching what Unity/Pillow expect.
                    scale = 255.0 / a
                    r = min(255, int(r * scale))
                    g = min(255, int(g * scale))
                    b = min(255, int(b * scale))
                px[x, y] = (r, g, b, a)
        return img, True

    # Font had no color data for this specific glyph - fall back to it as a plain
    # alpha mask (white) rather than dropping it, but flag it so it's easy to spot.
    img = Image.new("RGBA", (bmp.width, bmp.rows))
    px = img.load()
    for y in range(bmp.rows):
        row = buf[y * bmp.pitch: y * bmp.pitch + bmp.width]
        for x in range(bmp.width):
            px[x, y] = (255, 255, 255, row[x])
    return img, False


def extract_via_freetype(font_path, targets):
    face = freetype.Face(font_path)
    configure_size(face)

    results = []
    for label, cp, vs in targets:
        glyph_index = glyph_index_for_ft(face, cp, vs)
        if glyph_index == 0:
            results.append((label, cp, None, False))
            continue
        img, is_color = render_glyph_ft(face, glyph_index)
        results.append((label, cp, img, is_color))
    return results


def has_cbdt(font_path):
    try:
        return "CBDT" in TTFont(font_path, lazy=True)
    except Exception:
        return False


def main():
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(1)

    font_path, atlas_path, grid_path = sys.argv[1:4]

    if has_cbdt(font_path):
        print("Detected CBDT table - extracting embedded PNGs directly (fontTools).")
        raw_results = extract_via_cbdt(font_path, TARGETS)
    else:
        print("No CBDT table - rasterizing via FreeType (COLR/COLRv1 path).")
        raw_results = extract_via_freetype(font_path, TARGETS)

    images, names = [], []
    non_color_count = 0
    svg_only_count = 0
    for label, cp, img, is_color in raw_results:
        if img is None:
            if is_color is None:
                svg_only_count += 1
                print(f"SVG-ONLY glyph for {label} (U+{cp:04X}) - no COLR/CBDT/sbix fallback in this font, skipped")
            else:
                print(f"MISSING/EMPTY glyph for {label} (U+{cp:04X}) - skipped")
            continue

        images.append(img)
        names.append(f"0x{cp:04X}")
        if is_color:
            print(f"OK {label} -> 0x{cp:04X} ({img.width}x{img.height})")
        else:
            non_color_count += 1
            print(f"OK {label} -> 0x{cp:04X} ({img.width}x{img.height})  [!] NOT COLOR - grayscale fallback, check this one manually")

    if svg_only_count:
        print(f"\n{svg_only_count}/{len(TARGETS)} target glyph(s) are OpenType-SVG-only in this font, with no")
        print("COLR/CBDT/sbix fallback that can be rasterized without an external SVG renderer.")

    if not images:
        print("\nNothing extracted, aborting")
        sys.exit(1)

    if non_color_count:
        print(f"\n{non_color_count}/{len(images)} extracted glyph(s) had no color data (grayscale fallback used).")

    cell = max(max(im.width for im in images), max(im.height for im in images))
    cols = math.ceil(math.sqrt(len(images)))
    rows = math.ceil(len(images) / cols)
    atlas_h = rows * cell

    atlas = Image.new("RGBA", (cols * cell, atlas_h), (0, 0, 0, 0))
    cells = []
    for i, (img, name) in enumerate(zip(images, names)):
        col, row = i % cols, i // cols
        x = col * cell + (cell - img.width) // 2
        y_top = row * cell + (cell - img.height) // 2
        atlas.paste(img, (x, y_top), img)

        # Unity's SpriteMetaData.rect is measured from the BOTTOM of the texture;
        # Pillow paste coordinates are measured from the top - flip here so the C#
        # side can plug x/y straight into a Rect with no further conversion.
        y_bottom = atlas_h - y_top - img.height
        cells.append({"name": name, "x": x, "y": y_bottom, "width": img.width, "height": img.height})

    atlas.save(atlas_path)
    with open(grid_path, "w") as f:
        json.dump({"cellSize": cell, "cols": cols, "rows": rows, "cells": cells}, f, indent=2)

    print(f"Wrote {atlas_path} ({atlas.width}x{atlas.height}) and {grid_path}")


if __name__ == "__main__":
    main()