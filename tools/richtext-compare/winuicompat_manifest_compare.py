#!/usr/bin/env python3
"""Validate WinUICompat API surface against a manifest file."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

PUBLIC_EVENT_RE = re.compile(
    r"^\s*public\s+event\s+[A-Za-z0-9_<>\.\?,\[\]\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*;",
    re.MULTILINE,
)
PUBLIC_PROPERTY_RE = re.compile(
    r"^\s*public\s+(?:static\s+)?(?:readonly\s+)?[A-Za-z0-9_<>\.\?,\[\]\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>)",
    re.MULTILINE,
)
PUBLIC_METHOD_RE = re.compile(
    r"^\s*public\s+(?:static\s+)?(?:override\s+)?[A-Za-z0-9_<>\.\?,\[\]\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.MULTILINE,
)


def extract_members(content: str) -> dict[str, set[str]]:
    events = set(PUBLIC_EVENT_RE.findall(content))
    properties = set(PUBLIC_PROPERTY_RE.findall(content))
    methods = set(PUBLIC_METHOD_RE.findall(content))

    methods.difference_update({"get", "set", "add", "remove", "init"})
    properties = {name for name in properties if not name.endswith("Property")}

    return {
        "events": events,
        "properties": properties,
        "methods": methods,
    }


def compare_type(root: Path, spec: dict) -> dict:
    file_path = root / spec["file"]
    content = file_path.read_text(encoding="utf-8", errors="replace")
    actual = extract_members(content)

    missing = {
        "events": sorted(set(spec.get("events", [])) - actual["events"]),
        "properties": sorted(set(spec.get("properties", [])) - actual["properties"]),
        "methods": sorted(set(spec.get("methods", [])) - actual["methods"]),
    }

    return {
        "name": spec["name"],
        "file": spec["file"],
        "missing": missing,
        "missing_total": sum(len(v) for v in missing.values()),
    }


def to_markdown(results: list[dict]) -> str:
    total_missing = sum(item["missing_total"] for item in results)
    lines = [
        "# WinUICompat Manifest Compare Report",
        "",
        f"- Missing members total: `{total_missing}`",
        "",
    ]

    for item in results:
        lines.append(f"## {item['name']}")
        lines.append("")
        lines.append(f"- File: `{item['file']}`")
        lines.append(f"- Missing: `{item['missing_total']}`")
        lines.append(f"- Events: `{', '.join(item['missing']['events']) or '(none)'}`")
        lines.append(f"- Properties: `{', '.join(item['missing']['properties']) or '(none)'}`")
        lines.append(f"- Methods: `{', '.join(item['missing']['methods']) or '(none)'}`")
        lines.append("")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate WinUICompat surface against manifest")
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    results = [compare_type(args.root, item) for item in manifest.get("types", [])]

    markdown = to_markdown(results)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(markdown, encoding="utf-8")

    json_out = args.json_out or args.out.with_suffix(".json")
    json_out.write_text(json.dumps({"results": results}, indent=2), encoding="utf-8")

    print(f"Wrote markdown report: {args.out}")
    print(f"Wrote JSON report: {json_out}")
    print(f"Missing members: {sum(item['missing_total'] for item in results)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
