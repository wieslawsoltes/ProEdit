#!/usr/bin/env python3
"""Run WinUICompat parity checks and emit markdown/json dashboard reports."""

from __future__ import annotations

import argparse
import json
import shlex
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence


@dataclass
class CommandResult:
    name: str
    command: list[str]
    exit_code: int
    duration_seconds: float
    output_tail: str


def _repo_root_from_script() -> Path:
    return Path(__file__).resolve().parents[2]


def _run_command(name: str, command: Sequence[str], cwd: Path, output_tail_lines: int = 40) -> CommandResult:
    started = time.perf_counter()
    process = subprocess.run(
        list(command),
        cwd=cwd,
        capture_output=True,
        text=True,
        errors="replace",
    )
    duration = time.perf_counter() - started

    combined = "\n".join(part for part in (process.stdout, process.stderr) if part)
    lines = [line.rstrip() for line in combined.splitlines() if line.strip()]
    if len(lines) > output_tail_lines:
        lines = lines[-output_tail_lines:]
    output_tail = "\n".join(lines)

    return CommandResult(
        name=name,
        command=list(command),
        exit_code=process.returncode,
        duration_seconds=duration,
        output_tail=output_tail,
    )


def _load_manifest_missing_total(path: Path) -> int:
    if not path.exists():
        return -1

    payload = json.loads(path.read_text(encoding="utf-8"))
    total = 0
    for result in payload.get("results", []):
        total += int(result.get("missing_total", 0))
    return total


def _load_fixture_corpus(path: Path) -> tuple[str, int]:
    if not path.exists():
        return "(missing)", 0

    payload = json.loads(path.read_text(encoding="utf-8"))
    name = str(payload.get("name", path.name))
    scenarios = payload.get("scenarios", [])
    if not isinstance(scenarios, list):
        return name, 0
    return name, len(scenarios)


def _build_markdown(
    fixture_name: str,
    fixture_count: int,
    manifest_missing_total: int,
    results: list[CommandResult],
    manifest_report_path: Path,
    manifest_json_path: Path,
) -> str:
    all_commands_passed = all(item.exit_code == 0 for item in results)
    manifest_ok = manifest_missing_total == 0
    overall = all_commands_passed and manifest_ok

    lines: list[str] = []
    lines.append("# WinUICompat Parity Harness Report")
    lines.append("")
    lines.append(f"- Overall status: `{'PASS' if overall else 'FAIL'}`")
    lines.append(f"- Fixture corpus: `{fixture_name}` (`{fixture_count}` scenarios)")
    lines.append(f"- Manifest missing members: `{manifest_missing_total}`")
    lines.append(f"- Manifest markdown report: `{manifest_report_path}`")
    lines.append(f"- Manifest json report: `{manifest_json_path}`")
    lines.append("")
    lines.append("## Command Results")
    lines.append("")
    lines.append("| Step | Exit | Duration (s) | Command |")
    lines.append("|---|---:|---:|---|")
    for result in results:
        command_text = shlex.join(result.command)
        lines.append(
            f"| {result.name} | `{result.exit_code}` | `{result.duration_seconds:.2f}` | `{command_text}` |"
        )
    lines.append("")

    lines.append("## Output Tail")
    lines.append("")
    for result in results:
        lines.append(f"### {result.name}")
        lines.append("")
        if result.output_tail:
            lines.append("```text")
            lines.append(result.output_tail)
            lines.append("```")
        else:
            lines.append("(no output)")
        lines.append("")

    return "\n".join(lines)


def main() -> int:
    repo_root = _repo_root_from_script()

    parser = argparse.ArgumentParser(description="Run WinUICompat parity harness")
    parser.add_argument("--root", type=Path, default=repo_root, help="Repository root")
    parser.add_argument(
        "--manifest",
        type=Path,
        default=repo_root / "src/Vibe.Office.WinUICompat/Api/winui-richtext-manifest.json",
    )
    parser.add_argument(
        "--fixtures",
        type=Path,
        default=repo_root / "tools/richtext-compare/winuicompat-parity-fixtures.json",
    )
    parser.add_argument(
        "--manifest-report",
        type=Path,
        default=repo_root / "plan/winuicompat-manifest-compare-report.md",
    )
    parser.add_argument(
        "--manifest-json",
        type=Path,
        default=repo_root / "plan/winuicompat-manifest-compare-report.json",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=repo_root / "plan/winuicompat-parity-harness-report.md",
    )
    parser.add_argument("--json-out", type=Path, default=None)
    parser.add_argument("--skip-build", action="store_true", help="Skip solution build step")
    args = parser.parse_args()

    root = args.root.resolve()

    results: list[CommandResult] = []
    results.append(
        _run_command(
            "Manifest compare",
            [
                "python3",
                "tools/richtext-compare/winuicompat_manifest_compare.py",
                "--manifest",
                str(args.manifest),
                "--root",
                str(root),
                "--out",
                str(args.manifest_report),
                "--json-out",
                str(args.manifest_json),
            ],
            cwd=root,
        )
    )

    if not args.skip_build:
        results.append(_run_command("Build WinUICompat sample", ["dotnet", "build", "src/Vibe.WinUICompat.App/Vibe.WinUICompat.App.csproj", "-v", "minimal"], cwd=root))

    results.append(
        _run_command(
            "Core tests",
            ["dotnet", "test", "tests/Vibe.Office.WinUICompat.Tests/Vibe.Office.WinUICompat.Tests.csproj", "-v", "minimal"],
            cwd=root,
        )
    )
    results.append(
        _run_command(
            "Uno tests",
            ["dotnet", "test", "tests/Vibe.Office.WinUICompat.Uno.Tests/Vibe.Office.WinUICompat.Uno.Tests.csproj", "-v", "minimal"],
            cwd=root,
        )
    )

    fixture_name, fixture_count = _load_fixture_corpus(args.fixtures)
    manifest_missing_total = _load_manifest_missing_total(args.manifest_json)
    markdown = _build_markdown(
        fixture_name=fixture_name,
        fixture_count=fixture_count,
        manifest_missing_total=manifest_missing_total,
        results=results,
        manifest_report_path=args.manifest_report,
        manifest_json_path=args.manifest_json,
    )

    out_path = args.out
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(markdown, encoding="utf-8")

    json_payload = {
        "fixture_name": fixture_name,
        "fixture_count": fixture_count,
        "manifest_missing_total": manifest_missing_total,
        "results": [
            {
                "name": item.name,
                "command": item.command,
                "exit_code": item.exit_code,
                "duration_seconds": item.duration_seconds,
                "output_tail": item.output_tail,
            }
            for item in results
        ],
    }
    json_out = args.json_out or out_path.with_suffix(".json")
    json_out.write_text(json.dumps(json_payload, indent=2), encoding="utf-8")

    overall_pass = manifest_missing_total == 0 and all(item.exit_code == 0 for item in results)
    print(f"Wrote markdown report: {out_path}")
    print(f"Wrote JSON report: {json_out}")
    print(f"Overall status: {'PASS' if overall_pass else 'FAIL'}")
    return 0 if overall_pass else 1


if __name__ == "__main__":
    raise SystemExit(main())
