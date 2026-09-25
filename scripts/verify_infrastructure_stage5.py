#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
INFRA = SRC / "TaskFlow.Infrastructure"
DOMAIN = SRC / "TaskFlow.Domain"
APP = SRC / "TaskFlow.Application"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests"
errors: list[str] = []


def require(path: Path) -> str:
    if not path.exists():
        errors.append(f"missing required file: {path.relative_to(ROOT)}")
        return ""
    return path.read_text(encoding="utf-8")


def require_tokens(path: Path, *tokens: str) -> None:
    text = require(path)
    for token in tokens:
        if token not in text:
            errors.append(f"{path.relative_to(ROOT)}: missing token: {token}")


# Persistence packages are allowed only at the infrastructure boundary and integration-test boundary.
for project in ROOT.rglob("*.csproj"):
    tree = ET.parse(project)
    package_names = [
        node.attrib.get("Include", "")
        for node in tree.getroot().iter("PackageReference")
    ]
    forbidden = [
        name
        for name in package_names
        if name.startswith("Microsoft.EntityFrameworkCore")
        or name.startswith("Microsoft.AspNetCore.Identity.EntityFrameworkCore")
        or name.startswith("Microsoft.AspNetCore.DataProtection.EntityFrameworkCore")
        or name.startswith("Npgsql")
    ]
    relative = project.relative_to(ROOT).as_posix()
    if forbidden and relative not in {
        "src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj",
        "tests/TaskFlow.IntegrationTests/TaskFlow.IntegrationTests.csproj",
    }:
        errors.append(f"{relative}: persistence package leaked outside Infrastructure/tests: {forbidden}")

for boundary in (DOMAIN, APP):
    for path in boundary.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        for token in ("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity", "Npgsql"):
            if token in text:
                errors.append(f"{path.relative_to(ROOT)}: forbidden persistence/framework token: {token}")

context = INFRA / "Persistence" / "TaskFlowDbContext.cs"
require_tokens(
    context,
    "IdentityUserContext<ApplicationUser, Guid>",
    "IDataProtectionKeyContext",
    "DbSet<Project>",
    "DbSet<TaskItem>",
    "DbSet<Tag>",
    "DbSet<TaskTag>",
    "DbSet<DataProtectionKey>",
    "ApplyConfigurationsFromAssembly",
)

# Only one production DbContext owns the schema.
contexts = []
for path in SRC.rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    if re.search(r"\bclass\s+\w+DbContext\b", text) and "ModelSnapshot" not in text:
        contexts.append(path.relative_to(ROOT).as_posix())
if contexts != ["src/TaskFlow.Infrastructure/Persistence/TaskFlowDbContext.cs"]:
    errors.append(f"expected exactly one production DbContext, found: {contexts}")

mapping_requirements = {
    "Configurations/ProjectConfiguration.cs": (
        'ToTable("projects"',
        'HasColumnName("owner_user_id")',
        'HasConversion<string>()',
        'IsConcurrencyToken()',
        'ck_projects_status',
        'ix_projects_owner_user_id_status_created_at',
        'DeleteBehavior.Restrict',
    ),
    "Configurations/TaskItemConfiguration.cs": (
        'ToTable("task_items"',
        'HasColumnName("project_id")',
        'ck_task_items_status',
        'ck_task_items_priority',
        'ix_task_items_due_at',
        'IsConcurrencyToken()',
        'DeleteBehavior.Cascade',
    ),
    "Configurations/TagConfiguration.cs": (
        'ToTable("tags"',
        'HasColumnName("normalized_name")',
        'ux_tags_owner_user_id_normalized_name',
        'IsUnique()',
        'IsConcurrencyToken()',
        'DeleteBehavior.Restrict',
    ),
    "Configurations/TaskTagConfiguration.cs": (
        'ToTable("task_tags")',
        'new { taskTag.TaskId, taskTag.TagId }',
        'ix_task_tags_tag_id_task_id',
        'DeleteBehavior.Cascade',
    ),
    "Configurations/DataProtectionKeyConfiguration.cs": (
        'ToTable("data_protection_keys")',
        'HasColumnName("friendly_name")',
        'HasColumnName("xml")',
    ),
    "Configurations/Identity/ApplicationUserConfiguration.cs": (
        'ToTable("auth_users")',
        'HasColumnName("normalized_user_name")',
        'ux_auth_users_normalized_user_name',
        'HasColumnName("created_at")',
    ),
}
for relative, tokens in mapping_requirements.items():
    require_tokens(INFRA / "Persistence" / relative, *tokens)

migration = INFRA / "Persistence" / "Migrations" / "20260924170000_InitialCreate.cs"
migration_text = require(migration)
for table in (
    "auth_users",
    "data_protection_keys",
    "projects",
    "task_items",
    "tags",
    "task_tags",
):
    if f'name: "{table}"' not in migration_text:
        errors.append(f"initial migration missing table: {table}")
for constraint in (
    "ck_projects_status",
    "ck_projects_version",
    "ck_task_items_status",
    "ck_task_items_priority",
    "ck_task_items_version",
    "ck_tags_version",
    "ux_tags_owner_user_id_normalized_name",
):
    if constraint not in migration_text:
        errors.append(f"initial migration missing constraint/index: {constraint}")
if migration_text.count("ReferentialAction.Restrict") < 2:
    errors.append("initial migration must RESTRICT user -> project/tag deletes")
if migration_text.count("ReferentialAction.Cascade") < 5:
    errors.append("initial migration is missing expected cascade relationships")

snapshot = INFRA / "Persistence" / "Migrations" / "TaskFlowDbContextModelSnapshot.cs"
snapshot_text = require(snapshot)
if "new ProjectConfiguration" in snapshot_text or "ApplyConfigurationsFromAssembly" in snapshot_text:
    errors.append("model snapshot must be static; it must not execute live Fluent configuration classes")
for token in ("ProductVersion", "ConfigureProject", "ConfigureTaskItem", "ConfigureTag", "ConfigureTaskTag"):
    if token not in snapshot_text:
        errors.append(f"model snapshot missing static model token: {token}")

# Domain entities may expose private setters for EF materialization, but never public setters/EF attributes.
for path in (
    DOMAIN / "Projects" / "Project.cs",
    DOMAIN / "Tasks" / "TaskItem.cs",
    DOMAIN / "Tags" / "Tag.cs",
    DOMAIN / "TaskTags" / "TaskTag.cs",
):
    text = require(path)
    if "Microsoft.EntityFrameworkCore" in text or "[Key" in text or "[Column" in text:
        errors.append(f"{path.relative_to(ROOT)}: EF mapping metadata leaked into Domain")

# Locked restore graph must include the Stage 5 direct dependencies.
expected_versions = {
    "Microsoft.AspNetCore.DataProtection.EntityFrameworkCore": "10.0.12",
    "Microsoft.AspNetCore.Identity.EntityFrameworkCore": "10.0.12",
    "Microsoft.EntityFrameworkCore": "10.0.12",
    "Npgsql.EntityFrameworkCore.PostgreSQL": "10.0.3",
}
design_package = "Microsoft.EntityFrameworkCore.Design"
infra_lock_path = INFRA / "packages.lock.json"
try:
    infra_lock = json.loads(require(infra_lock_path))["dependencies"]["net10.0"]
except (json.JSONDecodeError, KeyError):
    errors.append("Infrastructure packages.lock.json is invalid")
    infra_lock = {}
for name, version in expected_versions.items():
    entry = infra_lock.get(name)
    if not entry or entry.get("resolved") != version or entry.get("type") != "Direct":
        errors.append(f"Infrastructure lock missing direct {name} {version}")
design_entry = infra_lock.get(design_package)
if not design_entry or design_entry.get("resolved") != "10.0.12" or design_entry.get("type") != "Direct":
    errors.append("Infrastructure lock missing private design-time EF package 10.0.12")

require_tokens(
    INFRA / "Persistence" / "Design" / "TaskFlowDbContextFactory.cs",
    "IDesignTimeDbContextFactory<TaskFlowDbContext>",
    "ConnectionStrings__Postgres",
    "UseNpgsql",
    "EnableSensitiveDataLogging(false)",
)

for lock_path in (
    SRC / "TaskFlow.Api" / "packages.lock.json",
    SRC / "TaskFlow.DbMigrator" / "packages.lock.json",
):
    try:
        deps = json.loads(require(lock_path))["dependencies"]["net10.0"]
    except (json.JSONDecodeError, KeyError):
        errors.append(f"invalid lock file: {lock_path.relative_to(ROOT)}")
        continue
    infra_project = next(
        (entry for package, entry in deps.items() if package.casefold() == "taskflow.infrastructure"),
        {},
    )
    infra_dependencies = {
        package.casefold()
        for package in infra_project.get("dependencies", {})
    }
    for name, version in expected_versions.items():
        if deps.get(name, {}).get("resolved") != version:
            errors.append(f"{lock_path.relative_to(ROOT)} missing transitive {name} {version}")
        if name.casefold() not in infra_dependencies:
            errors.append(f"{lock_path.relative_to(ROOT)} Infrastructure project dependency missing {name}")

integration_project = TESTS / "TaskFlow.IntegrationTests.csproj"
require_tokens(
    integration_project,
    'PackageReference Include="Npgsql"',
    'PackageReference Include="Testcontainers.PostgreSql"',
    'PackageReference Include="xunit.v3.mtp-v2"',
    'ProjectReference Include="../../src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj"',
)
fixture = TESTS / "Persistence" / "PostgresFixture.cs"
require_tokens(fixture, "PostgreSqlBuilder", "postgres:18-alpine", "Database.MigrateAsync()", "UseNpgsql")
integration_tests = TESTS / "Persistence" / "PostgresSchemaTests.cs"
require_tokens(
    integration_tests,
    "Migration_FromEmptyDatabase_CreatesExpectedTables",
    "TagName_IsUniquePerOwnerAfterNormalization",
    "DeletingProject_CascadesTasksAndRelations_ButKeepsTags",
    "DeletingTag_RemovesOnlyRelation_AndKeepsTask",
    "CheckConstraint_RejectsInvalidProjectStatus",
    "UserDelete_IsRestrictedWhileOwnedResourcesExist",
    "BusinessTimestamps_AreStoredAsTimestamptz",
    "IsConcurrencyToken",
)

try:
    integration_lock = json.loads(require(TESTS / "packages.lock.json"))["dependencies"]["net10.0"]
except (json.JSONDecodeError, KeyError):
    errors.append("Integration packages.lock.json is invalid")
    integration_lock = {}
for name, version in {
    "Npgsql": "10.0.3",
    "Testcontainers.PostgreSql": "4.15.0",
    "xunit.v3.mtp-v2": "4.0.0",
}.items():
    entry = integration_lock.get(name)
    if not entry or entry.get("resolved") != version or entry.get("type") != "Direct":
        errors.append(f"Integration lock missing direct {name} {version}")

if errors:
    print("Stage 5 Infrastructure contract verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 5 Infrastructure contract verification passed.")
