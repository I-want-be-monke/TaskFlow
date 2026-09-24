#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "artifacts"
REPORT = ARTIFACTS / "nuget-vulnerabilities.json"


def collect_vulnerabilities(node: Any, path: tuple[str, ...] = ()) -> list[tuple[tuple[str, ...], Any]]:
    findings: list[tuple[tuple[str, ...], Any]] = []
    if isinstance(node, dict):
        vulnerabilities = node.get("vulnerabilities")
        if isinstance(vulnerabilities, list) and vulnerabilities:
            findings.append((path, node))
        for key, value in node.items():
            findings.extend(collect_vulnerabilities(value, (*path, str(key))))
    elif isinstance(node, list):
        for index, value in enumerate(node):
            findings.extend(collect_vulnerabilities(value, (*path, str(index))))
    return findings


def main() -> int:
    ARTIFACTS.mkdir(exist_ok=True)
    command = [
        "dotnet",
        "package",
        "list",
        "--project",
        "TaskFlow.sln",
        "--include-transitive",
        "--vulnerable",
        "--format",
        "json",
        "--output-version",
        "1",
        "--no-restore",
    ]
    completed = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        print(completed.stdout, end="")
        print(completed.stderr, file=sys.stderr, end="")
        return completed.returncode

    try:
        report = json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        print(f"NuGet vulnerability output was not valid JSON: {exc}", file=sys.stderr)
        print(completed.stdout[:2000], file=sys.stderr)
        return 2

    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    findings = collect_vulnerabilities(report)
    if findings:
        print("Known NuGet vulnerabilities were found; CI policy blocks every known advisory:", file=sys.stderr)
        for path, package in findings:
            package_id = package.get("id") or package.get("name") or "unknown-package"
            resolved = package.get("resolvedVersion") or package.get("resolved") or "unknown-version"
            print(f"- {package_id} {resolved} ({'/'.join(path)})", file=sys.stderr)
            for vulnerability in package.get("vulnerabilities", []):
                if isinstance(vulnerability, dict):
                    severity = vulnerability.get("severity", "unknown")
                    advisory = vulnerability.get("advisoryUrl") or vulnerability.get("url") or "unknown-advisory"
                    print(f"  severity={severity} advisory={advisory}", file=sys.stderr)
        return 1

    print(f"NuGet vulnerability gate passed. Report: {REPORT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
