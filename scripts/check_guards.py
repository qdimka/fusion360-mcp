#!/usr/bin/env python3
"""P6 governance guards (docs/GOVERNANCE.md). Exit 0 = clean.

Checks:
  1. src/ style ratchet — for *.cs (bin/obj excluded): line <= 120 chars and
     a run of consecutive '//' comment lines <= 8 lines. Pre-existing debt
     is ratcheted in scripts/check_guards_baseline.json (per-file counts):
     NEW debt fails the gate; debt that shrank leaves a stale baseline entry
     (warning) — shrink it with:  python3 scripts/check_guards.py --regen
  2. docs/ link check — every .md/.py/.cs path referenced from docs/ must
     exist on disk:
       * markdown links [..](target)  -> resolved relative to the doc's dir
         (repo root as fallback), local targets only;
       * inline code spans that are PLAIN PATHS (no spaces, contain '/',
         end in .md/.py/.cs) -> resolved relative to the repo root.
     Skipped: URLs, fragments, tokens with '<' placeholders, ~/ paths,
     reference/ (untracked local clone) and dist/ (build output) prefixes,
     and lines that point at the external reference repos
     (Kompas3D-MCP / faust-machines) — those are not claims about this repo.
  3. docs/TOOLS.md sync — the generated listing must match what
     scripts/generate_tool_listing.py builds from
     src/Fusion360.Mcp.Core/Catalog/tools_catalog.json (drift -> exit 1).

Usage:
  python3 scripts/check_guards.py            # gate (exit 0 = clean)
  python3 scripts/check_guards.py --regen    # rewrite the style baseline
"""

from __future__ import annotations

import importlib.util
import json
import os
import pathlib
import re
import sys
from collections.abc import Iterable, Iterator

ROOT = pathlib.Path(__file__).resolve().parent.parent
BASELINE = ROOT / "scripts" / "check_guards_baseline.json"
MAX_LINE = 120
MAX_COMMENT_RUN = 8
PATH_EXTS = (".md", ".py", ".cs")
SKIP_TOKEN_PREFIXES = ("reference/", "dist/", "~/")
EXTERNAL_MARKERS = ("Kompas3D-MCP", "faust-machines")
MD_LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
CODE_SPAN_RE = re.compile(r"`([^`]+)`")
PLAIN_PATH_RE = re.compile(r"^[A-Za-z0-9_\-./]+$")


def iter_cs_files() -> Iterator[pathlib.Path]:
    for p in (ROOT / "src").rglob("*.cs"):
        rel = p.relative_to(ROOT)
        if "bin" in rel.parts or "obj" in rel.parts:
            continue
        yield p


def measure_style() -> dict[str, dict[str, int]]:
    """Per-file counts: long lines (>120) and long // comment runs (>8)."""
    stats: dict[str, dict[str, int]] = {"long_lines": {}, "comment_runs": {}}
    for p in iter_cs_files():
        text = p.read_text(encoding="utf-8", errors="replace")
        rel = str(p.relative_to(ROOT))
        long_lines = sum(1 for ln in text.splitlines() if len(ln) > MAX_LINE)
        runs, cur = 0, 0
        for ln in text.splitlines():
            if ln.strip().startswith("//"):
                cur += 1
            else:
                if cur > MAX_COMMENT_RUN:
                    runs += 1
                cur = 0
        if cur > MAX_COMMENT_RUN:  # run ending at EOF
            runs += 1
        stats["long_lines"][rel] = long_lines
        stats["comment_runs"][rel] = runs
    return stats


def load_baseline() -> dict | None:
    if not BASELINE.is_file():
        return None
    return json.loads(BASELINE.read_text(encoding="utf-8"))


def check_style(stats: dict, baseline: dict | None) -> list[str]:
    failures, warnings = [], []
    if baseline is None:
        return [f"missing {BASELINE.relative_to(ROOT)} — run: python3 scripts/check_guards.py --regen"]
    for kind in ("long_lines", "comment_runs"):
        current, base = stats[kind], baseline.get(kind, {})
        for rel in sorted(set(current) | set(base)):
            cur, b = current.get(rel, 0), base.get(rel, 0)
            if cur > b:
                what = ("line 120 chars" if kind == "long_lines"
                        else "comment run 8 lines")
                failures.append(f"{rel}: {kind} grew to {cur} (baseline {b}) — limit: {what}")
            elif b > 0 and cur < b:
                warnings.append(
                    f"stale baseline: {rel} {kind} {b} -> {cur} "
                    f"(run: python3 scripts/check_guards.py --regen)"
                )
    return failures + [f"WARN: {w}" for w in warnings]


def check_doc_links(paths: Iterable[pathlib.Path] | None = None) -> list[str]:
    failures = []
    if paths is None:
        docs = set(ROOT.glob("docs/**/*.md"))
        readme = ROOT / "README.md"
        if readme.is_file():
            docs.add(readme)
        for directory, dirs, files in os.walk(ROOT):
            dirs[:] = [name for name in dirs if name not in {
                ".git", ".superpowers", ".worktrees", "reference", "dist", "bin", "obj", "__pycache__"
            }]
            docs.update(pathlib.Path(directory) / name for name in files
                        if name in {"AGENTS.md", "CLAUDE.md", "GEMINI.md", "SKILL.md"})
        paths = sorted(docs)
    for doc in paths:
        doc = pathlib.Path(doc)
        for i, line in enumerate(doc.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            if any(m in line for m in EXTERNAL_MARKERS):
                continue
            targets: list[str] = []
            for link in MD_LINK_RE.findall(line):
                link = link.strip().strip("<>")
                if re.match(r"^[a-z][a-z0-9+.-]*://", link) or link.startswith(("#", "mailto:")):
                    continue
                if link.lower().endswith(PATH_EXTS):
                    targets.append(link)
            for span in CODE_SPAN_RE.findall(line):
                if PLAIN_PATH_RE.match(span) and "/" in span and span.lower().endswith(PATH_EXTS):
                    if span.startswith(SKIP_TOKEN_PREFIXES):
                        continue
                    targets.append(span)
            for target in targets:
                # doc-relative first, then repo-root relative (docs in subdirs
                # may name root paths like 'docs/E2E-STATUS.md').
                if not (doc.parent / target).is_file() and not (ROOT / target).is_file():
                    label = doc.relative_to(ROOT) if doc.is_relative_to(ROOT) else doc
                    failures.append(f"{label}:{i}: unresolvable link `{target}`")
    return failures


def check_tool_listing_sync() -> list[str]:
    """docs/TOOLS.md must equal the generator's output for the current catalog."""
    generator = ROOT / "scripts" / "generate_tool_listing.py"
    spec = importlib.util.spec_from_file_location("generate_tool_listing", generator)
    if spec is None or spec.loader is None:
        return ["cannot load scripts/generate_tool_listing.py — sync check skipped"]
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    catalog = json.loads(
        (ROOT / "src" / "Fusion360.Mcp.Core" / "Catalog" / "tools_catalog.json")
        .read_text(encoding="utf-8")
    )
    try:
        expected = module.build_markdown(catalog)
    except ValueError as exc:
        return [f"tool listing generation failed: {exc}"]
    doc = ROOT / "docs" / "TOOLS.md"
    if not doc.is_file():
        return ["missing docs/TOOLS.md — run: python3 scripts/generate_tool_listing.py"]
    if doc.read_text(encoding="utf-8") != expected:
        return ["docs/TOOLS.md out of sync with tools_catalog.json — "
                "run: python3 scripts/generate_tool_listing.py"]
    return []


def regen() -> None:
    stats = measure_style()
    data = {
        "note": (
            "P6 style-debt ratchet for src/*.cs (docs/GOVERNANCE.md). Counts may only "
            "shrink; update with: python3 scripts/check_guards.py --regen."
        ),
        "long_lines": {k: v for k, v in sorted(stats["long_lines"].items()) if v > 0},
        "comment_runs": {k: v for k, v in sorted(stats["comment_runs"].items()) if v > 0},
    }
    BASELINE.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    print(f"baseline regenerated: {sum(data['long_lines'].values())} long lines, "
          f"{sum(data['comment_runs'].values())} long comment runs "
          f"({len(data['long_lines'])} + {len(data['comment_runs'])} files)")


def main() -> int:
    if "--regen" in sys.argv[1:]:
        regen()
        return 0
    problems = (check_style(measure_style(), load_baseline())
                + check_doc_links()
                + check_tool_listing_sync())
    for p in problems:
        print(("FAIL" if not p.startswith("WARN") else "ok  ") + f" {p}")
    if any(not p.startswith("WARN") for p in problems):
        print(f"GUARDS FAILED — {sum(1 for p in problems if not p.startswith('WARN'))} problem(s)")
        return 1
    print("GUARDS OK — style ratchet (baseline) + docs/ link check "
          "+ docs/TOOLS.md <-> catalog sync clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
