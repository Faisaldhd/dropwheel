#!/usr/bin/env python3
"""Measure the uncompressed initial-document payload of the DropWheel site.

The budget is deliberately about the *initial document*: the HTML itself plus
every resource that a browser can request before first paint.  Below-the-fold
media (anything marked ``loading="lazy"``, tool clips, theme variants that are
only swapped at runtime) is out of scope, so the number reflects the download a
visitor pays for before they can read the hero.

Definition (also documented in docs/website/CI.md):

* ``docs/site/index.html`` bytes;
* every local stylesheet, icon, apple-touch-icon, preload and script referenced
  from ``<head>``;
* every local image candidate in the site header (the brand mark) and in the
  hero ``<section class="hero">`` (all ``<source srcset>`` candidates plus the
  fallback ``<img>``);
* any image carrying ``fetchpriority="high"`` anywhere in the document.

Duplicate paths are counted once.  Sizes are raw file bytes (no Content-Encoding
gzip/brotli).  The gate fails when the total is greater than ``--budget``
(default 500,000 bytes).

Exit codes:
  0  total is within budget
  1  total is over budget
  2  the document is malformed or an initial resource is missing
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit

DEFAULT_BUDGET = 500_000
VOID_ELEMENTS = {
    "area",
    "base",
    "br",
    "col",
    "embed",
    "hr",
    "img",
    "input",
    "link",
    "meta",
    "param",
    "source",
    "track",
    "wbr",
}

INITIAL_LINK_RELS = {"stylesheet", "icon", "apple-touch-icon", "preload", "modulepreload"}


class _PayloadParser(HTMLParser):
    """Collect the initial-document resource references from one HTML file."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.stack: list[tuple[str, set[str]]] = []
        self.refs: list[dict[str, object]] = []

    # -- helpers ---------------------------------------------------------
    def _in_head(self) -> bool:
        return any(tag == "head" for tag, _ in self.stack)

    def _ancestor_classes(self) -> set[str]:
        classes: set[str] = set()
        for _tag, cls in self.stack:
            classes |= cls
        return classes

    def _record(self, role: str, url: str, detail: str, line: int) -> None:
        if not url:
            return
        self.refs.append({"role": role, "url": url, "detail": detail, "line": line})

    def _initial_image_context(self, attrs: dict[str, str]) -> str | None:
        """Return a human detail string when an image is part of first paint."""
        classes = set((attrs.get("class") or "").split())
        if "brand__mark" in classes:
            return "header"
        if attrs.get("fetchpriority") == "high":
            return "hero (high priority)"
        for tag, ancestor_classes in self.stack:
            if tag == "section" and "hero" in ancestor_classes:
                return "hero"
            if tag == "header" and "site-header" in ancestor_classes:
                return "header"
        return None

    def _record_srcset(self, value: str, detail: str, line: int) -> None:
        for candidate in value.split(","):
            candidate = candidate.strip()
            if not candidate:
                continue
            url = candidate.split()[0]
            self._record("image", url, detail, line)

    # -- HTMLParser hooks -----------------------------------------------
    def handle_starttag(self, tag: str, attrs_list: list[tuple[str, str | None]]) -> None:
        attrs = {key: (value or "") for key, value in attrs_list}
        line = self.getpos()[0]

        if tag == "link" and self._in_head():
            rels = set(attrs.get("rel", "").lower().split())
            rel = next((r for r in rels if r in INITIAL_LINK_RELS), None)
            if rel:
                role = "stylesheet" if rel == "stylesheet" else "preload" if "preload" in rel else "icon"
                self._record(role, attrs.get("href", ""), f"link rel={rel}", line)
        elif tag == "script" and self._in_head():
            self._record("script", attrs.get("src", ""), "head script", line)
        elif tag in ("img", "source"):
            context = self._initial_image_context(attrs)
            if context is not None:
                if attrs.get("src"):
                    self._record("image", attrs["src"], context, line)
                if attrs.get("srcset"):
                    self._record_srcset(attrs["srcset"], context, line)

        if tag not in VOID_ELEMENTS:
            self.stack.append((tag, set((attrs.get("class") or "").split())))

    def handle_endtag(self, tag: str) -> None:
        for index in range(len(self.stack) - 1, -1, -1):
            if self.stack[index][0] == tag:
                del self.stack[index:]
                break


def _is_local(url: str) -> bool:
    parts = urlsplit(url)
    if parts.scheme or parts.netloc:
        return False
    if url.startswith("//"):
        return False
    return True


def _resolve(root: Path, url: str) -> Path:
    parts = urlsplit(url)
    path = unquote(parts.path)
    if path.startswith("/"):
        target = root / path.lstrip("/")
    else:
        target = root / path
    target = target.resolve()
    root_resolved = root.resolve()
    try:
        target.relative_to(root_resolved)
    except ValueError as exc:
        raise ValueError(f"initial resource escapes the site root: {url}") from exc
    return target


def collect(root: Path, index: Path) -> tuple[list[dict[str, object]], list[str]]:
    text = index.read_text(encoding="utf-8")
    parser = _PayloadParser()
    parser.feed(text)
    parser.close()

    errors: list[str] = []
    seen: dict[Path, dict[str, object]] = {}
    resources: list[dict[str, object]] = []

    def add(path: Path, role: str, detail: str, source: str) -> None:
        if path in seen:
            return
        if not path.is_file():
            errors.append(f"initial resource not found: {source} -> {path}")
            return
        item = {
            "role": role,
            "path": path,
            "relative": path.relative_to(root.resolve()).as_posix(),
            "bytes": path.stat().st_size,
            "detail": detail,
        }
        seen[path] = item
        resources.append(item)

    add(index.resolve(), "document", "initial HTML", index.name)
    for ref in parser.refs:
        url = str(ref["url"])
        if not _is_local(url):
            continue
        try:
            target = _resolve(root, url)
        except ValueError as exc:
            errors.append(str(exc))
            continue
        add(target, str(ref["role"]), str(ref["detail"]), url)

    return resources, errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default="docs/site", help="site root (default: docs/site)")
    parser.add_argument("--index", default=None, help="HTML entry point (default: <root>/index.html)")
    parser.add_argument("--budget", type=int, default=DEFAULT_BUDGET, help="budget in uncompressed bytes (default: 500000)")
    parser.add_argument("--json", action="store_true", help="emit a machine-readable JSON report instead of a table")
    args = parser.parse_args(argv)

    root = Path(args.root)
    index = Path(args.index) if args.index else root / "index.html"
    if not index.is_file():
        print(f"ERROR: entry document not found: {index}", file=sys.stderr)
        return 2

    resources, errors = collect(root, index)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 2

    total = sum(int(item["bytes"]) for item in resources)
    over_budget = total > args.budget
    within = args.budget > 0 and total <= args.budget

    if args.json:
        print(
            json.dumps(
                {
                    "root": str(root),
                    "index": str(index),
                    "budget": args.budget,
                    "total": total,
                    "over_budget": over_budget,
                    "files": [
                        {
                            "role": item["role"],
                            "path": item["relative"],
                            "bytes": item["bytes"],
                            "detail": item["detail"],
                        }
                        for item in resources
                    ],
                },
                indent=2,
            )
        )
        return 1 if over_budget else 0

    role_width = max(len("document"), max((len(str(item["role"])) for item in resources), default=0))
    path_width = max(len("file"), max((len(str(item["relative"])) for item in resources), default=0))
    print("Initial-document payload budget (uncompressed bytes)")
    print(f"{'role':<{role_width}}  {'file':<{path_width}}  {'bytes':>10}  detail")
    print(f"{'-' * role_width}  {'-' * path_width}  {'-' * 10}  {'-' * 20}")
    for item in resources:
        print(
            f"{str(item['role']):<{role_width}}  {str(item['relative']):<{path_width}}  "
            f"{int(item['bytes']):>10,}  {item['detail']}"
        )
    print(f"{'-' * role_width}  {'-' * path_width}  {'-' * 10}")
    print(
        f"{'TOTAL':<{role_width}}  {str(len(resources)) + ' files':<{path_width}}  {total:>10,}  "
        f"budget {args.budget:,} ({total / args.budget * 100:.1f}% used)"
    )
    print()
    print("Below-the-fold assets are excluded: loading=\"lazy\" images, tool clips and")
    print("runtime theme variants do not count toward the initial-document payload.")
    print()
    if over_budget:
        print(f"FAIL: {total:,} bytes exceeds the {args.budget:,} byte budget by {total - args.budget:,} bytes.")
        return 1
    print(f"PASS: {total:,} bytes is within the {args.budget:,} byte budget ({args.budget - total:,} bytes headroom).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
