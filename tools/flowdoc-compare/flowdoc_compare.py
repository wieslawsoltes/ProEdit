#!/usr/bin/env python3
import argparse
import json
import subprocess
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Set, Tuple, Optional

TYPE_RE = re.compile(
    r"public\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*"
    r"(class|struct|enum|interface)\s+(?P<name>\w+)" 
    r"(?:\s*:\s*(?P<bases>[^\{\n]+))?",
    re.MULTILINE,
)

PROP_RE = re.compile(r"public\s+[^;\n\(\{]+?\s+(?P<name>\w+)\s*\{\s*get\b", re.MULTILINE)
PROP_EXPR_RE = re.compile(r"public\s+[^;\n\(\{]+?\s+(?P<name>\w+)\s*=>", re.MULTILINE)
DP_RE = re.compile(r"public\s+static\s+readonly\s+DependencyProperty\s+(?P<name>\w+)Property", re.MULTILINE)
DP_RE2 = re.compile(r"public\s+static\s+DependencyProperty\s+(?P<name>\w+)Property", re.MULTILINE)


DEFAULT_ALLOWLIST = {
    "FlowDocument",
    "TextElement",
    "TextElementCollection",
    "Block",
    "BlockCollection",
    "Inline",
    "InlineCollection",
    "Paragraph",
    "Span",
    "Run",
    "Bold",
    "Italic",
    "Underline",
    "LineBreak",
    "Hyperlink",
    "Section",
    "List",
    "ListItem",
    "ListItemCollection",
    "Table",
    "TableRowGroup",
    "TableRowGroupCollection",
    "TableRow",
    "TableRowCollection",
    "TableCell",
    "TableCellCollection",
    "TableColumn",
    "TableColumnCollection",
    "AnchoredBlock",
    "Figure",
    "Floater",
    "BlockUIContainer",
    "InlineUIContainer",
}


@dataclass
class TypeInfo:
    kind: str
    bases: List[str]
    properties: Set[str]


@dataclass
class ModelInfo:
    types: Dict[str, TypeInfo]


@dataclass
class RoundtripValidationResult:
    fixtures_path: Path
    project_path: Path
    filter_text: str
    fixture_names: List[str]
    list_tests_exit_code: int
    list_tests_output: str
    missing_fixture_tests: List[str]
    run_tests_exit_code: int
    run_tests_output: str

    @property
    def passed(self) -> bool:
        return (
            self.list_tests_exit_code == 0
            and self.run_tests_exit_code == 0
            and len(self.missing_fixture_tests) == 0
        )


def load_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="ignore")


def find_matching_brace(text: str, start_index: int) -> int:
    depth = 0
    for idx in range(start_index, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return idx
    return -1


def extract_types(text: str) -> Dict[str, TypeInfo]:
    types: Dict[str, TypeInfo] = {}
    for match in TYPE_RE.finditer(text):
        name = match.group("name")
        kind = match.group(1)
        bases = []
        bases_raw = match.group("bases")
        if bases_raw:
            bases = [b.strip() for b in bases_raw.split(",") if b.strip()]

        brace_index = text.find("{", match.end())
        if brace_index == -1:
            continue

        end_index = find_matching_brace(text, brace_index)
        if end_index == -1:
            continue

        body = text[brace_index + 1 : end_index]
        properties = set()

        for p in PROP_RE.finditer(body):
            properties.add(p.group("name"))
        for p in PROP_EXPR_RE.finditer(body):
            properties.add(p.group("name"))
        for p in DP_RE.finditer(body):
            properties.add(p.group("name"))
        for p in DP_RE2.finditer(body):
            properties.add(p.group("name"))

        types[name] = TypeInfo(kind=kind, bases=bases, properties=properties)

    return types


def should_include_type(name: str, info: TypeInfo, include_all: bool, allowlist: Set[str]) -> bool:
    if include_all:
        return True
    if name in allowlist:
        return True
    base_matches = {"TextElement", "Block", "Inline", "Span", "AnchoredBlock", "BlockCollection", "InlineCollection"}
    for base in info.bases:
        if base.split("<", 1)[0].strip() in base_matches:
            return True
    return False


def build_model(root: Path, namespace_hint: str, include_all: bool, allowlist: Set[str]) -> ModelInfo:
    types: Dict[str, TypeInfo] = {}
    for file in root.rglob("*.cs"):
        text = load_text(file)
        if namespace_hint and f"namespace {namespace_hint}" not in text:
            continue
        for name, info in extract_types(text).items():
            if should_include_type(name, info, include_all, allowlist):
                types[name] = info
    return ModelInfo(types=types)


def resolve_effective_properties(model: ModelInfo) -> Dict[str, Set[str]]:
    cache: Dict[str, Set[str]] = {}

    def resolve(name: str) -> Set[str]:
        if name in cache:
            return cache[name]
        info = model.types.get(name)
        if info is None:
            cache[name] = set()
            return cache[name]
        props = set(info.properties)
        for base in info.bases:
            base_name = base.split("<", 1)[0].strip()
            if base_name in model.types:
                props |= resolve(base_name)
        cache[name] = props
        return props

    for type_name in model.types.keys():
        resolve(type_name)

    return cache


def compare_models(wpf: ModelInfo, ours: ModelInfo) -> Tuple[Set[str], Set[str], Dict[str, Dict[str, Set[str]]]]:
    wpf_types = set(wpf.types.keys())
    our_types = set(ours.types.keys())
    missing_types = sorted(wpf_types - our_types)
    extra_types = sorted(our_types - wpf_types)

    property_diff: Dict[str, Dict[str, Set[str]]] = {}
    wpf_props_map = resolve_effective_properties(wpf)
    our_props_map = resolve_effective_properties(ours)
    for name in sorted(wpf_types & our_types):
        wpf_props = wpf_props_map.get(name, set())
        our_props = our_props_map.get(name, set())
        missing_props = sorted(wpf_props - our_props)
        extra_props = sorted(our_props - wpf_props)
        if missing_props or extra_props:
            property_diff[name] = {
                "missing": set(missing_props),
                "extra": set(extra_props),
            }

    return set(missing_types), set(extra_types), property_diff


def run_process(args: List[str], cwd: Path) -> Tuple[int, str]:
    completed = subprocess.run(
        args,
        cwd=str(cwd),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )
    return completed.returncode, completed.stdout


def parse_fixture_names(fixtures_path: Path) -> List[str]:
    payload = json.loads(fixtures_path.read_text(encoding="utf-8"))
    if isinstance(payload, list):
        names: List[str] = []
        for item in payload:
            if isinstance(item, str) and item.strip():
                names.append(item.strip())
        return names

    if isinstance(payload, dict):
        fixtures = payload.get("fixtures", [])
        names = []
        if isinstance(fixtures, list):
            for item in fixtures:
                if isinstance(item, str) and item.strip():
                    names.append(item.strip())
                    continue
                if isinstance(item, dict):
                    name = item.get("name")
                    if isinstance(name, str) and name.strip():
                        names.append(name.strip())
        return names

    return []


def run_roundtrip_validation(
    fixtures_path: Path,
    project_path: Path,
    filter_text: str,
) -> RoundtripValidationResult:
    fixture_names = parse_fixture_names(fixtures_path)
    project_root = project_path.parent

    list_args = ["dotnet", "test", str(project_path), "-c", "Debug", "--list-tests"]
    list_tests_exit_code, list_tests_output = run_process(list_args, project_root)
    missing_fixture_tests = [name for name in fixture_names if name not in list_tests_output]

    run_args = [
        "dotnet",
        "test",
        str(project_path),
        "-c",
        "Debug",
        "--filter",
        filter_text,
    ]
    run_tests_exit_code, run_tests_output = run_process(run_args, project_root)

    return RoundtripValidationResult(
        fixtures_path=fixtures_path,
        project_path=project_path,
        filter_text=filter_text,
        fixture_names=fixture_names,
        list_tests_exit_code=list_tests_exit_code,
        list_tests_output=list_tests_output,
        missing_fixture_tests=missing_fixture_tests,
        run_tests_exit_code=run_tests_exit_code,
        run_tests_output=run_tests_output,
    )


def write_report(
    out_path: Path,
    wpf_root: Path,
    ours_root: Path,
    missing_types: Set[str],
    extra_types: Set[str],
    property_diff: Dict[str, Dict[str, Set[str]]],
    roundtrip_result: Optional[RoundtripValidationResult],
):
    lines: List[str] = []
    lines.append("# FlowDocument Model Comparison")
    lines.append("")
    lines.append(f"WPF source: `{wpf_root}`")
    lines.append(f"VibeOffice source: `{ours_root}`")
    lines.append("")
    lines.append("## Summary")
    lines.append("")
    lines.append(f"- Missing types: {len(missing_types)}")
    lines.append(f"- Extra types: {len(extra_types)}")
    lines.append(f"- Types with property differences: {len(property_diff)}")
    if roundtrip_result is not None:
        lines.append(f"- Roundtrip fixture validation: {'PASS' if roundtrip_result.passed else 'FAIL'}")
    lines.append("")
    lines.append("Note: Property comparisons include inherited properties only when the base type is also")
    lines.append("present in the scanned namespace subset. Base types outside the subset are not included.")
    lines.append("")

    if missing_types:
        lines.append("## Missing Types (in VibeOffice)")
        lines.append("")
        for name in sorted(missing_types):
            lines.append(f"- {name}")
        lines.append("")

    if extra_types:
        lines.append("## Extra Types (not in WPF model subset)")
        lines.append("")
        for name in sorted(extra_types):
            lines.append(f"- {name}")
        lines.append("")

    if property_diff:
        lines.append("## Property Differences")
        lines.append("")
        for name in sorted(property_diff.keys()):
            diff = property_diff[name]
            missing = sorted(diff["missing"])
            extra = sorted(diff["extra"])
            lines.append(f"### {name}")
            lines.append("")
            if missing:
                lines.append("Missing properties:")
                for prop in missing:
                    lines.append(f"- {prop}")
                lines.append("")
            if extra:
                lines.append("Extra properties:")
                for prop in extra:
                    lines.append(f"- {prop}")
                lines.append("")

    if roundtrip_result is not None:
        lines.append("## Roundtrip Fixture Validation")
        lines.append("")
        lines.append(f"- Fixture manifest: `{roundtrip_result.fixtures_path}`")
        lines.append(f"- Test project: `{roundtrip_result.project_path}`")
        lines.append(f"- Test filter: `{roundtrip_result.filter_text}`")
        lines.append(f"- Fixture count: {len(roundtrip_result.fixture_names)}")
        lines.append(f"- Status: {'PASS' if roundtrip_result.passed else 'FAIL'}")
        lines.append(f"- `dotnet test --list-tests` exit code: {roundtrip_result.list_tests_exit_code}")
        lines.append(f"- `dotnet test` exit code: {roundtrip_result.run_tests_exit_code}")
        lines.append("")
        if roundtrip_result.missing_fixture_tests:
            lines.append("Missing fixtures in discovered test names:")
            for name in roundtrip_result.missing_fixture_tests:
                lines.append(f"- {name}")
            lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare WPF FlowDocument model to VibeOffice FlowDocument model.")
    parser.add_argument("--wpf", required=True, help="Path to WPF System.Windows.Documents folder.")
    parser.add_argument("--ours", required=True, help="Path to Vibe.Office.FlowDocument folder.")
    parser.add_argument("--out", required=True, help="Output markdown report path.")
    parser.add_argument("--include-all", action="store_true", help="Include all public types in namespace.")
    parser.add_argument(
        "--allow",
        action="append",
        default=[],
        help="Type name to force include (can be repeated).",
    )
    parser.add_argument(
        "--namespace",
        default="System.Windows.Documents",
        help="Namespace hint for WPF filtering (default: System.Windows.Documents).",
    )
    parser.add_argument(
        "--roundtrip-fixtures",
        help="Optional path to a roundtrip fixture manifest JSON file.",
    )
    parser.add_argument(
        "--roundtrip-project",
        help="Optional path to the roundtrip test project (defaults to tests/Vibe.Office.FlowDocument.Tests/Vibe.Office.FlowDocument.Tests.csproj).",
    )
    parser.add_argument(
        "--roundtrip-filter",
        default="Category=FlowRoundtrip",
        help="Test filter used for roundtrip fixture execution.",
    )

    args = parser.parse_args()
    wpf_root = Path(args.wpf).expanduser().resolve()
    ours_root = Path(args.ours).expanduser().resolve()
    out_path = Path(args.out).expanduser().resolve()

    allowlist = set(DEFAULT_ALLOWLIST)
    allowlist.update(args.allow)

    wpf_model = build_model(wpf_root, args.namespace, args.include_all, allowlist)
    ours_model = build_model(ours_root, "Vibe.Office.FlowDocument", True, allowlist)

    missing_types, extra_types, property_diff = compare_models(wpf_model, ours_model)

    roundtrip_result: Optional[RoundtripValidationResult] = None
    if args.roundtrip_fixtures:
        script_root = Path(__file__).resolve().parent
        repo_root = script_root.parent.parent
        fixtures_path = Path(args.roundtrip_fixtures).expanduser().resolve()
        if args.roundtrip_project:
            project_path = Path(args.roundtrip_project).expanduser().resolve()
        else:
            project_path = (
                repo_root
                / "tests"
                / "Vibe.Office.FlowDocument.Tests"
                / "Vibe.Office.FlowDocument.Tests.csproj"
            ).resolve()

        roundtrip_result = run_roundtrip_validation(fixtures_path, project_path, args.roundtrip_filter)

    write_report(out_path, wpf_root, ours_root, missing_types, extra_types, property_diff, roundtrip_result)

    report = {
        "wpf": str(wpf_root),
        "ours": str(ours_root),
        "missing_types": sorted(missing_types),
        "extra_types": sorted(extra_types),
        "property_diff": {
            name: {
                "missing": sorted(diff["missing"]),
                "extra": sorted(diff["extra"]),
            }
            for name, diff in property_diff.items()
        },
    }
    if roundtrip_result is not None:
        report["roundtrip_validation"] = {
            "fixtures_path": str(roundtrip_result.fixtures_path),
            "project_path": str(roundtrip_result.project_path),
            "filter": roundtrip_result.filter_text,
            "fixture_names": roundtrip_result.fixture_names,
            "missing_fixture_tests": roundtrip_result.missing_fixture_tests,
            "list_tests_exit_code": roundtrip_result.list_tests_exit_code,
            "run_tests_exit_code": roundtrip_result.run_tests_exit_code,
            "passed": roundtrip_result.passed,
        }

    json_path = out_path.with_suffix(".json")
    json_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"Wrote report to {out_path}")
    print(f"Wrote JSON report to {json_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
