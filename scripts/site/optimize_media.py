#!/usr/bin/env python3
"""Deterministic media optimiser for the DropWheel website.

Sources (committed, real app renders):
  docs/screenshots/motion/*.webp, *-poster.png
  docs/screenshots/lock-badge/*.png

Outputs (shipped):
  docs/site/assets/motion/**, docs/site/assets/tools/**, docs/site/assets/lock/**

Every output is derived from its source on each run, so the result is
reproducible and idempotent.  The script prints a byte table, enforces the
per-file targets and the aggregate budgets, and exits non-zero on failure.

Usage:
  python scripts/site/optimize_media.py            # write outputs + report
  python scripts/site/optimize_media.py --check    # verify outputs, no writes
  python scripts/site/optimize_media.py --markdown # markdown byte table

Requires Pillow (bundled with the harness Python); no other dependencies.

NOTE: the repository .gitignore has a bare ``tools/`` rule, so new files written under
``docs/site/assets/tools/`` are ignored by a plain ``git add`` and must be staged with
``git add -f`` (the existing variants are already tracked).
"""

from __future__ import annotations

import argparse
import io
import os
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs" / "screenshots"
SITE = ROOT / "docs" / "site" / "assets"

# Targets (decimal bytes, stricter than KiB).
POSTER_LIMIT = 12_000
HERO_CLIP_LIMIT = 55_000
TOOL_CLIP_LIMIT = 45_000
LOCK_LIMIT = 5_000
TOTAL_LIMIT = 4_000_000
HERO_CRITICAL_LIMIT = 110_000

# Highest-first; the first quality that fits a cap wins.
Q_LADDER = (90, 88, 85, 82, 80, 78, 76, 75, 74, 72, 70, 68, 65, 62, 60, 58)
# When a clip is resized, its width may not go below this.
MIN_CLIP_WIDTH = 360
# Native clip width is only used down to this quality before falling back to a
# smaller width, so we never pick a blurry full-size clip over a crisp small one.
NATIVE_CLIP_MIN_Q = 72
# 1x/2x srcset variants must be materially lighter than the native file, so they
# do not simply re-spend the native byte budget at maximum quality.
VARIANT_MAX_Q = 84

TOOLS = ("crop", "editphotos", "annotate", "redact", "addbackground")
THEMES = ("dark", "light")


def poster_source(name: str, theme: str) -> Path:
    return SRC / "motion" / f"tool-{name}-{theme}-poster.png"


def clip_source(name: str, theme: str) -> Path:
    return SRC / "motion" / f"tool-{name}-{theme}.webp"


def poster_out(name: str, theme: str) -> Path:
    return SITE / "tools" / f"{name}-{theme}.webp"


def poster_variant(name: str, theme: str) -> Path:
    return SITE / "tools" / f"{name}-{theme}-320.webp"


def clip_out(name: str, theme: str) -> Path:
    return SITE / "tools" / f"{name}-{theme}-clip.webp"


def open_poster(path: Path) -> Image.Image:
    """Keep alpha only where the source actually uses it."""
    img = Image.open(path)
    img.load()
    if img.mode == "RGBA" and img.getchannel("A").getextrema()[0] < 255:
        return img
    return img.convert("RGB")


def encode_poster(img: Image.Image, quality: int) -> bytes:
    buf = io.BytesIO()
    img.save(buf, "WEBP", quality=quality, method=4, alpha_quality=100)
    return buf.getvalue()


def read_clip(path: Path) -> tuple[list[Image.Image], list[int]]:
    clip = Image.open(path)
    frames: list[Image.Image] = []
    durations: list[int] = []
    for index in range(getattr(clip, "n_frames", 1)):
        clip.seek(index)
        frames.append(clip.convert("RGBA"))
        durations.append(int(clip.info.get("duration") or 80))
    if not frames:
        raise SystemExit(f"empty clip: {path}")
    return frames, durations


def resize_frames(frames: list[Image.Image], width: int) -> list[Image.Image]:
    native_width, native_height = frames[0].size
    if width == native_width:
        return frames
    if width > native_width:
        raise SystemExit(f"refusing to upscale {width} > {native_width}")
    height = max(1, round(native_height * width / native_width))
    return [frame.resize((width, height), Image.Resampling.LANCZOS) for frame in frames]


def encode_clip(frames: list[Image.Image], durations: list[int], quality: int) -> bytes:
    buf = io.BytesIO()
    frames[0].save(
        buf,
        "WEBP",
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        loop=0,
        quality=quality,
        method=4,
        alpha_quality=100,
    )
    return buf.getvalue()


def choose_poster(img: Image.Image, cap: int, max_quality: int = 90) -> tuple[int, bytes]:
    for quality in Q_LADDER:
        if quality > max_quality:
            continue
        data = encode_poster(img, quality)
        if len(data) <= cap:
            return quality, data
    raise SystemExit("poster cannot meet its target even at the lowest quality")


def choose_clip(
    frames: list[Image.Image],
    durations: list[int],
    cap: int,
    widths: tuple[int, ...],
    native_min_q: int = NATIVE_CLIP_MIN_Q,
) -> tuple[int, int, bytes]:
    """Widest acceptable width, then highest quality that fits."""
    native_width = frames[0].size[0]
    for width_index, width in enumerate(widths):
        if width > native_width:
            continue
        ladder = Q_LADDER
        if width_index == 0 and len(widths) > 1:
            ladder = tuple(q for q in Q_LADDER if q >= native_min_q)
        scaled = resize_frames(frames, width)
        for quality in ladder:
            data = encode_clip(scaled, durations, quality)
            if len(data) <= cap:
                return scaled[0].size[0], quality, data
    raise SystemExit("clip cannot meet its target even at the smallest width/quality")


def describe_image(data: bytes, animated: bool = False) -> tuple[int, int, int]:
    image = Image.open(io.BytesIO(data))
    frames = getattr(image, "n_frames", 1) if animated else 1
    return image.size[0], image.size[1], frames


def human_table(rows: list[dict]) -> str:
    header = (
        f"{'output':52s} {'px':>10s} {'fr':>3s} {'q':>3s} {'before':>8s} "
        f"{'after':>8s} {'limit':>7s}  status"
    )
    lines = [header, "-" * len(header)]
    for row in rows:
        before = "-" if row["before"] is None else str(row["before"])
        px = f'{row["width"]}x{row["height"]}'
        lines.append(
            f'{row["path"]:52s} {px:>10s} {row["frames"]:>3d} {row["quality"]:>3d} '
            f'{before:>8s} {row["after"]:>8d} {row["limit"]:>7d}  {row["status"]}'
        )
    return "\n".join(lines)


def markdown_table(rows: list[dict]) -> str:
    lines = [
        "| file | pixels | frames | WebP q | before (B) | after (B) | target (B) | status |",
        "| --- | --- | --- | --- | --- | --- | --- | --- |",
    ]
    for row in rows:
        before = "—" if row["before"] is None else str(row["before"])
        lines.append(
            f'| `{row["path"]}` | {row["width"]}×{row["height"]} | {row["frames"]} | '
            f'{row["quality"]} | {before} | {row["after"]} | {row["limit"]} | {row["status"]} |'
        )
    return "\n".join(lines)


def write_atomic(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify outputs, never write")
    parser.add_argument("--markdown", action="store_true", help="print a markdown table")
    args = parser.parse_args()

    jobs: list[dict] = []

    # ---- wheel posters (hero still + #wheel still) -------------------------
    for theme in THEMES:
        source = SRC / "motion" / f"wheel-{theme}-poster.png"
        image = open_poster(source)
        before = (SITE / "motion" / f"wheel-{theme}-poster.webp")
        cap = min(POSTER_LIMIT, before.stat().st_size)
        quality, data = choose_poster(image, cap)
        width, height, frames = describe_image(data)
        jobs.append(
            dict(path=f"motion/wheel-{theme}-poster.webp", out=before, data=data,
                 before=before.stat().st_size, width=width, height=height, frames=frames,
                 limit=POSTER_LIMIT, status="ok", quality=quality)
        )

        variant_out = SITE / "motion" / f"wheel-{theme}-poster-360.webp"
        scaled = image.resize(
            (360, round(image.size[1] * 360 / image.size[0])), Image.Resampling.LANCZOS
        )
        quality, data = choose_poster(scaled, POSTER_LIMIT, max_quality=VARIANT_MAX_Q)
        width, height, frames = describe_image(data)
        jobs.append(
            dict(path=f"motion/wheel-{theme}-poster-360.webp", out=variant_out, data=data,
                 before=None, width=width, height=height, frames=frames,
                 limit=POSTER_LIMIT, status="ok", quality=quality)
        )

    # ---- wheel clips (hero, animated) -------------------------------------
    for theme in THEMES:
        source = SRC / "motion" / f"wheel-{theme}.webp"
        frames, durations = read_clip(source)
        native = frames[0].size[0]
        before = SITE / "motion" / f"wheel-{theme}.webp"
        cap = min(HERO_CLIP_LIMIT, before.stat().st_size)
        width, quality, data = choose_clip(
            frames, durations, cap, (native, 400, 360), native_min_q=62
        )
        w, h, frame_count = describe_image(data, animated=True)
        jobs.append(
            dict(path=f"motion/wheel-{theme}.webp", out=before, data=data,
                 before=before.stat().st_size, width=w, height=h, frames=frame_count,
                 limit=HERO_CLIP_LIMIT, status="ok", quality=quality)
        )

    # ---- tool posters + 320w variants -------------------------------------
    for name in TOOLS:
        for theme in THEMES:
            source = poster_source(name, theme)
            image = open_poster(source)
            before = poster_out(name, theme)
            cap = min(POSTER_LIMIT, before.stat().st_size)
            quality, data = choose_poster(image, cap)
            w, h, frames = describe_image(data)
            jobs.append(
                dict(path=f"tools/{name}-{theme}.webp", out=before, data=data,
                     before=before.stat().st_size, width=w, height=h, frames=frames,
                     limit=POSTER_LIMIT, status="ok", quality=quality)
            )

            variant_out = poster_variant(name, theme)
            scaled = image.resize(
                (320, round(image.size[1] * 320 / image.size[0])), Image.Resampling.LANCZOS
            )
            quality, data = choose_poster(scaled, POSTER_LIMIT, max_quality=VARIANT_MAX_Q)
            w, h, frames = describe_image(data)
            jobs.append(
                dict(path=f"tools/{name}-{theme}-320.webp", out=variant_out, data=data,
                     before=None, width=w, height=h, frames=frames,
                     limit=POSTER_LIMIT, status="ok", quality=quality)
            )

    # ---- tool clips (animated) --------------------------------------------
    for name in TOOLS:
        for theme in THEMES:
            source = clip_source(name, theme)
            frames, durations = read_clip(source)
            native = frames[0].size[0]
            before = clip_out(name, theme)
            cap = min(TOOL_CLIP_LIMIT, before.stat().st_size)
            width, quality, data = choose_clip(frames, durations, cap, (native, MIN_CLIP_WIDTH))
            w, h, frame_count = describe_image(data, animated=True)
            jobs.append(
                dict(path=f"tools/{name}-{theme}-clip.webp", out=before, data=data,
                     before=before.stat().st_size, width=w, height=h, frames=frame_count,
                     limit=TOOL_CLIP_LIMIT, status="ok", quality=quality)
            )

    # ---- lock badges (real icon renders on Explorer backgrounds) ----------
    lock_map = {
        "document-dark.webp": "document-dark-256.png",
        "document-light.webp": "document-white-256.png",
        "folder-dark.webp": "folder-dark-256.png",
        "folder-light.webp": "folder-white-256.png",
    }
    for out_name, source_name in lock_map.items():
        source = SRC / "lock-badge" / source_name
        image = Image.open(source)
        image.load()
        image = image.convert("RGB")  # fully opaque previews
        before = SITE / "lock" / out_name
        cap = min(LOCK_LIMIT, before.stat().st_size)
        quality, data = choose_poster(image, cap)
        w, h, frames = describe_image(data)
        jobs.append(
            dict(path=f"lock/{out_name}", out=before, data=data,
                 before=before.stat().st_size, width=w, height=h, frames=frames,
                 limit=LOCK_LIMIT, status="ok", quality=quality)
        )

    # ---- verify / write ----------------------------------------------------
    failures: list[str] = []
    for row in jobs:
        current = row["out"].stat().st_size if row["out"].exists() else None
        if args.check:
            if current is None:
                failures.append(f'{row["path"]}: missing')
                row["status"] = "MISSING"
            elif current != len(row["data"]):
                failures.append(
                    f'{row["path"]}: on disk {current} B, reproducible output {len(row["data"])} B'
                )
                row["status"] = "STALE"
            else:
                row["status"] = "ok"
        else:
            write_atomic(row["out"], row["data"])
        if len(row["data"]) > row["limit"]:
            failures.append(f'{row["path"]}: {len(row["data"])} B > {row["limit"]} B target')
            row["status"] = "OVER"
        if row["before"] is not None and len(row["data"]) > row["before"]:
            failures.append(f'{row["path"]}: grew {row["before"]} -> {len(row["data"])} B')
            row["status"] = "GREW"

    # ---- aggregate budgets -------------------------------------------------
    media_files = sorted(
        p for p in SITE.rglob("*")
        if p.is_file() and p.suffix.lower() in {".webp", ".png", ".jpg", ".jpeg", ".avif", ".gif"}
    )
    total = sum(p.stat().st_size for p in media_files)
    hero_critical_paths = [
        SITE / "motion/wheel-dark-poster.webp",
        SITE / "motion/wheel-dark.webp",
        SITE / "icon/dropwheel-128.webp",
        SITE / "lock/document-dark.webp",
        SITE / "lock/document-light.webp",
        SITE / "lock/folder-dark.webp",
        SITE / "lock/folder-light.webp",
    ]
    hero_critical = sum(p.stat().st_size for p in hero_critical_paths)
    if total > TOTAL_LIMIT:
        failures.append(f"docs/site/assets images total {total} B > {TOTAL_LIMIT} B")
    if hero_critical > HERO_CRITICAL_LIMIT:
        failures.append(f"hero-critical set {hero_critical} B > {HERO_CRITICAL_LIMIT} B")

    rows = [
        {
            "path": row["path"],
            "width": row["width"],
            "height": row["height"],
            "frames": row["frames"],
            "quality": row["quality"],
            "before": row["before"],
            "after": len(row["data"]),
            "limit": row["limit"],
            "status": row["status"],
        }
        for row in jobs
    ]
    rows.sort(key=lambda r: r["path"])

    print(markdown_table(rows) if args.markdown else human_table(rows))
    print()
    print(f"images in docs/site/assets : {len(media_files)} files, {total} B")
    print(f"hero-critical set          : {hero_critical} B (limit {HERO_CRITICAL_LIMIT} B)")
    print(f"poster target {POSTER_LIMIT} B | hero clip {HERO_CLIP_LIMIT} B | "
          f"tool clip {TOOL_CLIP_LIMIT} B | lock {LOCK_LIMIT} B")

    if failures:
        print("\nFAILURES:")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print("\nOK: all targets met" + (" (outputs verified byte-identical)" if args.check else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
