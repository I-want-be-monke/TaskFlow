#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

EXPECTED = {
    "src/TaskFlow.Domain/TaskFlow.Domain.csproj": set(),
    "src/TaskFlow.Application/TaskFlow.Application.csproj": {"TaskFlow.Domain"},
    "src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj": {"TaskFlow.Application", "TaskFlow.Domain"},
    "src/TaskFlow.Api/TaskFlow.Api.csproj": {"TaskFlow.Application", "TaskFlow.Infrastructure"},
    "src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj": {"TaskFlow.Infrastructure"},
    "src/TaskFlow.Client/TaskFlow.Client.csproj": set(),
}

REQUIRED_BUILD_PROPS = {
    "Nullable": "enable",
    "ImplicitUsings": "enable",
    "TreatWarningsAsErrors": "true",
    "EnforceCodeStyleInBuild": "true",
    "EnableNETAnalyzers": "true",
    "LangVersion": "14.0",
    "RestorePackagesWithLockFile": "true",
}


def project_refs(project: Path) -> set[str]:
    tree = ET.parse(project)
    refs: set[str] = set()
    for node in tree.findall(".//ProjectReference"):
        include = node.attrib["Include"].replace("\\", "/")
        refs.add(Path(include).stem)
    return refs


def property_values(path: Path) -> dict[str, str]:
    tree = ET.parse(path)
    values: dict[str, str] = {}
    for group in tree.findall("PropertyGroup"):
        for child in group:
            if child.text:
                values[child.tag] = child.text.strip()
    return values


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


for rel, expected in EXPECTED.items():
    actual = project_refs(ROOT / rel)
    if actual != expected:
        fail(f"{rel}: expected refs {sorted(expected)}, got {sorted(actual)}")

client_text = (ROOT / "src/TaskFlow.Client/TaskFlow.Client.csproj").read_text(encoding="utf-8")
if "ProjectReference" in client_text:
    fail("TaskFlow.Client must not reference server projects")

props = property_values(ROOT / "Directory.Build.props")
for name, expected in REQUIRED_BUILD_PROPS.items():
    if props.get(name) != expected:
        fail(f"Directory.Build.props: {name} must be {expected!r}, got {props.get(name)!r}")

global_json = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
if global_json.get("sdk", {}).get("version") != "10.0.401":
    fail("global.json must pin .NET SDK 10.0.401")
if global_json.get("sdk", {}).get("allowPrerelease") is not False:
    fail("global.json must disable prerelease SDKs")

required_lock_files = [ROOT / "packages.lock.json"]
required_lock_files.extend(p.parent / "packages.lock.json" for p in ROOT.glob("src/**/*.csproj"))
required_lock_files.extend(p.parent / "packages.lock.json" for p in ROOT.glob("tests/**/*.csproj"))
for lock in required_lock_files:
    if not lock.is_file():
        fail(f"missing lock file: {lock.relative_to(ROOT)}")

for generated in ("bin", "obj"):
    if any(p.is_dir() for p in ROOT.rglob(generated)):
        fail(f"generated directory {generated}/ must not be committed")

for source in ROOT.rglob("*.cs"):
    text = source.read_text(encoding="utf-8")
    if "class AssemblyMarker;" in text or "class StageZeroMarker;" in text:
        fail(f"invalid body-less class declaration: {source.relative_to(ROOT)}")

print("Stage 0 static architecture/build checks: OK")
