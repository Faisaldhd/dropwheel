#!/usr/bin/env python3
"""Static QA gate for the DropWheel product site (docs/site).

Standard library only. No network access. No browser. It parses the authored
HTML and CSS, validates the checks agreed for the 2.4.0 website lane, measures
the first-load byte budget, and exits non-zero if any FAIL is found.

Usage:
    python scripts/site/qa/qa_check.py --site docs/site
    python scripts/site/qa/qa_check.py --site docs/site --verbose
    python scripts/site/qa/qa_check.py --site docs/site --json

First-load definition (deterministic, documented):
    index.html
  + the stylesheet
  + every external script (boot.js, app.js)
  + the worst-case primary hero image (the largest file referenced by the hero
    <picture>: img src, <source> srcset, data-clip-*/data-poster-*)
  + the header brand mark
  + the favicon
Only subresources a first paint can fetch are counted. Lazy images below the
fold, social metadata images, and the rest of the tree are reported but not
charged to the 500,000-byte budget.

Exit codes: 0 = all checks PASS, 1 = at least one FAIL, 2 = usage/setup error.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from html.parser import HTMLParser
from urllib.parse import urlsplit

BUDGET_BYTES = 500_000
MIN_WIDTH_LIMIT_PX = 360
LONG_TEXT_LIMIT = 40
VOID_TAGS = {
    "area", "base", "br", "col", "embed", "hr", "img", "input", "link",
    "meta", "param", "source", "track", "wbr",
}

ARABIC_RE = re.compile(r"[\u0600-\u06FF\u0750-\u077F\u08A0-\u08FF\uFB50-\uFDFF\uFE70-\uFEFF]")
LATIN_TOKEN_RE = re.compile(r"[A-Za-z][A-Za-z0-9.+#_/-]*")
HEX_LONG_RE = re.compile(r"\S{40,}")

# Technical/brand tokens that are legitimately kept in Latin script inside the
# Arabic half of the bilingual page. Anything else that looks like English
# prose is reported by LANG-3.
TECH_ALLOW = {
    "PDF", "ZIP", "PNG", "JPG", "WEBP", "ICO", "AVIF", "TIFF", "MP4", "MOV",
    "MKV", "WEBM", "GIF", "MP3", "M4A", "WAV", "FLAC", "OGG", "OPUS", "AIFF",
    "DOCX", "ODT", "MD", "HTML", "TXT", "EPUB", "RTF", "PPTX", "ODP", "XLSX",
    "CSV", "TSV", "ODS", "TAR", "AES-256", "AES-256-GCM", "SHA-256", "SHA256SUMS.txt",
    "ECDSA", "P-256", "QR", "EXIF", "IPTC", "XMP", "EBU", "R128", "MIT", "CI",
    "DropWheel", "GitHub", "Windows", "FFmpeg", "Pandoc", "SmartScreen",
    "Authenticode", "FileAction", "Metamorfolog", "Tangerine", "Programs",
    "ProgramFiles", "NET", "PATH", "LOCALAPPDATA", "http", "https", "sig",
    "Ctrl", "Alt", "Space", "Enter", "v", "dwenc", "dwdir",
}

ALLOWED_LINK_PREFIXES = (
    ("github.com", "/Faisaldhd/dropwheel"),
    ("github.com", "/Metamorfolog/file_action"),
    ("opensource.org", ""),
)

TRACKER_PATTERNS = re.compile(
    r"google-analytics|googletagmanager|doubleclick|facebook\.net|connect\.facebook|"
    r"hotjar|segment\.(?:io|com)|mixpanel|plausible|umami|matomo|piwik|_paq|"
    r"sentry\.(?:io|com)|cloudflareinsights|clarity\.ms|bing\.com/bat|"
    r"googlesyndication|adservice\.google|analytics\.js",
    re.IGNORECASE,
)
PLACEHOLDER_RE = re.compile(r"\b(?:TODO|FIXME|TBD|XXX)\b|lorem ipsum", re.IGNORECASE)


class Node:
    __slots__ = ("tag", "attrs", "line", "parent", "children", "text")

    def __init__(self, tag=None, attrs=None, line=0, parent=None):
        self.tag = tag
        self.attrs = attrs or {}
        self.line = line
        self.parent = parent
        self.children = []
        self.text = None


class PageParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = Node("#document")
        self.stack = [self.root]
        self.nodes = []
        self.comments = []

    def handle_starttag(self, tag, attrs):
        node = Node(tag, {k.lower(): (v if v is not None else "") for k, v in attrs},
                    self.getpos()[0], self.stack[-1])
        self.stack[-1].children.append(node)
        self.nodes.append(node)
        if tag not in VOID_TAGS:
            self.stack.append(node)

    def handle_startendtag(self, tag, attrs):
        node = Node(tag, {k.lower(): (v if v is not None else "") for k, v in attrs},
                    self.getpos()[0], self.stack[-1])
        self.stack[-1].children.append(node)
        self.nodes.append(node)

    def handle_endtag(self, tag):
        if tag in VOID_TAGS:
            return
        for i in range(len(self.stack) - 1, 0, -1):
            if self.stack[i].tag == tag:
                del self.stack[i:]
                return

    def handle_data(self, data):
        node = Node(None, None, self.getpos()[0], self.stack[-1])
        node.text = data
        self.stack[-1].children.append(node)
        self.nodes.append(node)

    def handle_comment(self, data):
        self.comments.append((self.getpos()[0], data))


def element_text(node):
    if node.text is not None:
        return node.text
    return "".join(element_text(c) for c in node.children)


def descendants(node):
    for child in node.children:
        if child.tag:
            yield child
            yield from descendants(child)


def has_ancestor_tag(node, tag):
    parent = node.parent
    while parent is not None:
        if parent.tag == tag:
            return True
        parent = parent.parent
    return False


def nearest_lang(node):
    parent = node
    while parent is not None:
        if parent.attrs and "lang" in parent.attrs:
            return parent.attrs["lang"].strip().lower()
        parent = parent.parent
    return None


def class_tokens(node):
    return (node.attrs.get("class") or "").split()


def parse_srcset(value):
    """Return the URL part of a srcset. The site has no comma-containing URLs."""
    out = []
    for candidate in (value or "").split(","):
        candidate = candidate.strip()
        if candidate:
            out.append(candidate.split()[0])
    return out


class CssRule:
    __slots__ = ("selector", "decls", "line", "media")

    def __init__(self, selector, decls, line, media):
        self.selector = selector
        self.decls = decls
        self.line = line
        self.media = media


def css_strip_comments(text):
    out = []
    i = 0
    in_comment = False
    while i < len(text):
        if not in_comment and text.startswith("/*", i):
            in_comment = True
            out.extend((" ", " "))
            i += 2
        elif in_comment and text.startswith("*/", i):
            in_comment = False
            out.extend((" ", " "))
            i += 2
        elif in_comment:
            out.append("\n" if text[i] == "\n" else " ")
            i += 1
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def _find_matching_brace(text, start):
    depth = 0
    i = start
    quote = None
    while i < len(text):
        ch = text[i]
        if quote:
            if ch == "\\":
                i += 2
                continue
            if ch == quote:
                quote = None
        elif ch in "\"'":
            quote = ch
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def css_parse(text):
    """Parse rules and @media block rules. Comments must be stripped first."""
    rules = []
    text = css_strip_comments(text)

    def line_at(pos):
        return text.count("\n", 0, pos) + 1

    def parse_range(start, end, media):
        i = start
        while i < end:
            while i < end and text[i].isspace():
                i += 1
            if i >= end:
                return
            if text[i] == "@":
                at_start = i
                while i < end and text[i] not in "{;":
                    i += 1
                if i >= end:
                    return
                if text[i] == ";":
                    i += 1
                    continue
                close = _find_matching_brace(text, i)
                if close < 0:
                    return
                head = " ".join(text[at_start:i].split())
                if head.lower().startswith("@media"):
                    parse_range(i + 1, close, media + [head[6:].strip()])
                i = close + 1
                continue
            sel_start = i
            while i < end and text[i] not in "{};":
                i += 1
            if i >= end:
                return
            if text[i] in ";}":
                i += 1
                continue
            close = _find_matching_brace(text, i)
            if close < 0:
                return
            selector = " ".join(text[sel_start:i].split())
            body = text[i + 1:close]
            decls = {}
            for chunk in body.split(";"):
                if ":" not in chunk:
                    continue
                name, value = chunk.split(":", 1)
                name = name.strip().lower()
                if name:
                    decls[name] = " ".join(value.split()).lower()
            if selector:
                rules.append(CssRule(selector, decls, line_at(sel_start), media))
            i = close + 1

    parse_range(0, len(text), [])
    return rules


def selector_last_compound(selector):
    compound = re.split(r"[\s>+~]+", selector.strip())[-1]
    compound = re.sub(r"::?[a-zA-Z-]+(?:\([^)]*\))?", "", compound)
    return compound


def selector_matches(node, selector):
    """A deliberately small matcher: last compound only, tag/.class/#id."""
    compound = selector_last_compound(selector)
    if not compound or compound == "*":
        return True
    tag = None
    classes = []
    ident = None
    token = ""
    i = 0
    while i < len(compound):
        ch = compound[i]
        if ch in ".#":
            if token:
                tag = token
                token = ""
            j = i + 1
            while j < len(compound) and (compound[j].isalnum() or compound[j] in "-_"):
                j += 1
            value = compound[i + 1:j]
            if ch == ".":
                classes.append(value)
            else:
                ident = value
            i = j
        else:
            token += ch
            i += 1
    if token:
        tag = token
    if tag and node.tag != tag.lower():
        return False
    if ident and node.attrs.get("id") != ident:
        return False
    node_classes = set(class_tokens(node))
    return all(c in node_classes for c in classes)


def max_visible_text_len(node):
    scoped = [n for n in descendants(node) if n.attrs.get("data-lang") in ("en", "ar")]
    if scoped:
        lengths = []
        for lang in ("en", "ar"):
            texts = [element_text(n).strip() for n in scoped if n.attrs.get("data-lang") == lang]
            if texts:
                lengths.append(max(len(t) for t in texts))
        return max(lengths) if lengths else 0
    return len(element_text(node).strip())


def wrap_handled(node, rules):
    parent = node
    while parent is not None:
        if parent.tag:
            for rule in rules:
                if selector_matches(parent, rule.selector):
                    d = rule.decls
                    if d.get("overflow-wrap") in ("anywhere", "break-word"):
                        return True
                    if d.get("word-break") in ("break-all", "break-word"):
                        return True
                    if d.get("white-space") in ("pre-wrap", "pre-line", "break-spaces"):
                        return True
                    if d.get("overflow-x") in ("auto", "scroll"):
                        return True
        parent = parent.parent
    return False


class Check:
    def __init__(self, cid, title):
        self.cid = cid
        self.title = title
        self.ok = True
        self.details = []
        self.evidence = ""

    def fail(self, message):
        self.ok = False
        self.details.append(message)

    def set_evidence(self, message):
        self.evidence = message


class Report:
    def __init__(self):
        self.checks = []
        self.measurements = {}

    def add(self, check):
        self.checks.append(check)
        return check

    def failed(self):
        return [c for c in self.checks if not c.ok]


def size(path):
    try:
        return os.path.getsize(path)
    except OSError:
        return None


def rel(path, cwd):
    return os.path.relpath(path, cwd).replace(os.sep, "/")


def collect_resources(page, css_refs):
    refs = []
    fetch_link_rels = {
        "stylesheet", "icon", "apple-touch-icon", "preload", "prefetch",
        "modulepreload", "manifest", "mask-icon",
    }
    for node in page.nodes:
        if node.tag == "script" and node.attrs.get("src"):
            refs.append((node.attrs["src"], node.line, "script src"))
        if node.tag in ("img", "iframe", "video", "audio", "source", "embed", "object"):
            if node.attrs.get("src"):
                refs.append((node.attrs["src"], node.line, node.tag + " src"))
            if node.attrs.get("poster"):
                refs.append((node.attrs["poster"], node.line, "video poster"))
            if node.attrs.get("srcset"):
                for url in parse_srcset(node.attrs["srcset"]):
                    refs.append((url, node.line, node.tag + " srcset"))
        # Theme/clip alternatives can sit on <img>, <picture> or a play <button>.
        for attr in ("data-dark", "data-light", "data-clip-dark", "data-clip-light",
                     "data-poster-dark", "data-poster-light"):
            value = node.attrs.get(attr)
            if value and re.search(r"\.(?:webp|png|jpe?g|svg|gif|avif)$", value, re.I):
                refs.append((value, node.line, node.tag + " " + attr))
        if node.tag == "input" and node.attrs.get("type") == "image" and node.attrs.get("src"):
            refs.append((node.attrs["src"], node.line, "input src"))
        if node.tag == "link":
            rels = set((node.attrs.get("rel") or "").lower().split())
            if rels & fetch_link_rels and node.attrs.get("href"):
                refs.append((node.attrs["href"], node.line, "link " + "/".join(sorted(rels))))
    for url, line in css_refs:
        refs.append((url, line, "css url()"))
    return refs


def is_external(url):
    value = url.strip()
    return bool(re.match(r"^(?:https?:)?//", value, re.IGNORECASE))


def main(argv=None):
    parser = argparse.ArgumentParser(description="Static QA gate for docs/site")
    parser.add_argument("--site", default="docs/site", help="site directory (default: docs/site)")
    parser.add_argument("--verbose", action="store_true", help="print evidence for every check")
    parser.add_argument("--json", action="store_true", help="print a machine-readable summary")
    args = parser.parse_args(argv)

    cwd = os.getcwd()
    site = os.path.abspath(args.site)
    index_path = os.path.join(site, "index.html")
    if not os.path.isfile(index_path):
        print("qa_check: no index.html under " + site, file=sys.stderr)
        return 2
    with open(index_path, encoding="utf-8") as handle:
        html = handle.read()

    page = PageParser()
    page.feed(html)

    # ---------------------------------------------------------------- helpers
    report = Report()
    all_nodes = [n for n in page.nodes if n.tag]
    ids = {}
    for node in all_nodes:
        if "id" in node.attrs and node.attrs["id"].strip():
            ids.setdefault(node.attrs["id"], []).append(node)

    css_path = None
    for node in all_nodes:
        if node.tag == "link" and "stylesheet" in (node.attrs.get("rel") or "").lower().split():
            css_path = os.path.join(site, node.attrs.get("href", "").split("?")[0])
            break
    css_text = ""
    css_rules = []
    css_refs = []
    if css_path and os.path.isfile(css_path):
        with open(css_path, encoding="utf-8") as handle:
            css_text = handle.read()
        css_rules = css_parse(css_text)
        for match in re.finditer(r"url\(\s*(['\"]?)([^'\")]+)\1\s*\)", css_strip_comments(css_text)):
            css_refs.append((match.group(2), css_text.count("\n", 0, match.start()) + 1))
        if "@import" in css_strip_comments(css_text):
            css_refs.append(("@import", 1))

    resources = collect_resources(page, css_refs)
    external_resources = [r for r in resources if is_external(r[0])]

    # ---------------------------------------------------------------- EXT
    check = report.add(Check("EXT-1", "No external subresource references"))
    if external_resources:
        for url, line, where in external_resources:
            check.fail(f"{rel(index_path, cwd)}:{line}: {where} -> {url}")
    check.set_evidence(f"{len(resources)} local/remote resource references; {len(external_resources)} external")

    check = report.add(Check("EXT-2", "No inline event handlers"))
    handlers = [(n, a) for n in all_nodes for a in n.attrs if a.startswith("on")]
    for node, attr in handlers:
        check.fail(f"{rel(index_path, cwd)}:{node.line}: <{node.tag} {attr}=...>")
    check.set_evidence(f"{len(handlers)} inline handler attributes")

    check = report.add(Check("EXT-3", "No inline <script> without src"))
    # A JSON-LD data block is not executable JavaScript: <script type="application/ld+json">
    # is inert under script-src and carries structured data. Every other inline script
    # (no type, type="module", type="application/json", ...) is still a failure.
    json_ld = [n for n in all_nodes
               if n.tag == "script" and not n.attrs.get("src")
               and (n.attrs.get("type") or "").strip().lower() == "application/ld+json"]
    inline_scripts = [n for n in all_nodes
                      if n.tag == "script" and not n.attrs.get("src")
                      and element_text(n).strip() and n not in json_ld]
    for node in inline_scripts:
        check.fail(f"{rel(index_path, cwd)}:{node.line}: executable inline script "
                   f"(type={(node.attrs.get('type') or 'none')!r})")
    check.set_evidence(f"{len(inline_scripts)} executable inline script blocks; "
                       f"{len(json_ld)} JSON-LD data block(s) skipped as non-executable")

    check = report.add(Check("EXT-4", "No analytics/tracker identifiers"))
    tracker_hits = []
    for node in all_nodes:
        if node.tag == "script" and node.attrs.get("src"):
            if TRACKER_PATTERNS.search(node.attrs["src"]):
                tracker_hits.append((node.line, node.attrs["src"]))
    for url, line, where in resources:
        if TRACKER_PATTERNS.search(url):
            tracker_hits.append((line, url))
    for node in all_nodes:
        if node.tag == "script" and not node.attrs.get("src"):
            code = element_text(node)
            if TRACKER_PATTERNS.search(code):
                tracker_hits.append((node.line, "inline script token"))
    for path, label in ((css_path, "CSS"),):
        if path and os.path.isfile(path):
            text = css_strip_comments(open(path, encoding="utf-8").read())
            if TRACKER_PATTERNS.search(text):
                tracker_hits.append((text.count("\n", 0, TRACKER_PATTERNS.search(text).start()) + 1, label + " token"))
    js_paths = [os.path.join(site, n.attrs["src"].split("?")[0]) for n in all_nodes
                if n.tag == "script" and n.attrs.get("src") and not is_external(n.attrs["src"])]
    for path in js_paths:
        if os.path.isfile(path):
            text = open(path, encoding="utf-8").read()
            text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
            text = re.sub(r"^\s*//.*$", " ", text, flags=re.M)
            match = TRACKER_PATTERNS.search(text)
            if match:
                tracker_hits.append((text.count("\n", 0, match.start()) + 1, rel(path, cwd)))
    if tracker_hits:
        for line, where in tracker_hits:
            check.fail(f"{rel(index_path, cwd)}:{line}: {where}")
    check.set_evidence(f"{len(tracker_hits)} tracker identifiers in scripts/URLs/styles")

    # ---------------------------------------------------------------- CSP
    csp_nodes = [n for n in all_nodes
                 if n.tag == "meta" and n.attrs.get("http-equiv", "").lower() == "content-security-policy"]
    check = report.add(Check("CSP-1", "CSP meta present in <head>"))
    if len(csp_nodes) != 1:
        check.fail(f"{rel(index_path, cwd)}: expected exactly 1 CSP meta, found {len(csp_nodes)}")
    else:
        csp_node = csp_nodes[0]
        first_resource = min([n.line for n in all_nodes
                              if (n.tag == "script" and n.attrs.get("src")) or
                                 (n.tag == "link" and "stylesheet" in (n.attrs.get("rel") or "").lower().split())] or [10**9])
        if csp_node.line > first_resource:
            check.fail(f"{rel(index_path, cwd)}:{csp_node.line}: CSP meta appears after the first subresource")
        check.set_evidence(f"CSP at {rel(index_path, cwd)}:{csp_node.line}")

    check = report.add(Check("CSP-2", "CSP is restrictive"))
    directives = {}
    csp_node = csp_nodes[0] if csp_nodes else None
    if csp_node:
        for part in csp_node.attrs.get("content", "").split(";"):
            bits = part.strip().split()
            if bits:
                directives[bits[0].lower()] = [b.strip().lower() for b in bits[1:]]
        required = {
            "default-src": {"'self'", "'none'"},
            "script-src": {"'self'"},
            "style-src": {"'self'"},
            "img-src": {"'self'"},
            "object-src": {"'none'"},
            "base-uri": {"'none'"},
            "connect-src": {"'none'"},
        }
        for name, allowed in required.items():
            values = directives.get(name)
            if not values:
                check.fail(f"{rel(index_path, cwd)}:{csp_node.line}: missing {name}")
            elif not any(v in allowed for v in values):
                check.fail(f"{rel(index_path, cwd)}:{csp_node.line}: {name} is {' '.join(values)!r}, expected {sorted(allowed)}")
        for name in ("script-src", "style-src", "default-src", "img-src"):
            for value in directives.get(name, []):
                if value in ("*", "http:", "https:", "data:") or "unsafe-inline" in value or "unsafe-eval" in value:
                    check.fail(f"{rel(index_path, cwd)}:{csp_node.line}: {name} allows {value}")
        check.set_evidence("; ".join(f"{k}={' '.join(v)}" for k, v in directives.items()))

    check = report.add(Check("CSP-3", "style-src 'self' can allow the page as written"))
    style_attrs = [n for n in all_nodes if "style" in n.attrs]
    style_blocks = [n for n in all_nodes if n.tag == "style" and element_text(n).strip()]
    for node in style_attrs:
        check.fail(f"{rel(index_path, cwd)}:{node.line}: inline style attribute on <{node.tag}>")
    for node in style_blocks:
        check.fail(f"{rel(index_path, cwd)}:{node.line}: inline <style> block")
    js_style_writes = []
    for path in js_paths:
        if os.path.isfile(path):
            code = open(path, encoding="utf-8").read()
            if re.search(r"setAttribute\(\s*['\"]style['\"]|\.style\.[A-Za-z]", code):
                js_style_writes.append(rel(path, cwd))
    check.set_evidence(f"{len(style_attrs)} style attrs, {len(style_blocks)} style blocks, "
                       f"{len(js_style_writes)} JS style writes")

    # ---------------------------------------------------------------- ids / anchors
    check = report.add(Check("ID-1", "IDs are unique"))
    duplicates = {k: v for k, v in ids.items() if len(v) > 1}
    if duplicates:
        for name, nodes in sorted(duplicates.items()):
            check.fail(f"{rel(index_path, cwd)}:{nodes[0].line}: duplicate id {name!r} ({len(nodes)} uses)")
    check.set_evidence(f"{len(ids)} unique ids")

    check = report.add(Check("ID-2", "Every internal anchor target exists"))
    internal = 0
    for node in all_nodes:
        href = node.attrs.get("href", "")
        if href.startswith("#"):
            internal += 1
            target = href[1:]
            if not target:
                check.fail(f"{rel(index_path, cwd)}:{node.line}: empty fragment href")
            elif target not in ids:
                check.fail(f"{rel(index_path, cwd)}:{node.line}: href={href!r} has no target")
    check.set_evidence(f"{internal} internal href/use references")

    check = report.add(Check("ID-3", "aria-labelledby/describedby/controls and for targets exist"))
    aria_count = 0
    for node in all_nodes:
        for attr in ("aria-labelledby", "aria-describedby", "aria-controls", "for"):
            value = node.attrs.get(attr)
            if not value:
                continue
            for token in value.split():
                aria_count += 1
                if token not in ids:
                    check.fail(f"{rel(index_path, cwd)}:{node.line}: {attr}={token!r} has no target")
    check.set_evidence(f"{aria_count} ARIA/for references")

    # ---------------------------------------------------------------- images / media
    check = report.add(Check("IMG-1", "Every <img> has alt (or aria-hidden), width and height"))
    images = [n for n in all_nodes if n.tag == "img"]
    for node in images:
        missing = []
        if "alt" not in node.attrs and node.attrs.get("aria-hidden") != "true":
            missing.append("alt")
        for dim in ("width", "height"):
            if not re.fullmatch(r"\d+", node.attrs.get(dim, "")) or int(node.attrs.get(dim, "0")) <= 0:
                missing.append(dim)
        if missing:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: <img> missing {', '.join(missing)}")
    check.set_evidence(f"{len(images)} images")

    check = report.add(Check("MEDIA-1", "Video/source elements carry dimensions and a poster"))
    videos = [n for n in all_nodes if n.tag == "video"]
    sources = [n for n in all_nodes if n.tag == "source"]
    for node in videos:
        for attr in ("width", "height", "poster"):
            if not node.attrs.get(attr):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: <video> missing {attr}")
    for node in sources:
        parent = node.parent
        while parent is not None and parent.tag not in ("video", "audio", "picture"):
            parent = parent.parent
        if parent is None:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: <source> outside video/audio/picture")
        elif parent.tag in ("video", "audio"):
            for attr in ("width", "height"):
                if not parent.attrs.get(attr):
                    check.fail(f"{rel(index_path, cwd)}:{node.line}: media <source> parent lacks {attr}")
            if parent.tag == "video" and not parent.attrs.get("poster"):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: video <source> parent lacks poster")
        else:
            if not node.attrs.get("srcset"):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: picture <source> lacks srcset")
            if not node.attrs.get("type"):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: picture <source> lacks type")
            if not any(n.tag == "img" for n in descendants(parent)):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: <picture> has no fallback <img>")
        if node.attrs.get("src") and not node.attrs.get("srcset"):
            check.fail(f"{rel(index_path, cwd)}:{node.line}: <source> uses src instead of srcset")
    check.set_evidence(f"{len(videos)} video, {len(sources)} source elements")

    # ---------------------------------------------------------------- headings
    headings = []
    for node in all_nodes:
        if node.tag in ("h1", "h2", "h3", "h4", "h5", "h6") and not has_ancestor_tag(node, "svg"):
            headings.append(node)
    check = report.add(Check("HEAD-1", "Exactly one h1"))
    h1s = [n for n in headings if n.tag == "h1"]
    if len(h1s) != 1:
        for node in h1s:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: <h1> {element_text(node).strip()[:60]!r}")
        if not h1s:
            check.fail(f"{rel(index_path, cwd)}: no <h1>")
    check.set_evidence(f"{len(h1s)} h1, {len(headings)} headings")

    check = report.add(Check("HEAD-2", "Heading levels are not skipped"))
    previous = 0
    for node in headings:
        level = int(node.tag[1])
        if previous and level > previous + 1:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: <{node.tag}> after <h{previous}> "
                       f"({element_text(node).strip()[:50]!r})")
        previous = level
    check.set_evidence(f"{len(headings)} headings in document order")

    # ---------------------------------------------------------------- language
    html_nodes = [n for n in all_nodes if n.tag == "html"]
    check = report.add(Check("LANG-1", "html lang and dir are present"))
    if not html_nodes:
        check.fail(f"{rel(index_path, cwd)}: no <html> element")
    else:
        root = html_nodes[0]
        lang = (root.attrs.get("lang") or "").strip()
        direction = (root.attrs.get("dir") or "").strip().lower()
        if not lang:
            check.fail(f"{rel(index_path, cwd)}:{root.line}: <html> has no lang")
        if direction not in ("ltr", "rtl", "auto"):
            check.fail(f"{rel(index_path, cwd)}:{root.line}: <html dir={direction!r}>")
        check.set_evidence(f"lang={lang!r} dir={direction!r}")

    check = report.add(Check("LANG-2", "Arabic text is inside lang=\"ar\""))
    arabic_outside = []
    for node in page.nodes:
        if node.text is None or not ARABIC_RE.search(node.text):
            continue
        if has_ancestor_tag(node, "script") or has_ancestor_tag(node, "style"):
            continue
        if nearest_lang(node) != "ar":
            arabic_outside.append((node.line, node.text.strip()[:70], nearest_lang(node)))
    for line, text, scope in arabic_outside:
        check.fail(f"{rel(index_path, cwd)}:{line}: Arabic outside lang=ar (scope {scope!r}): {text!r}")
    check.set_evidence(f"{len(arabic_outside)} Arabic text nodes outside lang=ar")

    check = report.add(Check("LANG-3", "No Arabic in English blocks and no English prose in Arabic blocks"))
    arabic_in_en = []
    for node in all_nodes:
        if node.attrs.get("data-lang") == "en" and ARABIC_RE.search(element_text(node)):
            arabic_in_en.append((node.line, element_text(node).strip()[:70]))
    for line, text in arabic_in_en:
        check.fail(f"{rel(index_path, cwd)}:{line}: Arabic inside data-lang=en: {text!r}")
    latin_prose = []
    for node in all_nodes:
        if node.attrs.get("data-lang") != "ar":
            continue
        text = element_text(node)
        for match in LATIN_TOKEN_RE.finditer(text):
            token = match.group(0).rstrip(".,;:!?)]}")
            base = token
            if base in TECH_ALLOW:
                continue
            if re.fullmatch(r"[A-Z]{1,8}", base) and len(base) <= 8:
                continue
            if re.search(r"\d", base) and len(base) <= 24:
                continue
            if re.fullmatch(r"[A-Za-z0-9._/-]{1,40}", base) and (base.isupper() or "." in base or "/" in base or "-" in base):
                continue
            latin_prose.append((node.line, base))
    for line, token in latin_prose:
        check.fail(f"{rel(index_path, cwd)}:{line}: Latin token in data-lang=ar: {token!r}")
    check.set_evidence(f"{len(arabic_in_en)} Arabic-in-EN, {len(latin_prose)} non-technical Latin tokens in AR")

    check = report.add(Check("LANG-4", "Both languages cover the same sections"))
    sections = [n for n in all_nodes if n.tag == "section"]
    section_ids = [n.attrs.get("id") for n in sections if n.attrs.get("id")]
    if len(section_ids) != len(set(section_ids)):
        check.fail(f"{rel(index_path, cwd)}: duplicate section ids")
    for node in sections:
        members = list(descendants(node))
        en = sum(1 for m in members if m.attrs.get("data-lang") == "en")
        ar = sum(1 for m in members if m.attrs.get("data-lang") == "ar")
        label = node.attrs.get("id") or node.attrs.get("class", "section").split()[0]
        if en != ar:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: section {label!r} has {en} EN / {ar} AR text spans")
        if en == 0:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: section {label!r} has no localized text")
    check.set_evidence(f"{len(sections)} sections ({len(section_ids)} with ids), EN/AR span counts equal")

    # ---------------------------------------------------------------- CTAs / links
    verified_url = "https://github.com/Faisaldhd/dropwheel/releases/latest/download/DropWheel-Setup.exe"
    check = report.add(Check("CTA-1", "Download CTAs use the verified URL or an in-page anchor"))
    download_ctas = []
    button_ctas = []
    for node in all_nodes:
        if node.tag != "a":
            continue
        href = node.attrs.get("href", "")
        if "btn" in class_tokens(node):
            button_ctas.append(node)
            if not (href == verified_url or href.startswith("#")):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: button href is neither the verified URL nor an anchor: {href!r}")
            if href.startswith("#") and href[1:] not in ids:
                check.fail(f"{rel(index_path, cwd)}:{node.line}: button anchor {href!r} has no target")
        text = element_text(node)
        if "btn" in class_tokens(node) and href and ("Download" in text or "تنزيل" in text):
            download_ctas.append(node)
            if href != verified_url and not href.startswith("#"):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: download CTA does not use the verified URL: {href!r}")
            if href.startswith("#") and href[1:] not in ids:
                check.fail(f"{rel(index_path, cwd)}:{node.line}: download CTA anchor {href!r} has no target")
    en_ctas = sum(1 for n in download_ctas for d in descendants(n) if d.attrs.get("data-lang") == "en")
    ar_ctas = sum(1 for n in download_ctas for d in descendants(n) if d.attrs.get("data-lang") == "ar")
    if not download_ctas:
        check.fail(f"{rel(index_path, cwd)}: no download CTA found")
    if en_ctas != ar_ctas:
        check.fail(f"{rel(index_path, cwd)}: {en_ctas} EN download labels vs {ar_ctas} AR labels")
    check.set_evidence(f"{len(download_ctas)} download CTAs ({en_ctas} EN/{ar_ctas} AR), {len(button_ctas)} button CTAs")

    check = report.add(Check("LINK-1", "External anchors are on the allowlist"))
    external_links = []
    for node in all_nodes:
        if node.tag == "a" and is_external(node.attrs.get("href", "")):
            external_links.append(node)
            url = node.attrs["href"]
            parts = urlsplit(url)
            host = (parts.hostname or "").lower()
            path = parts.path or ""
            if not any(host == allowed_host and path.startswith(prefix)
                       for allowed_host, prefix in ALLOWED_LINK_PREFIXES):
                check.fail(f"{rel(index_path, cwd)}:{node.line}: not allowlisted: {url}")
    check.set_evidence(f"{len(external_links)} external anchors")

    check = report.add(Check("LINK-2", "External URLs are well-formed"))
    bad_urls = 0
    for node in external_links:
        url = node.attrs["href"]
        bad = None
        if "<!--" in url or "-->" in url:
            bad = "HTML comment marker inside URL"
        elif re.search(r"[\s<>\"`]", url):
            bad = "whitespace or angle/quote/backtick"
        else:
            parts = urlsplit(url)
            if parts.scheme not in ("http", "https") or not parts.netloc:
                bad = "not an absolute http(s) URL"
            elif parts.username or parts.password:
                bad = "URL contains credentials"
        if bad:
            bad_urls += 1
            check.fail(f"{rel(index_path, cwd)}:{node.line}: {bad}: {url}")
    check.set_evidence(f"{len(external_links) - bad_urls}/{len(external_links)} external URLs well-formed")

    check = report.add(Check("LINK-3", "target=_blank links carry rel=noopener"))
    blank_links = [n for n in all_nodes if n.tag == "a" and n.attrs.get("target", "").lower() == "_blank"]
    for node in blank_links:
        rels = set((node.attrs.get("rel") or "").lower().split())
        if "noopener" not in rels:
            check.fail(f"{rel(index_path, cwd)}:{node.line}: target=_blank without rel=noopener")
    check.set_evidence(f"{len(blank_links)} target=_blank links")

    # ---------------------------------------------------------------- text
    check = report.add(Check("TXT-1", "No TODO/FIXME/lorem/placeholder strings"))
    text_hits = []
    for node in page.nodes:
        if node.text is not None:
            if has_ancestor_tag(node, "script") or has_ancestor_tag(node, "style"):
                continue
            if PLACEHOLDER_RE.search(node.text) or re.search(r"\bplaceholder\b", node.text, re.I):
                text_hits.append((node.line, node.text.strip()[:70]))
        elif node.tag:
            for attr in ("alt", "title", "aria-label", "placeholder", "value", "content",
                         "data-alt-en", "data-alt-ar", "data-label-en", "data-label-ar"):
                value = node.attrs.get(attr)
                if value and (PLACEHOLDER_RE.search(value) or re.search(r"\bplaceholder\b", value, re.I)):
                    text_hits.append((node.line, f"{attr}={value[:60]!r}"))
    for line, text in text_hits:
        check.fail(f"{rel(index_path, cwd)}:{line}: {text!r}")
    check.set_evidence(f"{len(text_hits)} placeholder-like strings in visible text/attributes")

    # ---------------------------------------------------------------- assets / size
    check = report.add(Check("ASSET-1", "Every referenced local asset exists"))
    local_refs = []
    seen = set()
    for url, line, where in resources:
        if is_external(url) or url.startswith("data:") or url.startswith("#"):
            continue
        clean = url.split("?")[0].split("#")[0]
        if not clean:
            continue
        path = os.path.normpath(os.path.join(site, clean))
        if path in seen:
            continue
        seen.add(path)
        local_refs.append((clean, path, line, where))
    missing = []
    for clean, path, line, where in local_refs:
        if not os.path.isfile(path):
            missing.append((clean, line, where))
    for clean, line, where in missing:
        check.fail(f"{rel(index_path, cwd)}:{line}: missing {where} -> {clean}")
    bytes_total = sum(size(path) or 0 for _, path, _, _ in local_refs)
    check.set_evidence(f"{len(local_refs)} unique referenced files, {bytes_total} bytes, {len(missing)} missing")

    check = report.add(Check("SIZE-1", "First-load bytes are under 500,000"))
    base_parts = [("document", index_path)]
    for node in all_nodes:
        if node.tag == "link" and "stylesheet" in (node.attrs.get("rel") or "").lower().split() and node.attrs.get("href"):
            base_parts.append(("stylesheet", os.path.join(site, node.attrs["href"].split("?")[0])))
            break
    for node in all_nodes:
        if node.tag == "script" and node.attrs.get("src") and not is_external(node.attrs["src"]):
            base_parts.append(("script", os.path.join(site, node.attrs["src"].split("?")[0])))
    for node in all_nodes:
        if node.tag == "img" and "brand__mark" in class_tokens(node) and node.attrs.get("src"):
            base_parts.append(("header mark", os.path.join(site, node.attrs["src"].split("?")[0])))
            break
    for node in all_nodes:
        if node.tag == "link" and "icon" in (node.attrs.get("rel") or "").lower().split() and node.attrs.get("href"):
            base_parts.append(("favicon", os.path.join(site, node.attrs["href"].split("?")[0])))
            break

    hero_candidates = []
    hero = next((n for n in all_nodes
                 if n.tag == "img" and n.attrs.get("fetchpriority", "").lower() == "high"), None)
    if hero is None:
        hero = next((n for n in all_nodes if n.tag == "img"), None)
    if hero is not None:
        if hero.attrs.get("src"):
            hero_candidates.append(os.path.join(site, hero.attrs["src"].split("?")[0]))
        picture = hero.parent
        while picture is not None and picture.tag != "picture":
            picture = picture.parent
        if picture is not None:
            for url in re.findall(r"assets/[^\"'\s,]+", " ".join(picture.attrs.values())):
                hero_candidates.append(os.path.join(site, url))
            for node in descendants(picture):
                if node.tag == "source" and node.attrs.get("srcset"):
                    for url in parse_srcset(node.attrs["srcset"]):
                        if not is_external(url):
                            hero_candidates.append(os.path.join(site, url.split("?")[0]))
    hero_sizes = [(c, size(c)) for c in hero_candidates if os.path.isfile(c)]

    def dedupe(parts):
        out = []
        seen = set()
        for label, path in parts:
            key = os.path.normcase(os.path.abspath(path))
            if key in seen:
                continue
            seen.add(key)
            out.append((label, path))
        return out

    base_parts = dedupe(base_parts)
    base_total = sum(size(p) or 0 for _, p in base_parts)
    if hero_sizes:
        worst = max(hero_sizes, key=lambda item: item[1] or 0)
        best = min(hero_sizes, key=lambda item: item[1] or 0)
        first_parts = dedupe(base_parts + [("hero image (worst case)", worst[0])])
        reduced_total = base_total + (best[1] or 0)
    else:
        first_parts = base_parts
        reduced_total = None
    total = sum(size(p) or 0 for _, p in first_parts)
    if total > BUDGET_BYTES:
        check.fail(f"{rel(index_path, cwd)}: first load {total} B exceeds {BUDGET_BYTES} B")
    check.set_evidence(f"first load {total} B across {len(first_parts)} files"
                       + (f" (reduced-motion variant {reduced_total} B)"
                          if reduced_total is not None and reduced_total != total else ""))
    report.measurements["first_load_bytes"] = total
    report.measurements["first_load_reduced_motion_bytes"] = reduced_total
    report.measurements["first_load_files"] = [
        {"role": label, "path": rel(path, cwd), "bytes": size(path) or 0} for label, path in first_parts
    ]

    # ---------------------------------------------------------------- phone width
    check = report.add(Check("PHONE-1", "Viewport meta is present"))
    viewports = [n for n in all_nodes if n.tag == "meta" and (n.attrs.get("name") or "").lower() == "viewport"]
    if len(viewports) != 1:
        check.fail(f"{rel(index_path, cwd)}: expected 1 viewport meta, found {len(viewports)}")
    else:
        content = viewports[0].attrs.get("content", "")
        if "width=device-width" not in content.replace(" ", ""):
            check.fail(f"{rel(index_path, cwd)}:{viewports[0].line}: viewport lacks width=device-width")
        check.set_evidence(f"viewport at {rel(index_path, cwd)}:{viewports[0].line}: {content!r}")

    check = report.add(Check("PHONE-2", "No fixed min-width above 360px"))
    for rule in css_rules:
        value = rule.decls.get("min-width", "")
        match = re.fullmatch(r"(\d+(?:\.\d+)?)px", value)
        if match and float(match.group(1)) > MIN_WIDTH_LIMIT_PX:
            check.fail(f"{rel(css_path, cwd)}:{rule.line}: {rule.selector} min-width: {value}")
    check.set_evidence(f"{len(css_rules)} CSS rules scanned")

    check = report.add(Check("PHONE-3", "No 100vw width without a max-width"))
    for rule in css_rules:
        for prop in ("width", "inline-size"):
            if rule.decls.get(prop, "").endswith("100vw") and "max-width" not in rule.decls:
                check.fail(f"{rel(css_path, cwd)}:{rule.line}: {rule.selector} {prop}: 100vw")
    check.set_evidence("no unconstrained 100vw widths")

    check = report.add(Check("PHONE-4", "white-space: nowrap is only used on short or clipped text"))
    nowrap_rules = [r for r in css_rules if r.decls.get("white-space") == "nowrap"]
    for rule in nowrap_rules:
        candidates = [n for n in all_nodes if n.tag and selector_matches(n, rule.selector)]
        longest = 0
        for node in candidates:
            longest = max(longest, max_visible_text_len(node))
        clipped = rule.decls.get("overflow") in ("hidden", "clip", "auto", "scroll") or \
            rule.decls.get("text-overflow") == "ellipsis"
        if longest > LONG_TEXT_LIMIT and not clipped:
            check.fail(f"{rel(css_path, cwd)}:{rule.line}: {rule.selector} nowrap with {longest} chars and no overflow handling")
    check.set_evidence(f"{len(nowrap_rules)} nowrap rules checked")

    check = report.add(Check("PHONE-5", "Long unbreakable tokens have wrapping or scroll handling"))
    long_tokens = []
    for node in page.nodes:
        if node.text is None or len(node.text.strip()) < LONG_TEXT_LIMIT:
            continue
        if has_ancestor_tag(node, "script") or has_ancestor_tag(node, "style") or has_ancestor_tag(node, "svg"):
            continue
        for match in HEX_LONG_RE.finditer(node.text.strip()):
            token = match.group(0).strip(".,;:()[]{}<>")
            if len(token) < LONG_TEXT_LIMIT:
                continue
            container = node.parent
            if container is not None and wrap_handled(container, css_rules):
                continue
            long_tokens.append((node.line, token))
    for line, token in long_tokens:
        check.fail(f"{rel(index_path, cwd)}:{line}: {len(token)}-char unbreakable token without overflow-wrap/"
                   f"word-break/overflow-x: {token[:24]}...")
    check.set_evidence(f"{len(long_tokens)} unhandled long tokens")

    # ---------------------------------------------------------------- measurements
    html_bytes = size(index_path) or 0
    css_bytes = size(css_path) if css_path else 0
    js_files = [p for p in js_paths if os.path.isfile(p)]
    js_bytes = sum(size(p) or 0 for p in js_files)
    site_files = []
    for root, _, files in os.walk(site):
        for name in files:
            site_files.append(os.path.join(root, name))
    unique_asset_paths = {path for _, path, _, _ in local_refs if os.path.isfile(path)}
    report.measurements.update({
        "site": rel(site, cwd),
        "html_bytes": html_bytes,
        "css_bytes": css_bytes or 0,
        "js_bytes": js_bytes,
        "js_files": [rel(p, cwd) for p in js_files],
        "site_files": len(site_files),
        "site_total_bytes": sum(size(p) or 0 for p in site_files),
        "referenced_files": len(local_refs),
        "referenced_bytes": sum(size(p) or 0 for p in unique_asset_paths),
        "images": len(images),
        "external_requests": len(external_resources),
        "budget_bytes": BUDGET_BYTES,
    })

    # ---------------------------------------------------------------- output
    failures = report.failed()
    if args.json:
        payload = {
            "site": report.measurements["site"],
            "checks": [
                {"id": c.cid, "title": c.title, "status": "PASS" if c.ok else "FAIL",
                 "evidence": c.evidence, "details": c.details}
                for c in report.checks
            ],
            "measurements": report.measurements,
            "summary": {"pass": len(report.checks) - len(failures), "fail": len(failures),
                        "total": len(report.checks)},
            "result": "PASS" if not failures else "FAIL",
        }
        print(json.dumps(payload, indent=2, ensure_ascii=False))
    else:
        print("qa_check - static gate for " + rel(site, cwd))
        for check in report.checks:
            status = "PASS" if check.ok else "FAIL"
            print(f"{status}  {check.cid:<8} {check.title}")
            if check.evidence and args.verbose:
                print(f"      evidence: {check.evidence}")
            for detail in check.details:
                print(f"      {detail}")
        print("")
        count_pass = len(report.checks) - len(failures)
        print(f"SUMMARY  PASS {count_pass}  FAIL {len(failures)}  TOTAL {len(report.checks)}")
        print(f"MEASURED html={html_bytes}B css={report.measurements['css_bytes']}B "
              f"js={report.measurements['js_bytes']}B first_load={total}B "
              f"assets={len(unique_asset_paths)} referenced={len(local_refs)} "
              f"external_requests={len(external_resources)} "
              f"site_total={report.measurements['site_total_bytes']}B/{len(site_files)} files")
        if total > BUDGET_BYTES:
            print(f"BUDGET   FAIL {total}B > {BUDGET_BYTES}B")
        else:
            print(f"BUDGET   PASS {total}B <= {BUDGET_BYTES}B "
                  f"({total * 100.0 / BUDGET_BYTES:.1f}% used)")
        print("RESULT   " + ("PASS" if not failures else "FAIL"))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
