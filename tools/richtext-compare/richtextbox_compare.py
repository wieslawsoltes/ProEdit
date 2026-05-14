#!/usr/bin/env python3
"""Compare WPF RichTextBox public members against ProEdit RichTextBox members.

Usage:
  python3 tools/richtext-compare/richtextbox_compare.py \
    --wpf /path/to/wpf/RichTextBox.cs \
    --ours /path/to/ProEdit.RichText.Avalonia/RichTextBox.cs \
    --out /path/to/report.md
"""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


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


@dataclass(frozen=True)
class Members:
    events: set[str]
    properties: set[str]
    methods: set[str]

    def all_names(self) -> set[str]:
        return set().union(self.events, self.properties, self.methods)


def _extract_members(content: str, class_name: str) -> Members:
    events = set(PUBLIC_EVENT_RE.findall(content))
    properties = set(PUBLIC_PROPERTY_RE.findall(content))
    methods = set(PUBLIC_METHOD_RE.findall(content))

    # Filter constructor and property accessors that may be captured.
    methods.discard(class_name)
    methods.difference_update({"get", "set", "init", "add", "remove"})

    # Filter obvious fields/constants captured as "properties" due relaxed regex.
    field_like = {name for name in properties if name.endswith("Property")}
    properties.difference_update(field_like)

    return Members(events=events, properties=properties, methods=methods)


def _sorted(items: Iterable[str]) -> list[str]:
    return sorted(items, key=lambda item: item.lower())


def build_report(wpf_path: Path, ours_path: Path) -> dict:
    wpf_text = wpf_path.read_text(encoding="utf-8", errors="replace")
    ours_text = ours_path.read_text(encoding="utf-8", errors="replace")

    wpf = _extract_members(wpf_text, "RichTextBox")
    ours = _extract_members(ours_text, "RichTextBox")

    missing_events = _sorted(wpf.events - ours.events)
    missing_properties = _sorted(wpf.properties - ours.properties)
    missing_methods = _sorted(wpf.methods - ours.methods)

    extra_events = _sorted(ours.events - wpf.events)
    extra_properties = _sorted(ours.properties - wpf.properties)
    extra_methods = _sorted(ours.methods - wpf.methods)

    return {
        "wpf": {
            "events": _sorted(wpf.events),
            "properties": _sorted(wpf.properties),
            "methods": _sorted(wpf.methods),
            "total": len(wpf.all_names()),
        },
        "ours": {
            "events": _sorted(ours.events),
            "properties": _sorted(ours.properties),
            "methods": _sorted(ours.methods),
            "total": len(ours.all_names()),
        },
        "missing": {
            "events": missing_events,
            "properties": missing_properties,
            "methods": missing_methods,
            "total": len(missing_events) + len(missing_properties) + len(missing_methods),
        },
        "extra": {
            "events": extra_events,
            "properties": extra_properties,
            "methods": extra_methods,
            "total": len(extra_events) + len(extra_properties) + len(extra_methods),
        },
    }


def to_markdown(report: dict, wpf_path: Path, ours_path: Path) -> str:
    lines: list[str] = []
    lines.append("# RichTextBox API Compare Report")
    lines.append("")
    lines.append(f"- WPF source: `{wpf_path}`")
    lines.append(f"- ProEdit source: `{ours_path}`")
    lines.append("")
    lines.append("## Summary")
    lines.append("")
    lines.append(f"- WPF public member count: `{report['wpf']['total']}`")
    lines.append(f"- ProEdit public member count: `{report['ours']['total']}`")
    lines.append(f"- Missing member count (WPF -> ProEdit): `{report['missing']['total']}`")
    lines.append(f"- Extra member count (ProEdit -> WPF): `{report['extra']['total']}`")
    lines.append("")
    lines.append("## Missing Members")
    lines.append("")
    lines.append(f"- Events ({len(report['missing']['events'])}): `{', '.join(report['missing']['events']) or '(none)'}`")
    lines.append(f"- Properties ({len(report['missing']['properties'])}): `{', '.join(report['missing']['properties']) or '(none)'}`")
    lines.append(f"- Methods ({len(report['missing']['methods'])}): `{', '.join(report['missing']['methods']) or '(none)'}`")
    lines.append("")
    lines.append("## Extra Members")
    lines.append("")
    lines.append(f"- Events ({len(report['extra']['events'])}): `{', '.join(report['extra']['events']) or '(none)'}`")
    lines.append(f"- Properties ({len(report['extra']['properties'])}): `{', '.join(report['extra']['properties']) or '(none)'}`")
    lines.append(f"- Methods ({len(report['extra']['methods'])}): `{', '.join(report['extra']['methods']) or '(none)'}`")
    lines.append("")
    lines.append("## Notes")
    lines.append("")
    lines.append("- This report compares members declared directly in each class source file.")
    lines.append("- It does not include inherited members from base types.")
    lines.append("- Differences may include intentional architecture deviations.")
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare WPF and ProEdit RichTextBox API members.")
    parser.add_argument("--wpf", required=True, type=Path, help="Path to WPF RichTextBox.cs")
    parser.add_argument("--ours", required=True, type=Path, help="Path to ProEdit RichTextBox.cs")
    parser.add_argument("--out", required=True, type=Path, help="Path to markdown output report")
    parser.add_argument("--json-out", type=Path, default=None, help="Optional JSON output path")
    args = parser.parse_args()

    report = build_report(args.wpf, args.ours)
    markdown = to_markdown(report, args.wpf, args.ours)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(markdown, encoding="utf-8")

    json_out = args.json_out or args.out.with_suffix(".json")
    json_out.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"Wrote markdown report: {args.out}")
    print(f"Wrote JSON report: {json_out}")
    print(f"Missing members: {report['missing']['total']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
