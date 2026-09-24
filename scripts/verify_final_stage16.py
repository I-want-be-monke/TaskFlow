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
    require(file.exists(), f"Missing required Stage 16 file: {path}")
    return file.read_text(encoding="utf-8") if file.exists() else ""


browser = read("scripts/final_browser_e2e.py")
for token in (
    "Create account",
    "Sign in",
    "New project",
    "Edit project",
    "New task",
    "Create task",
    "Save changes",
    "New tag",
    "Attach",
    "Apply filters",
    "Archive",
    "tasks.project_archived",
    "Restore",
    "Reload latest",
    "api/v1/auth/me",
    "expected=404",
    "expected=400",
    "compose(env_file, \"restart\", \"api\")",
    "assert_internal_health",
    "assert_database_least_privilege",
    "assert_logs_redacted",
    "Confirm delete",
    "Logout",
):
    require(token in browser, f"Final browser E2E must cover: {token}")

require("sync_playwright" in browser and "chromium.launch(headless=True)" in browser,
        "Stage 16 must use a real headless browser, not only HTTP smoke requests")
require("ignore_https_errors=True" in browser, "Local production-like E2E must support the generated self-signed HTTPS certificate")
require("__Host-TaskFlow.Auth" in browser, "Restart/health E2E must verify the real secure auth cookie session")
require("X-XSRF-TOKEN" in browser, "Final E2E must exercise the antiforgery request header")

workflow_text = read(".github/workflows/ci.yml")
try:
    yaml.safe_load(workflow_text)
except yaml.YAMLError as exc:
    errors.append(f"CI workflow is invalid YAML after Stage 16 changes: {exc}")

for token in (
    "verify-static-stage16.sh",
    "playwright==1.63.0",
    "python3 -m playwright install --with-deps chromium",
    "scripts/final_browser_e2e.py",
):
    require(token in workflow_text, f"CI must enforce final Stage 16 gate: {token}")
require("continue-on-error" not in workflow_text, "Final CI quality gates must fail closed")

# Final DoD relies on these already-executable P0 suites rather than reimplementing them in browser code.
required_tests = {
    "tests/TaskFlow.IntegrationTests/Api/AuthSecurityTests.cs": (
        "UnsafeRequestWithoutAntiforgery_ReturnsRfc7807",
        "BolaMatrix_ForeignProjectTaskTagAndRelationReturnNotFound",
        "CookieAndAntiforgeryTokens_WorkAcrossApiReplicasSharingPostgresKeyRing",
    ),
    "tests/TaskFlow.IntegrationTests/Persistence/ConcurrencyTests.cs": (
        "ConcurrentUpdatesWithSameVersion_ProduceOneSuccessAndOneConflict",
        "ArchiveRace_SerializesProjectDependentMutations",
        "CanonicalLockOrder_CompletesWithoutDeadlock",
    ),
    "tests/TaskFlow.IntegrationTests/Admin/DbMigratorTests.cs": (
        "EmptyDatabase_MigratesSuccessfullyWithMigratorRole",
        "SecondMigrator_WhenAdvisoryLockIsHeld_FailsWithinBoundedTimeout",
        "ApiRole_CannotPerformSchemaDdl",
        "MigratorRole_HasRequiredSchemaDdlPrivilege",
    ),
    "tests/TaskFlow.IntegrationTests/Api/ObservabilityLoggingTests.cs": (
        "Formatter_DropsSensitiveStructuredFieldsAndExceptionMessage",
        "UnexpectedException_ProducesOneBoundaryErrorEventAndSafeProblemDetails",
    ),
}
for path, names in required_tests.items():
    text = read(path)
    for name in names:
        require(name in text, f"Final DoD requires executable test {path}:{name}")

# Reassert final architecture boundaries explicitly at the final stage.
for project, forbidden in {
    "src/TaskFlow.Domain/TaskFlow.Domain.csproj": ("EntityFrameworkCore", "AspNetCore", "Npgsql"),
    "src/TaskFlow.Application/TaskFlow.Application.csproj": ("Infrastructure", "Api", "AspNetCore", "HttpContext"),
    "src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj": ("TaskFlow.Api",),
    "src/TaskFlow.Client/TaskFlow.Client.csproj": ("TaskFlow.Api", "TaskFlow.Domain", "TaskFlow.Application", "TaskFlow.Infrastructure"),
}.items():
    content = read(project)
    for marker in forbidden:
        require(marker not in content, f"Final architecture boundary violation: {project} contains {marker}")

api_program = read("src/TaskFlow.Api/Program.cs")
require("MigrateAsync(" not in api_program and "EnsureCreated(" not in api_program and "EnsureCreatedAsync(" not in api_program,
        "API startup must never apply schema migrations")

production_migrate_calls: list[str] = []
for path in (ROOT / "src").rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    if "MigrateAsync(" in text or ".Migrate(" in text:
        production_migrate_calls.append(path.relative_to(ROOT).as_posix())
require(production_migrate_calls == ["src/TaskFlow.DbMigrator/MigrationRunner.cs"],
        f"Only DbMigrator may migrate production schema; found {production_migrate_calls}")

client_text = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "src/TaskFlow.Client").rglob("*.*") if path.is_file())
for forbidden in ("localStorage", "sessionStorage", "Bearer ", "refreshToken", "MarkupString", "innerHTML"):
    require(forbidden not in client_text, f"Final Client security boundary forbids {forbidden}")

for path in (
    "docs/STAGE_16_RATIONALE.md",
    "docs/STAGE_16_DOD.md",
    "Отчёт.md",
    "scripts/verify-static-stage16.sh",
    "scripts/verify-stage16.sh",
):
    read(path)

report = read("Отчёт.md")
for heading in (
    "Доменная область TaskFlow",
    "Стек реализации",
    "Основные сущности",
    "Архитектура",
    "CRUD",
    "12-factor",
    "Как запустить приложение локально",
):
    require(heading in report, f"Final root report must contain section: {heading}")

# Every stage tag through 15 must still exist in history before the new tag is created.
try:
    import subprocess
    tags = subprocess.check_output(["git", "tag", "--list"], cwd=ROOT, text=True).splitlines()
    for stage in range(0, 16):
        require(f"stage-{stage}-complete" in tags, f"Missing historical Git tag stage-{stage}-complete")
except Exception as exc:  # noqa: BLE001 - verifier reports Git tool failure
    errors.append(f"Could not inspect Git tags: {exc}")

if errors:
    print("Stage 16 final Definition of Done verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 16 final E2E/Definition-of-Done verification passed.")
