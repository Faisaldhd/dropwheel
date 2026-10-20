#!/usr/bin/env python3
"""Stamp verified release facts into the deployed copy of docs/site/index.html.

The page is the source of truth for its own markup.  This script only replaces
the contents of five explicitly delimited regions and rewrites per-version
release download URLs.  It never invents layout, styles or scripts, and it
never touches the repository copy unless explicitly asked with --in-place.

Marker contract (each marker pair must appear exactly once, in order):

  <!--release-version:start-->2.3.0<!--release-version:end-->
  <!--release-mb:start-->60.05<!--release-mb:end-->
  <!--release-bytes:start-->62,967,948<!--release-bytes:end-->
  <!--release-sha256:start-->7381…<!--release-sha256:end-->
  <!--release-status:start-->…<!--release-status:end-->

The status region receives one bilingual status strip.  Every
``releases/download/v<semver>/`` path is rewritten to the release being
deployed; the stable ``releases/latest/download/DropWheel-Setup.exe`` URL is
left alone.

The deploy must fail when the marker is missing or the installer is absent from
SHA256SUMS.txt, so this script exits non-zero without writing anything in
either case.

Exit codes:
  0  verified and (unless --dry-run) written
  2  marker, sums or argument validation failed
"""

from __future__ import annotations

import argparse
import hashlib
import re
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

DEFAULT_ASSET = "DropWheel-Setup.exe"

# key -> marker base name; the region is <!--<base>:start-->...<!--<base>:end-->
MARKERS = {
    "version": "release-version",
    "mb": "release-mb",
    "bytes": "release-bytes",
    "sha256": "release-sha256",
    "status": "release-status",
}

VERSION_RE = re.compile(r"^v?(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$")
SHA_RE = re.compile(r"^[0-9a-fA-F]{64}$")
SUMS_RE = re.compile(r"^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$")
VERSIONED_DOWNLOAD_RE = re.compile(r"releases/download/v(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)/")
# Per-version asset names inside a release URL, e.g.
# releases/download/v2.4.0/DropWheel-2.3.0-Setup.exe must become 2.4.0 as well.
VERSIONED_ASSET_RE = re.compile(
    r"(releases/download/v[^/]+/DropWheel-)(\d+\.\d+\.\d+)(?=-)"
)


@dataclass
class ReleaseFacts:
    version: str  # display form, no leading v
    tag: str  # URL form, with leading v
    asset: str
    size: int
    sha256: str


class StampError(Exception):
    """A release or page fact that makes a deploy unsafe."""


def parse_sums(path: Path, asset: str) -> str:
    """Return the lowercase SHA-256 for ``asset`` from a SHA256SUMS.txt file."""
    if not path.is_file():
        raise StampError(f"SHA256SUMS.txt not found: {path}")
    matches: list[str] = []
    # utf-8-sig tolerates a BOM from hand-edited or Windows-generated sums files.
    for line_number, raw in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        match = SUMS_RE.match(line)
        if not match:
            continue
        digest, name = match.group(1), match.group(2).strip()
        # SHA256SUMS lines may use " *name" (binary marker) or a leading "./".
        name = name[2:] if name.startswith("*") else name
        name = name[2:] if name.startswith("./") else name
        if name == asset:
            matches.append(digest.lower())
    if not matches:
        raise StampError(
            f"{asset!r} is missing from {path}. The deploy is refused: a release "
            "without the expected installer asset cannot be published."
        )
    unique = sorted(set(matches))
    if len(unique) > 1:
        raise StampError(f"{asset!r} appears more than once with different hashes in {path}: {unique}")
    digest = unique[0]
    if not SHA_RE.match(digest):
        raise StampError(f"{asset!r} has an invalid SHA-256 in {path}: {digest!r}")
    return digest


def find_regions(text: str, key: str) -> list[tuple[int, int]]:
    """Return every (start, end) region for a marker pair, in document order.

    A marker may legitimately repeat: the page shows the version in the release
    bar, hero facts, release card and installer filenames.  Every wrapped region
    is replaced with the same value.
    """
    base = MARKERS[key]
    start_token = f"<!--{base}:start-->"
    end_token = f"<!--{base}:end-->"
    starts = [match.start() for match in re.finditer(re.escape(start_token), text)]
    ends = [match.start() for match in re.finditer(re.escape(end_token), text)]
    if not starts and not ends:
        return []
    if len(starts) != len(ends):
        raise StampError(
            f"marker {base!r} is unbalanced: {len(starts)} start token(s), {len(ends)} end token(s)"
        )
    regions: list[tuple[int, int]] = []
    for index, (start, end) in enumerate(zip(starts, ends)):
        if start > end:
            raise StampError(f"marker {base!r} #{index + 1} has its end before its start")
        if index + 1 < len(starts) and end + len(end_token) > starts[index + 1]:
            raise StampError(f"marker {base!r} #{index + 1} is nested inside itself")
        regions.append((start, end + len(end_token)))
    return regions


def check_markers(text: str) -> dict[str, list[tuple[int, int]]]:
    """Validate all marker pairs and make sure their regions do not overlap."""
    regions: dict[str, list[tuple[int, int]]] = {}
    missing: list[str] = []
    for key in MARKERS:
        found = find_regions(text, key)
        if not found:
            missing.append(MARKERS[key])
        regions[key] = found
    if missing:
        detail = "\n".join(f"  <!--{name}:start-->...<!--{name}:end-->" for name in missing)
        raise StampError(
            "release marker(s) missing from index.html:\n"
            f"{detail}\n"
            "The page owner must add every marker pair before a deploy. "
            "Refusing to stamp; nothing was written."
        )
    if len(regions["status"]) != 1:
        raise StampError(
            f"marker {MARKERS['status']!r} must appear exactly once (found {len(regions['status'])})"
        )

    flattened = sorted(
        (start, end, key) for key, found in regions.items() for start, end in found
    )
    for (_, left_end, left_key), (right_start, _, right_key) in zip(flattened, flattened[1:]):
        if left_end > right_start:
            raise StampError(
                f"marker regions overlap: {MARKERS[left_key]!r} and {MARKERS[right_key]!r}"
            )
    return regions


def status_block(facts: ReleaseFacts) -> str:
    version = facts.version
    return (
        '<span class="release-bar__tag"><bdi dir="ltr">'
        f"{version}"
        "</bdi></span>\n"
        '<p class="release-bar__text">\n'
        '  <span data-lang="en"><bdi dir="ltr">'
        f"{version}"
        "</bdi> is verified and published. The installer below is the verified "
        "release build.</span>\n"
        '  <span data-lang="ar" lang="ar">تم التحقق من <bdi dir="ltr">'
        f"{version}"
        "</bdi> ونشره. المثبّت أدناه هو بناء الإصدار الذي تم التحقق منه.</span>\n"
        "</p>"
    )


def replacement_for(key: str, facts: ReleaseFacts) -> str:
    if key == "version":
        return facts.version
    if key == "mb":
        return f"{facts.size / 1048576:.2f}"
    if key == "bytes":
        return f"{facts.size:,}"
    if key == "sha256":
        return facts.sha256
    if key == "status":
        return status_block(facts)
    raise AssertionError(key)


def render(text: str, facts: ReleaseFacts) -> tuple[str, dict[str, str], int]:
    regions = check_markers(text)
    replacements = {key: replacement_for(key, facts) for key in MARKERS}

    # Apply from the end so earlier offsets stay valid.  Every occurrence of a
    # value marker gets the same replacement.
    stamped = text
    flattened = sorted(
        ((start, end, key) for key, found in regions.items() for start, end in found),
        reverse=True,
    )
    for start, end, key in flattened:
        base = MARKERS[key]
        stamped = (
            stamped[:start]
            + f"<!--{base}:start-->"
            + replacements[key]
            + f"<!--{base}:end-->"
            + stamped[end:]
        )

    older = sorted(
        {
            match.group(1)
            for match in VERSIONED_DOWNLOAD_RE.finditer(stamped)
            if match.group(1) != facts.version
        }
        | {
            match.group(2)
            for match in VERSIONED_ASSET_RE.finditer(stamped)
            if match.group(2) != facts.version
        }
    )
    stamped, rewrite_count = VERSIONED_DOWNLOAD_RE.subn(
        lambda _match: f"releases/download/{facts.tag}/", stamped
    )
    # The path prefix and the per-version asset filename are separate strings in
    # the page; keep both on the release being deployed so the link cannot 404.
    stamped, asset_count = VERSIONED_ASSET_RE.subn(
        lambda match: f"{match.group(1)}{facts.version}", stamped
    )
    if rewrite_count or asset_count:
        print(
            "Rewrote per-version download URLs: "
            + (", ".join(older) if older else "already current")
            + f" -> {facts.tag} ({rewrite_count} path prefix(es), {asset_count} asset filename(s))"
        )
    else:
        print(f"No per-version download URL found; stable latest/download URLs are untouched.")

    # The markers must all still be present and no placeholder may survive.
    check_markers(stamped)
    return stamped, replacements, rewrite_count


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--index", default="docs/site/index.html", help="index.html to stamp (default: docs/site/index.html)")
    parser.add_argument("--sums", required=True, help="local SHA256SUMS.txt downloaded from the release")
    parser.add_argument("--version", required=True, help="release version, e.g. 2.4.0 or v2.4.0")
    parser.add_argument("--asset-name", default=DEFAULT_ASSET, help=f"installer asset name (default: {DEFAULT_ASSET})")
    parser.add_argument("--asset-size", type=int, required=True, help="expected asset size in bytes from the release API")
    parser.add_argument("--output", default=None, help="write a stamped copy of the whole site here (deploy artifact)")
    parser.add_argument("--in-place", action="store_true", help="edit --index directly (maintainer use only)")
    parser.add_argument("--dry-run", action="store_true", help="validate and print the facts without writing anything")
    parser.add_argument("--force", action="store_true", help="allow an existing --output directory to be replaced")
    args = parser.parse_args(argv)

    try:
        version_match = VERSION_RE.match(args.version.strip())
        if not version_match:
            raise StampError(f"invalid release version: {args.version!r} (expected e.g. 2.4.0 or v2.4.0)")
        version = version_match.group(1)
        if args.asset_size <= 0:
            raise StampError("--asset-size must be a positive byte count")
        if args.dry_run and args.in_place:
            raise StampError("--dry-run and --in-place are mutually exclusive")

        index = Path(args.index)
        if not index.is_file():
            raise StampError(f"index.html not found: {index}")
        # Resolve once so --index docs/site/index.html and --output DIR work
        # together: index.relative_to(site_root) must compare absolute paths.
        index = index.resolve()
        sums = Path(args.sums)
        sha256 = parse_sums(sums, args.asset_name)
        facts = ReleaseFacts(
            version=version,
            tag=f"v{version}",
            asset=args.asset_name,
            size=args.asset_size,
            sha256=sha256,
        )

        original = index.read_text(encoding="utf-8")
        stamped, replacements, _rewrite_count = render(original, facts)

        print("Release facts verified:")
        print(f"  version : {facts.version}")
        print(f"  bytes   : {facts.size:,} ({facts.size / 1048576:.2f} MiB)")
        print(f"  sha256  : {facts.sha256}")
        print(f"  asset   : {facts.asset}")
        print(f"  sums    : {sums}")
        print(f"  source  : {index}")
        print()
        print("Marker values written:")
        for key in MARKERS:
            value = replacements[key]
            preview = value if key != "status" else "<bilingual status strip>"
            print(f"  {MARKERS[key]:<16} {preview}")

        if args.dry_run:
            print()
            print("DRY RUN: no files were modified.")
            print(f"Stamped size: {len(stamped.encode('utf-8')):,} bytes (was {len(original.encode('utf-8')):,})")
            print(f"Stamped sha256: {hashlib.sha256(stamped.encode('utf-8')).hexdigest()}")
            return 0

        if args.output:
            output = Path(args.output)
            if args.in_place:
                raise StampError("--output and --in-place are mutually exclusive")
            site_root = index.parent.resolve()
            if output.exists():
                if not args.force:
                    raise StampError(f"output directory already exists: {output} (pass --force to replace)")
                shutil.rmtree(output)
            resolved_output = output.resolve()
            try:
                resolved_output.relative_to(site_root)
            except ValueError:
                pass
            else:
                raise StampError("--output must be outside the site root")
            shutil.copytree(site_root, resolved_output)
            destination = resolved_output / index.relative_to(site_root)
        elif args.in_place:
            destination = index
        else:
            raise StampError("nothing to do: pass --output DIR (deploy), --in-place, or --dry-run")

        destination.write_text(stamped, encoding="utf-8")
        digest = hashlib.sha256(destination.read_bytes()).hexdigest()
        print()
        print(f"Wrote stamped page: {destination}")
        print(f"Stamped sha256    : {digest}")
        if destination.resolve() != index.resolve():
            print(f"Repository page untouched: {index}")
        return 0
    except StampError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
