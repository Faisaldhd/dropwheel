#!/usr/bin/env python3
"""Validate every internal and external link on the DropWheel static site.

Internal checks (always on, offline):

* every local ``href``/``src``/``srcset``/``poster`` resolves to an existing
  file below the site root;
* every fragment (``#id``) resolves to an ``id`` in the target HTML document.

External checks (HTTP HEAD with a ranged GET fallback):

* GitHub release/download URLs, the licence, SECURITY.md and FileAction are
  fetched from the network;
* redirects are followed, so GitHub's release-asset redirects are tolerated;
* 404 and 410 are hard failures;
* 401/403/429 and 5xx are reported as warnings (rate limits and transient
  upstream failures must not block a release), unless ``--strict`` is used;
* ``--offline`` skips external requests entirely, for local runs without a
  network.  CI always runs online.

Exit codes:
  0  every internal link resolves and no external URL is definitively dead
  1  at least one internal link is broken or an external URL returned 404/410
  2  the site root or entry document is unusable
"""

from __future__ import annotations

import argparse
import re
import ssl
import sys
import time
import urllib.error
import urllib.request
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit

SKIP_SCHEMES = {"mailto", "tel", "data", "javascript", "blob", "ftp"}
LINK_ATTRS = {
    "a": ("href",),
    "area": ("href",),
    "link": ("href",),
    "script": ("src",),
    "img": ("src",),
    "source": ("src", "srcset"),
    "video": ("src", "poster"),
    "audio": ("src",),
    "iframe": ("src",),
    "use": ("href",),
    "image": ("href",),
}
USER_AGENT = (
    "DropWheel-site-link-check/1.0 "
    "(+https://github.com/Faisaldhd/dropwheel; static-site CI)"
)
# The page is a template: release markers may sit inside filename text (and, in
# one historical case, inside an href).  The deploy step rewrites/removes them,
# so link checking resolves the URL as the stamped artifact will serve it.
RELEASE_MARKER_RE = re.compile(r"<!--release-[a-z0-9-]+:(?:start|end)-->")


def normalize_url(url: str) -> str:
    return RELEASE_MARKER_RE.sub("", url)


class _LinkParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.links: list[tuple[str, str, int]] = []
        self.ids: set[str] = set()

    def handle_starttag(self, tag: str, attrs_list: list[tuple[str, str | None]]) -> None:
        attrs = {key: (value or "") for key, value in attrs_list}
        line = self.getpos()[0]
        if attrs.get("id"):
            self.ids.add(attrs["id"])
        if tag == "a" and attrs.get("name"):
            self.ids.add(attrs["name"])
        for attribute in LINK_ATTRS.get(tag, ()):
            value = attrs.get(attribute)
            if not value:
                continue
            if attribute == "srcset":
                for candidate in value.split(","):
                    candidate = candidate.strip()
                    if candidate:
                        self.links.append((candidate.split()[0], f"{tag}[srcset]", line))
            else:
                self.links.append((value, f"{tag}[{attribute}]", line))


def _parse_html(path: Path) -> _LinkParser:
    parser = _LinkParser()
    parser.feed(path.read_text(encoding="utf-8"))
    parser.close()
    return parser


def _is_inside(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def _resolve_internal(root: Path, current: Path, url: str) -> tuple[Path | None, str]:
    parts = urlsplit(url)
    path = unquote(parts.path)
    if not path:
        target = current
    elif path.startswith("/"):
        target = root / path.lstrip("/")
    else:
        target = current.parent / path
    target = target.resolve()
    if not _is_inside(target, root):
        return None, f"escapes site root: {url}"
    if target.is_dir():
        target = target / "index.html"
    if not target.is_file():
        return None, f"missing file: {url}"
    return target, ""


class Checker:
    def __init__(self, root: Path, timeout: float, retries: int, strict: bool) -> None:
        self.root = root.resolve()
        self.timeout = timeout
        self.retries = retries
        self.strict = strict
        self.dead: list[str] = []
        self.warnings: list[str] = []
        self.internal_checked = 0
        self.fragments_checked = 0
        self.external_checked = 0
        self.external_skipped = 0
        self.markers_stripped = 0
        self._ids: dict[Path, set[str]] = {}
        self._external_cache: dict[str, tuple[str, str]] = {}

    def _ids_for(self, path: Path) -> set[str]:
        if path not in self._ids:
            self._ids[path] = _parse_html(path).ids
        return self._ids[path]

    def check_internal(self, current: Path, url: str, source: str, line: int) -> None:
        self.internal_checked += 1
        target, error = _resolve_internal(self.root, current, url)
        if target is None:
            self.dead.append(f"{current.name}:{line} {source} -> {url} ({error})")
            return
        fragment = unquote(urlsplit(url).fragment)
        if not fragment:
            return
        if target.suffix.lower() not in (".html", ".htm"):
            self.warnings.append(
                f"{current.name}:{line} {source} -> {url} (fragment on non-HTML target is not checked)"
            )
            return
        self.fragments_checked += 1
        if fragment not in self._ids_for(target):
            self.dead.append(
                f"{current.name}:{line} {source} -> {url} (no element with id={fragment!r})"
            )

    def check_external(self, url: str, source: str, line: int) -> None:
        if url in self._external_cache:
            status, note = self._external_cache[url]
            if status == "dead":
                self.dead.append(f"external {url} -> {note}")
            elif status == "warn":
                self.warnings.append(f"external {url} -> {note}")
            else:
                self.external_checked += 1
            return
        self.external_checked += 1
        status, note = self._fetch(url)
        self._external_cache[url] = (status, note)
        if status == "dead":
            self.dead.append(f"external {url} -> {note} ({source} line {line})")
        elif status == "warn":
            self.warnings.append(f"external {url} -> {note} ({source} line {line})")

    def _fetch(self, url: str) -> tuple[str, str]:
        headers = {"User-Agent": USER_AGENT, "Accept": "*/*"}
        context = ssl.create_default_context()
        last_note = "unknown error"
        for attempt in range(self.retries + 1):
            if attempt:
                time.sleep(min(2**attempt, 8))
            for method in ("HEAD", "GET"):
                request = urllib.request.Request(url, method=method, headers=headers)
                if method == "GET":
                    request.add_header("Range", "bytes=0-1")
                try:
                    with urllib.request.urlopen(request, timeout=self.timeout, context=context) as response:
                        code = response.status
                        if 200 <= code < 400:
                            return "ok", f"HTTP {code}"
                        last_note = f"HTTP {code}"
                except urllib.error.HTTPError as exc:
                    code = exc.code
                    last_note = f"HTTP {code}"
                    if code in (404, 410):
                        return "dead", last_note
                    if code in (405, 501) and method == "HEAD":
                        # Server does not implement HEAD; try the ranged GET.
                        continue
                    if code in (401, 403, 429):
                        # Rate limits and bot protection are not proof of a dead link.
                        if attempt >= self.retries:
                            return "warn", last_note + " (rate limit/forbidden; not a 404)"
                        break
                    if 400 <= code < 500:
                        return "dead", last_note
                    if 500 <= code < 600:
                        if attempt >= self.retries:
                            return "warn", last_note + " (upstream server error)"
                        break
                    return "warn", last_note
                except (urllib.error.URLError, TimeoutError, ssl.SSLError, OSError) as exc:
                    last_note = f"network error: {getattr(exc, 'reason', exc)}"
                    if attempt >= self.retries:
                        return "warn", last_note
                    break
        return "warn", last_note


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default="docs/site", help="site root (default: docs/site)")
    parser.add_argument("--index", default=None, help="entry document (default: <root>/index.html)")
    parser.add_argument("--offline", action="store_true", help="skip external HTTP checks (local runs only)")
    parser.add_argument("--timeout", type=float, default=20.0, help="per-request timeout in seconds (default: 20)")
    parser.add_argument("--retries", type=int, default=2, help="retries for transient failures (default: 2)")
    parser.add_argument("--strict", action="store_true", help="treat rate-limit/5xx warnings as failures")
    args = parser.parse_args(argv)

    root = Path(args.root)
    index = Path(args.index) if args.index else root / "index.html"
    if not root.is_dir():
        print(f"ERROR: site root not found: {root}", file=sys.stderr)
        return 2
    if not index.is_file():
        print(f"ERROR: entry document not found: {index}", file=sys.stderr)
        return 2

    html_files = sorted(root.rglob("*.html"))
    checker = Checker(root, args.timeout, max(0, args.retries), args.strict)
    external_seen: set[str] = set()

    for html_file in html_files:
        parser_result = _parse_html(html_file)
        for url, source, line in parser_result.links:
            normalized = normalize_url(url)
            if normalized != url:
                checker.markers_stripped += 1
            url = normalized
            if not url or url.startswith("//"):
                if url.startswith("//") and not args.offline:
                    if url not in external_seen:
                        external_seen.add(url)
                        checker.check_external("https:" + url, source, line)
                    else:
                        checker.external_checked += 1
                continue
            parts = urlsplit(url)
            if parts.scheme in ("http", "https"):
                if args.offline:
                    checker.external_skipped += 1
                    continue
                if url not in external_seen:
                    external_seen.add(url)
                    checker.check_external(url, source, line)
                else:
                    checker.external_checked += 1
                continue
            if parts.scheme:
                continue
            checker.check_internal(html_file, url, source, line)

    print(f"Site root       : {root.resolve()}")
    print(f"HTML documents  : {len(html_files)}")
    if checker.markers_stripped:
        print(
            f"Marker tokens   : {checker.markers_stripped} URL(s) resolved after stripping deploy-stamp markers"
        )
    print(f"Internal links  : {checker.internal_checked} checked, {checker.fragments_checked} fragments")
    print(
        "External links  : "
        + (
            f"{checker.external_skipped} skipped (--offline)"
            if args.offline
            else f"{checker.external_checked} checked, {len(external_seen)} unique URLs"
        )
    )
    if checker.warnings:
        print(f"Warnings        : {len(checker.warnings)}")
        for warning in checker.warnings:
            print(f"  WARN {warning}")
    if checker.dead:
        print(f"Dead links      : {len(checker.dead)}", file=sys.stderr)
        for dead in checker.dead:
            print(f"  DEAD {dead}", file=sys.stderr)
        print("FAIL: broken links found.", file=sys.stderr)
        return 1

    if args.strict and checker.warnings:
        print("FAIL: --strict treats warnings as failures.", file=sys.stderr)
        return 1

    print(f"PASS: no dead links ({checker.internal_checked} internal, {len(external_seen)} external).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
