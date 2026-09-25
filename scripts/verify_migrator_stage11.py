#!/usr/bin/env python3
from pathlib import Path
import json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MIGRATOR = ROOT / "src" / "TaskFlow.DbMigrator"
API = ROOT / "src" / "TaskFlow.Api"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Admin"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 11 verification failed: {message}")


def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


program = read(MIGRATOR / "Program.cs")
settings = read(MIGRATOR / "DbMigratorSettings.cs")
application = read(MIGRATOR / "DbMigratorApplication.cs")
runner = read(MIGRATOR / "MigrationRunner.cs")
exit_codes = read(MIGRATOR / "DbMigratorExitCodes.cs")
all_migrator = "\n".join(path.read_text(encoding="utf-8") for path in sorted(MIGRATOR.glob("*.cs")))

for token in [
    "RunFromEnvironmentAsync",
    "Console.CancelKeyPress",
    "ConnectionStrings__Postgres",
    "Migrator__LockTimeoutSeconds",
    "TaskFlowDbContext",
    "MigrationsAssembly(typeof(TaskFlowDbContext).Assembly.FullName)",
    "pg_try_advisory_lock",
    "pg_advisory_unlock",
    "AdvisoryLockKey",
    "Database.MigrateAsync",
    "MigrationStarted",
    "MigrationCompleted",
    "MigrationFailed",
    "AddTaskFlowJsonConsole",
    'serviceName: "TaskFlow.DbMigrator"',
    "operation_id",
    "release_id",
]:
    require(token in all_migrator, f"DbMigrator missing {token}")

for code in ["Success = 0", "ConfigurationError = 2", "LockTimeout = 3", "MigrationFailed = 4", "Cancelled = 130"]:
    require(code in exit_codes, f"stable exit code missing: {code}")

require("CancelAfter(timeout)" in runner, "advisory-lock wait must be bounded")
require("EnableSensitiveDataLogging(false)" in runner, "migrator must keep EF sensitive logging disabled")
require("RetryDelay" in runner and "Task.Delay" in runner, "advisory lock must retry boundedly instead of blocking forever")
require("MigrationLockTimeoutException" in runner, "lock-timeout outcome must be explicit")
require("return DbMigratorExitCodes.LockTimeout" in runner, "lock timeout must return non-zero exit")
require("return DbMigratorExitCodes.MigrationFailed" in runner, "migration failure must return non-zero exit")

# Production migration ownership: only the admin process applies schema changes.
production_migrate_calls = []
for path in (ROOT / "src").rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    if "MigrateAsync(" in text or "Database.Migrate(" in text or "EnsureCreated(" in text:
        production_migrate_calls.append(path.relative_to(ROOT).as_posix())
require(production_migrate_calls == ["src/TaskFlow.DbMigrator/MigrationRunner.cs"],
        f"only DbMigrator may migrate production schema, got {production_migrate_calls}")

api_sources = "\n".join(path.read_text(encoding="utf-8") for path in API.rglob("*.cs"))
for forbidden in ["MigrateAsync(", "Database.Migrate(", "EnsureCreated(", "EnsureCreatedAsync("]:
    require(forbidden not in api_sources, f"API startup/runtime must not contain {forbidden}")

# Project dependency remains DbMigrator -> Infrastructure only; direct NuGet packages are tooling/runtime libs.
project = ET.parse(MIGRATOR / "TaskFlow.DbMigrator.csproj")
project_refs = {Path(node.attrib["Include"]).stem for node in project.findall(".//ProjectReference")}
require(project_refs == {"TaskFlow.Infrastructure"}, f"unexpected DbMigrator project refs: {sorted(project_refs)}")
package_refs = {node.attrib["Include"] for node in project.findall(".//PackageReference")}
require(not package_refs, f"DbMigrator must consume persistence/logging packages through Infrastructure, got {sorted(package_refs)}")

lock = json.loads(read(MIGRATOR / "packages.lock.json"))
locked = lock["dependencies"]["net10.0"]
for package, version in [
    ("Microsoft.EntityFrameworkCore", "10.0.12"),
    ("Microsoft.Extensions.Logging.Console", "10.0.12"),
    ("Npgsql.EntityFrameworkCore.PostgreSQL", "10.0.3"),
]:
    require(
        locked.get(package, {}).get("type") in {"Transitive", "CentralTransitive"},
        f"{package} must stay transitive through Infrastructure",
    )
    require(locked.get(package, {}).get("resolved") == version, f"{package} lock version changed")

least_privilege = read(ROOT / "deploy" / "postgres" / "least-privilege.sql")
for token in [
    "taskflow_app",
    "taskflow_migrator",
    "REVOKE CREATE ON SCHEMA public FROM PUBLIC",
    "GRANT USAGE ON SCHEMA public TO taskflow_app",
    "GRANT USAGE, CREATE ON SCHEMA public TO taskflow_migrator",
    "ALTER DEFAULT PRIVILEGES FOR ROLE taskflow_migrator",
]:
    require(token in least_privilege, f"least-privilege template missing {token}")
require("CREATE ROLE" not in least_privilege.upper() and "PASSWORD '" not in least_privilege.upper(),
        "least-privilege template must not embed role-creation credentials")

env_example = read(ROOT / ".env.example")
for token in ["ConnectionStrings__Postgres", "Migrator__LockTimeoutSeconds=30", "Username=taskflow_migrator"]:
    require(token in env_example, f".env.example missing Stage 11 contract: {token}")

fixture = read(TESTS / "MigratorPostgresFixture.cs")
tests = read(TESTS / "DbMigratorTests.cs")
for token in ["postgres:18-alpine", "CreateEmptyDatabaseAsync", "CreateRoleAsync", "REVOKE CREATE ON SCHEMA public FROM PUBLIC"]:
    require(token in fixture, f"migrator PostgreSQL fixture missing {token}")
for token in [
    "EmptyDatabase_MigratesSuccessfullyWithMigratorRole",
    "SecondMigrator_WhenAdvisoryLockIsHeld_FailsWithinBoundedTimeout",
    "MigrationFailure_ReturnsNonZeroExitCode",
    "ApiRole_CannotPerformSchemaDdl",
    "MigratorRole_HasRequiredSchemaDdlPrivilege",
    "MigrationEventCatalog_RemainsStable",
    "DbMigratorExitCodes.Success",
    "DbMigratorExitCodes.LockTimeout",
    "PostgresErrorCodes.InsufficientPrivilege",
    "MigrationRunner.AdvisoryLockKey",
]:
    require(token in tests, f"Stage 11 integration tests missing {token}")

integration_project = read(ROOT / "tests" / "TaskFlow.IntegrationTests" / "TaskFlow.IntegrationTests.csproj")
require("TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj" in integration_project,
        "IntegrationTests must reference DbMigrator for admin-process contract tests")

print("Stage 11 DbMigrator/admin-process verification passed.")
