#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re
import sys
import yaml

ROOT = Path(__file__).resolve().parent.parent
errors: list[str] = []


def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


def read(path: str) -> str:
    file = ROOT / path
    require(file.exists(), f"Missing required Stage 15 file: {path}")
    return file.read_text(encoding="utf-8") if file.exists() else ""


workflow_text = read(".github/workflows/ci.yml")
try:
    workflow = yaml.safe_load(workflow_text) or {}
except yaml.YAMLError as exc:
    errors.append(f"CI workflow is invalid YAML: {exc}")
    workflow = {}

for token in (
    "dotnet restore TaskFlow.sln --locked-mode",
    "dotnet build TaskFlow.sln --no-restore --configuration Release",
    "TaskFlow.Domain.Tests.csproj",
    "TaskFlow.Application.Tests.csproj",
    "TaskFlow.IntegrationTests.csproj",
    "check_nuget_vulnerabilities.py",
    "scan-secrets.sh",
    "compose-up.sh",
    "scan-containers.sh",
    "compose-smoke.sh",
    "ci_e2e_smoke.py",
):
    require(token in workflow_text, f"CI workflow must include gate: {token}")

require("permissions:\n  contents: read" in workflow_text, "CI must use read-only default GitHub token permissions")
require("fetch-depth: 0" in workflow_text, "Secret scan must checkout full Git history")
require("persist-credentials: false" in workflow_text, "Security-sensitive CI jobs must not persist checkout credentials")
require("needs: [quality, integration_p0, supply_chain]" in workflow_text, "Container gate must depend on code/test/supply-chain gates")
require("ubuntu-24.04" in workflow_text, "CI runner OS must be explicit instead of mutable ubuntu-latest")
require("@v" not in workflow_text, "GitHub Actions must be pinned to immutable commit SHAs, not mutable version tags")

sha_uses = re.findall(r"uses:\s+[^@\s]+@([0-9a-f]{40})", workflow_text)
uses_lines = [line for line in workflow_text.splitlines() if "uses:" in line]
require(len(sha_uses) == len(uses_lines), "Every GitHub Action use must be pinned to a full 40-character SHA")

secret_scan = read("scripts/scan-secrets.sh")
require("gitleaks:v8.30.1" in secret_scan, "Secret scanner version must be pinned")
require("--redact" in secret_scan, "Secret scanner must redact findings from logs")

container_scan = read("scripts/scan-containers.sh")
require("trivy:0.70.0" in container_scan, "Trivy version must be pinned")
require("HIGH,CRITICAL" in container_scan, "Trivy report must include HIGH and CRITICAL vulnerabilities")
require("--severity CRITICAL" in container_scan and "--ignore-unfixed" in container_scan and "--exit-code 1" in container_scan,
        "Container gate must fail on fixable CRITICAL vulnerabilities")
for image in ("taskflow-api", "taskflow-migrator", "taskflow-frontend"):
    require(image in container_scan, f"Container scan must cover {image}")

nuget_scan = read("scripts/check_nuget_vulnerabilities.py")
require('"--vulnerable"' in nuget_scan and '"--include-transitive"' in nuget_scan and '"--format"' in nuget_scan,
        "NuGet scan must include transitive known vulnerabilities in machine-readable form")

for path in ("scripts/ci_e2e_smoke.py", "scripts/verify-static-stage15.sh", "scripts/verify-stage15.sh", ".github/dependabot.yml"):
    read(path)

for p0_test in (
    "tests/TaskFlow.IntegrationTests/Api/AuthSecurityTests.cs",
    "tests/TaskFlow.IntegrationTests/Persistence/ConcurrencyTests.cs",
    "tests/TaskFlow.IntegrationTests/Admin/DbMigratorTests.cs",
    "tests/TaskFlow.IntegrationTests/Persistence/PostgresSchemaTests.cs",
):
    read(p0_test)

require("package-ecosystem: github-actions" in read(".github/dependabot.yml"), "Dependabot must watch GitHub Actions")
require("package-ecosystem: nuget" in read(".github/dependabot.yml"), "Dependabot must watch NuGet packages")

if errors:
    print("Stage 15 CI verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 15 CI/quality-gate verification passed.")
