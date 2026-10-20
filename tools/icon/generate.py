#!/usr/bin/env python3
"""Reproducible DropWheel icon generator.

Pure Pillow (no network, no fonts, no third-party raster libraries).
The geometry in this file mirrors branding/dropwheel-icon.svg; every raster
size is rendered from the vector description with its own hand-tuned
radius / hub / gap table (never a downscale of a larger bitmap).

    python tools/icon/generate.py            # rewrite every committed asset
    python tools/icon/generate.py --verify   # byte-compare + contract checks
    python tools/icon/generate.py --sheet    # rewrite the QA contact sheet
"""

from __future__ import annotations

import argparse
import io
import math
import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]

# ---------------------------------------------------------------------------
# Brand palette (flat, sRGB, no gradients)
# ---------------------------------------------------------------------------
BODY = (0x82, 0x24, 0xD4)          # #8224D4
HIGHLIGHT = (0xB4, 0x8A, 0xE0)     # #B48AE0
HUB = (0xF5, 0xF1, 0xFB)           # #F5F1FB

# Tray variants keep the existing dark/light mapping:
#   tray-dark  = light glyph, for a DARK taskbar
#   tray-light = dark glyph, for a LIGHT taskbar
TRAY_DARK = {"body": (0xC8, 0xA6, 0xF0), "highlight": (0xA2, 0x4C, 0xFF), "hub": (0xFF, 0xFF, 0xFF)}
TRAY_LIGHT = {"body": (0x5A, 0x3C, 0x86), "highlight": (0x82, 0x24, 0xD4), "hub": (0xFF, 0xFF, 0xFF)}
APP = {"body": BODY, "highlight": HIGHLIGHT, "hub": HUB}

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
TRAY_SIZES = [16, 20, 24, 32, 40, 48]
WEB_SIZES = [16, 32, 48, 128, 180, 256]
# 256 is PNG-compressed as specified.  128 is PNG too: the 120 KB cap cannot be
# met otherwise (32-bit BGRA at 96+128 alone is 103,376 B before the 256 frame),
# and PNG entries are Vista+ compatible.  16-96 stay 32-bit BGRA BMP.
PNG_ICO_SIZES = {128, 256}

# ---------------------------------------------------------------------------
# Hand tuning table.  radius margin, hub radius and radial gap are in *device*
# pixels at that exact size: small frames get fatter gaps and a relatively
# larger hub so six slices and the highlighted slice stay legible.
# ---------------------------------------------------------------------------
TUNE = {
    16:  dict(margin=0.5, hub=1.4, gap=2.0),
    20:  dict(margin=0.5, hub=1.9, gap=2.0),
    24:  dict(margin=0.5, hub=2.2, gap=2.0),
    32:  dict(margin=0.5, hub=3.3, gap=2.2),
    40:  dict(margin=1.0, hub=4.2, gap=2.6),
    48:  dict(margin=1.0, hub=5.0, gap=3.0),
    64:  dict(margin=1.5, hub=6.7, gap=3.6),
    96:  dict(margin=2.0, hub=10.0, gap=4.8),
    128: dict(margin=2.0, hub=13.4, gap=6.0),
    180: dict(margin=3.0, hub=18.8, gap=8.0),
    256: dict(margin=3.0, hub=26.7, gap=9.0),
    1024: dict(margin=12.0, hub=111.0, gap=24.0),
}
SS = 8  # supersampling factor for the analytic vector render

# Tiny sizes get a harder alpha ramp: the 8x box filter leaves a wide
# semi-transparent ring that reads as soft at 100%.  The clamp keeps one AA
# pixel but makes the petal interiors solid; 32 gets a mild lift only.
ALPHA_CLAMP = {16, 20, 24}
ALPHA_LIFT = {32: 0.7}


def params(size: int, palette: dict) -> dict:
    t = TUNE[size]
    radius = size / 2.0 - t["margin"]
    # Constant radial gap expressed as the angular gap that gives `gap` px at
    # the rim.  asin keeps the wedge ends square to the circle.
    half = math.degrees(math.asin(min(0.9, t["gap"] / 2.0 / radius)))
    return {"radius": radius, "hub_r": t["hub"], "half_gap": half, **palette}


def render(size: int, palette: dict) -> Image.Image:
    """Render one frame at supersample scale, then box-filter in premultiplied alpha."""
    p = params(size, palette)
    ss = SS if size <= 256 else 2
    n = size * ss
    c = n / 2.0
    big = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    r = p["radius"] * ss
    box = (c - r, c - r, c + r, c + r)
    for k in range(6):
        start = 30.0 + 60.0 * k + p["half_gap"]
        end = 30.0 + 60.0 * (k + 1) - p["half_gap"]
        fill = p["highlight"] if k == 5 else p["body"]
        d.pieslice(box, start=start, end=end, fill=fill)
    h = p["hub_r"] * ss
    d.ellipse((c - h, c - h, c + h, c + h), fill=p["hub"])
    # Premultiplied resample: transparent pixels are black in a straight-alpha
    # RGBA image, so resizing straight alpha is what creates a dark halo.
    small = big.convert("RGBa").resize((size, size), Image.Resampling.BOX).convert("RGBA")
    if size in ALPHA_CLAMP:
        a = small.getchannel("A").point(
            lambda v: 0 if v < 48 else (255 if v > 128 else round((v - 48) * 255 / 80)))
        small.putalpha(a)
    elif size in ALPHA_LIFT:
        a = small.getchannel("A").point(lambda v: min(255, round(255 * (v / 255.0) ** ALPHA_LIFT[size])))
        small.putalpha(a)
    return small


# ---------------------------------------------------------------------------
# ICO writer (Vista+ layout: PNG for 256, 32-bit BGRA BMP below)
# ---------------------------------------------------------------------------
def _png_bytes(im: Image.Image) -> bytes:
    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True, compress_level=9)
    return buf.getvalue()


def _and_mask(im: Image.Image) -> bytes:
    """1-bpp bottom-up AND mask: 1 = fully transparent."""
    s = im.size[0]
    a = im.getchannel("A").load()
    row_bytes = ((s + 31) // 32) * 4
    rows = []
    for y in range(s - 1, -1, -1):
        row = bytearray(row_bytes)
        for x in range(s):
            if a[x, y] < 128:
                row[x >> 3] |= 0x80 >> (x & 7)
        rows.append(bytes(row))
    return b"".join(rows)


def _bmp_entry(im: Image.Image) -> bytes:
    s = im.size[0]
    xor = im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
    mask = _and_mask(im)
    header = struct.pack("<IiiHHIIiiII", 40, s, s * 2, 1, 32, 0, len(xor) + len(mask), 0, 0, 0, 0)
    return header + xor + mask


def build_ico(frames: dict[int, Image.Image], png_sizes=PNG_ICO_SIZES) -> bytes:
    entries = []
    blob = b""
    offset = 6 + 16 * len(frames)
    for s in sorted(frames):
        data = _png_bytes(frames[s]) if s in png_sizes else _bmp_entry(frames[s])
        w = 0 if s >= 256 else s
        entries.append(struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
        blob += data
    return struct.pack("<HHH", 0, 1, len(frames)) + b"".join(entries) + blob


# ---------------------------------------------------------------------------
# Asset set
# ---------------------------------------------------------------------------
def app_frames() -> dict[int, Image.Image]:
    return {s: render(s, APP) for s in ICO_SIZES}


def tray_frames(palette: dict) -> dict[int, Image.Image]:
    return {s: render(s, palette) for s in TRAY_SIZES}


def apple_touch() -> Image.Image:
    """180 px apple-touch icon: the one place an opaque brand background is allowed."""
    bg = Image.new("RGBA", (180, 180), (0xF5, 0xF1, 0xFB, 0xFF))
    bg.alpha_composite(render(180, APP))
    return bg


def website_assets() -> dict[str, bytes]:
    out: dict[str, bytes] = {}
    for s in WEB_SIZES:
        im = apple_touch() if s == 180 else render(s, APP)
        out[f"dropwheel-{s}.png"] = _png_bytes(im)
    buf = io.BytesIO()
    render(128, APP).save(buf, format="WEBP", lossless=True, method=6)
    out["dropwheel-128.webp"] = buf.getvalue()
    fav = {s: render(s, APP) for s in (16, 32, 48)}
    out["favicon.ico"] = build_ico(fav, png_sizes=set())
    return out


def assets() -> dict[str, bytes]:
    """Map every committed path (relative to ROOT) to its exact bytes."""
    ico = build_ico(app_frames())
    a = {
        "branding/DropWheel.ico": ico,
        "src/FileAction.App/Assets/DropWheel.ico": ico,
        "branding/DropWheel-tray-dark.ico": build_ico(tray_frames(TRAY_DARK), png_sizes=set()),
        "branding/DropWheel-tray-light.ico": build_ico(tray_frames(TRAY_LIGHT), png_sizes=set()),
        "src/FileAction.App/Assets/DropWheel-tray-dark.ico": build_ico(tray_frames(TRAY_DARK), png_sizes=set()),
        "src/FileAction.App/Assets/DropWheel-tray-light.ico": build_ico(tray_frames(TRAY_LIGHT), png_sizes=set()),
        "branding/DropWheel-1024.png": _png_bytes(render(1024, APP)),
        "branding/DropWheel-256.png": _png_bytes(render(256, APP)),
        "src/FileAction.App/Assets/DropWheel.png": _png_bytes(render(256, APP)),
    }
    for name, data in website_assets().items():
        a[f"docs/site/assets/icon/{name}"] = data
    return a


# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------
def parse_ico(data: bytes):
    magic, kind, count = struct.unpack_from("<HHH", data, 0)
    if magic != 0 or kind != 1:
        raise ValueError("not an ICO")
    out = []
    for i in range(count):
        w, h, cc, r, planes, bpp, size, off = struct.unpack_from("<BBBBHHII", data, 6 + 16 * i)
        fmt = "png" if data[off:off + 8] == b"\x89PNG\r\n\x1a\n" else "bmp"
        out.append(dict(size=w or 256, w=w, h=h, bpp=bpp, planes=planes, offset=off,
                        length=size, fmt=fmt, data=data[off:off + size]))
    return out


def luma(px) -> float:
    return 0.2126 * px[0] + 0.7152 * px[1] + 0.0722 * px[2]


def dark_fringe(im: Image.Image):
    """Edge pixels that are both translucent and near-black (the halo metric)."""
    im = im.convert("RGBA")
    px = im.load()
    edge = dark = 0
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            r, g, b, a = px[x, y]
            if 8 < a < 250:
                edge += 1
                if luma((r, g, b)) < 64:
                    dark += 1
    return edge, dark, (100.0 * dark / edge if edge else 0.0)


def decode_entry(e) -> Image.Image:
    if e["fmt"] == "png":
        return Image.open(io.BytesIO(e["data"])).convert("RGBA")
    raw = e["data"]
    s = e["size"]
    xor = raw[40:40 + s * s * 4]
    # stored bottom-up BGRA -> top-down RGBA
    im = Image.frombytes("RGBA", (s, s), xor, "raw", "BGRA")
    return im.transpose(Image.Transpose.FLIP_TOP_BOTTOM)


def _same_frame(a: bytes, b: bytes) -> bool:
    ia = Image.open(io.BytesIO(a)).convert("RGBA")
    ib = Image.open(io.BytesIO(b)).convert("RGBA")
    return ia.size == ib.size and ia.tobytes() == ib.tobytes()


def _same_ico(a: bytes, b: bytes) -> bool:
    ea, eb = parse_ico(a), parse_ico(b)
    ka = [(e["size"], e["w"], e["h"], e["bpp"], e["fmt"]) for e in ea]
    kb = [(e["size"], e["w"], e["h"], e["bpp"], e["fmt"]) for e in eb]
    if ka != kb:
        return False
    return all(decode_entry(x).tobytes() == decode_entry(y).tobytes() for x, y in zip(ea, eb))


def verify() -> int:
    problems: list[str] = []
    generated = assets()
    for rel, data in generated.items():
        path = ROOT / rel
        if not path.is_file():
            problems.append(f"missing {rel}")
            continue
        committed = path.read_bytes()
        # Compare decoded pixels rather than encoder bytes: PNG/zlib/WebP
        # encoders can differ by platform while the artwork is identical.
        if rel.endswith(".ico"):
            if not _same_ico(committed, data):
                problems.append(f"stale {rel} (pixels or directory differ)")
        elif not _same_frame(committed, data):
            problems.append(f"stale {rel} (pixels differ)")
    svg_path = ROOT / "branding/dropwheel-icon.svg"
    if not svg_path.is_file():
        problems.append("missing branding/dropwheel-icon.svg")
    else:
        svg = svg_path.read_text(encoding="utf-8")
        if svg.count("<path") != 6:
            problems.append("SVG must contain exactly 6 slice paths")
        for token in ("#8224D4", "#B48AE0", "#F5F1FB"):
            if token not in svg:
                problems.append(f"SVG missing brand colour {token}")

    ico_path = ROOT / "src/FileAction.App/Assets/DropWheel.ico"
    if ico_path.is_file():
        data = ico_path.read_bytes()
        ents = parse_ico(data)
        sizes = [e["size"] for e in ents]
        if sizes != ICO_SIZES:
            problems.append(f"ICO sizes {sizes} != {ICO_SIZES}")
        if len(set(sizes)) != len(sizes):
            problems.append("ICO has duplicate sizes")
        for e in ents:
            if e["size"] == 256:
                if e["w"] != 0 or e["h"] != 0:
                    problems.append("256 frame directory byte should be 0")
            elif e["w"] != e["size"] or e["h"] != e["size"]:
                problems.append(f"frame {e['size']} directory dims {e['w']}x{e['h']}")
            want = "png" if e["size"] in PNG_ICO_SIZES else "bmp"
            if e["fmt"] != want:
                problems.append(f"{e['size']} frame is {e['fmt']}, expected {want}")
            if e["fmt"] == "bmp" and e["bpp"] != 32:
                problems.append(f"{e['size']} frame is {e['bpp']} bpp, expected 32")
            im = decode_entry(e)
            if im.size != (e["size"], e["size"]):
                problems.append(f"{e['size']} decoded {im.size}")
            edge, dark, pct = dark_fringe(im)
            if dark:
                problems.append(f"{e['size']} px dark alpha fringe: {dark}/{edge} ({pct:.2f}%)")
        if len(data) > 120 * 1024:
            problems.append(f"ICO is {len(data):,} bytes, over the 120 KB budget")
        print(f"ICO  {len(data):,} bytes  frames={len(ents)}  max={max(sizes)}  "
              f"png={[e['size'] for e in ents if e['fmt'] == 'png']}  "
              f"bmp={[e['size'] for e in ents if e['fmt'] == 'bmp']}")

    for name, expect in (("DropWheel-tray-dark.ico", TRAY_SIZES), ("DropWheel-tray-light.ico", TRAY_SIZES)):
        for base in ("branding", "src/FileAction.App/Assets"):
            p = ROOT / base / name
            if not p.is_file():
                problems.append(f"missing {p.relative_to(ROOT)}")
                continue
            ents = parse_ico(p.read_bytes())
            if [e["size"] for e in ents] != expect:
                problems.append(f"{p.relative_to(ROOT)} sizes != {expect}")
            for e in ents:
                if e["fmt"] != "bmp" or e["bpp"] != 32:
                    problems.append(f"{p.relative_to(ROOT)} {e['size']} not 32-bit BMP")
                edge, dark, pct = dark_fringe(decode_entry(e))
                if dark:
                    problems.append(f"{p.relative_to(ROOT)} {e['size']} dark fringe {dark} ({pct:.2f}%)")

    print("PASS" if not problems else "FAIL")
    for p in problems:
        print("  -", p)
    return 0 if not problems else 1


# ---------------------------------------------------------------------------
# QA contact sheet
# ---------------------------------------------------------------------------
def _font(sz: int):
    try:
        return ImageFont.load_default(size=sz)
    except TypeError:
        return ImageFont.load_default()


def sheet(out_path: Path) -> None:
    before_dir = ROOT / "docs/screenshots/icon/before"
    sizes = [16, 24, 32, 48, 256]
    cell_w, label_w, row_h, pad = 150, 170, 170, 26
    y0 = pad + 86
    zoom_y = y0 + 4 * row_h + 42
    width = label_w + cell_w * len(sizes) + pad * 2
    height = 1240
    light_bg = (0xF5, 0xF1, 0xFB, 255)
    dark_bg = (0x12, 0x0D, 0x1A, 255)
    paper = (255, 255, 255, 255)
    ink = (38, 30, 52, 255)
    muted = (90, 80, 105, 255)
    im = Image.new("RGBA", (width, height), paper)
    d = ImageDraw.Draw(im)
    f_title, f_sub, f_lab, f_small = _font(26), _font(14), _font(15), _font(12)
    d.text((pad, pad - 8), "DropWheel icon - before / after", font=f_title, fill=ink)
    d.text((pad, pad + 26), "Before = committed icon. After = generated flat 6-slice mark. "
           "Actual size on #F5F1FB and #120D1A; transparent around the wheel.",
           font=f_sub, fill=muted)

    for col, s in enumerate(sizes):
        d.text((label_w + col * cell_w + 8, y0 - 6), f"{s} px", font=f_small, fill=muted)

    rows = [
        ("before", "light", light_bg, False),
        ("after", "light", light_bg, True),
        ("before", "dark", dark_bg, False),
        ("after", "dark", dark_bg, True),
    ]
    for ri, (kind, mode, bg, is_after) in enumerate(rows):
        y = y0 + ri * row_h
        d.text((pad, y + row_h // 2 - 10), f"{kind} / {mode}", font=f_lab, fill=ink)
        for col, s in enumerate(sizes):
            x = label_w + col * cell_w
            d.rectangle((x + 4, y + 4, x + cell_w - 8, y + row_h - 8), fill=bg)
            if is_after:
                icon = render(s, APP)
            else:
                icon = Image.open(before_dir / f"dropwheel-{s}.png").convert("RGBA")
            show = min(s, 128)
            if s > 128:
                icon = icon.convert("RGBa").resize((show, show), Image.Resampling.LANCZOS).convert("RGBA")
            im.alpha_composite(icon, (x + (cell_w - 8 - show) // 2, y + (row_h - 8 - show) // 2))

    # 400% nearest-neighbour zoom rows for 16/24/32
    d.text((pad, zoom_y), "400% nearest-neighbour zoom (16 / 24 / 32 px, after):", font=f_lab, fill=ink)
    d.text((pad, zoom_y + 22), "Pixel grid visible; edge pixels are lavender/purple, never a black matte.",
           font=f_small, fill=muted)
    for ri, (mode, bg) in enumerate((("light", light_bg), ("dark", dark_bg))):
        y = zoom_y + 52 + ri * 140
        d.text((pad, y + 50), mode, font=f_small, fill=muted)
        for col, s in enumerate([16, 24, 32]):
            x = label_w + col * 140
            d.rectangle((x + 2, y, x + 134, y + 132), fill=bg)
            icon = render(s, APP).resize((s * 4, s * 4), Image.Resampling.NEAREST)
            im.alpha_composite(icon, (x + 2 + (132 - s * 4) // 2, y + 2 + (132 - s * 4) // 2))

    # footer metrics (measured live from the committed ICO)
    ico_path = ROOT / "src/FileAction.App/Assets/DropWheel.ico"
    ico_data = ico_path.read_bytes()
    ents = parse_ico(ico_data)
    after_edge = after_dark = 0
    for e in ents:
        ed, dr, _ = dark_fringe(decode_entry(e))
        after_edge += ed
        after_dark += dr
    oe, od, opct = dark_fringe(Image.open(before_dir / "dropwheel-16.png").convert("RGBA"))
    fy = height - 62
    d.text((pad, fy), f"Alpha fringe at 16 px:  before {od}/{oe} dark edge px ({opct:.1f}%)  ->  "
           f"after {after_dark}/{after_edge} dark edge px over all {len(ents)} ICO frames.",
           font=f_small, fill=ink)
    bmp = [e["size"] for e in ents if e["fmt"] == "bmp"]
    png = [e["size"] for e in ents if e["fmt"] == "png"]
    d.text((pad, fy + 20), f"ICO {len(ico_data):,} bytes - 10 sizes 16-256 - PNG {png}; "
           f"BMP {bmp} - straight alpha, no matte.",
           font=f_small, fill=muted)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    im.convert("RGB").save(out_path, optimize=True)
    print(f"sheet -> {out_path} ({out_path.stat().st_size:,} bytes)")


def dump() -> int:
    rc = 0
    for rel in ("src/FileAction.App/Assets/DropWheel.ico",
                "branding/DropWheel-tray-dark.ico",
                "branding/DropWheel-tray-light.ico"):
        p = ROOT / rel
        data = p.read_bytes()
        ents = parse_ico(data)
        print(f"\n{rel}  {len(data):,} bytes  {len(ents)} frames")
        print(f"  {'size':>4}  {'dims':>9}  {'bpp':>3}  {'format':>6}  {'bytes':>7}  dark fringe")
        for e in ents:
            edge, dark, pct = dark_fringe(decode_entry(e))
            dims = "256x256" if e["size"] == 256 else f"{e['w']}x{e['h']}"
            print(f"  {e['size']:>4}  {dims:>9}  {e['bpp']:>3}  {e['fmt']:>6}  {e['length']:>7,}  "
                  f"{dark}/{edge} ({pct:.2f}%)")
            if dark:
                rc = 1
        if rel.endswith("DropWheel.ico") and len(data) > 120 * 1024:
            print(f"  OVER BUDGET: {len(data):,} > 122,880")
            rc = 1
    return rc


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--verify", action="store_true", help="check committed bytes and the ICO contract")
    ap.add_argument("--sheet", action="store_true", help="rewrite docs/screenshots/icon/icon-quality-sheet.png")
    ap.add_argument("--dump", action="store_true", help="print the ICO directory and per-frame alpha fringes")
    ap.add_argument("--out", type=Path, default=ROOT, help="repository root for writes")
    args = ap.parse_args()
    if args.verify:
        return verify()
    if args.dump:
        return dump()
    if args.sheet:
        sheet(args.out / "docs/screenshots/icon/icon-quality-sheet.png")
        return 0
    for rel, data in assets().items():
        p = args.out / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
        print(f"wrote {rel} ({len(data):,} bytes)")
    sheet(args.out / "docs/screenshots/icon/icon-quality-sheet.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())
