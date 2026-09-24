#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INFRA = ROOT / "src" / "TaskFlow.Infrastructure"
APP = ROOT / "src" / "TaskFlow.Application"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Persistence"
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


require_tokens(
    APP / "Common" / "Abstractions" / "IUnitOfWork.cs",
    "Task<Result> SaveChangesAsync",
)
require_tokens(
    APP / "Common" / "Errors" / "PersistenceErrors.cs",
    "persistence.concurrency_conflict",
    "tags.duplicate_name",
    "persistence.database_unavailable",
)

require_tokens(
    INFRA / "Persistence" / "Interceptors" / "VersionConcurrencyInterceptor.cs",
    "SaveChangesInterceptor",
    "EntityState.Modified",
    'entry.Property("Version")',
    "originalVersion + 1",
)
require_tokens(
    INFRA / "Persistence" / "TaskFlowDbContext.cs",
    "VersionConcurrencyInterceptor.Instance",
    "AddInterceptors",
)
require_tokens(
    INFRA / "Persistence" / "UnitOfWork.cs",
    "DbUpdateConcurrencyException",
    "PostgresErrorCodes.UniqueViolation",
    "ux_tags_owner_user_id_normalized_name",
    "PersistenceErrors.ConcurrencyConflict()",
    "PersistenceErrors.DuplicateTagName()",
)
require_tokens(
    INFRA / "Persistence" / "Transactions" / "EfTransactionManager.cs",
    "BeginTransactionAsync",
    "IsolationLevel.ReadCommitted",
    "CommitAsync",
    "RollbackAsync",
)

repo_requirements = {
    "Repositories/ProjectRepository.cs": (
        "IProjectRepository",
        "owner_user_id",
        "FOR UPDATE",
        "CurrentTransaction",
    ),
    "Repositories/TaskRepository.cs": (
        "ITaskRepository",
        "project.owner_user_id",
        "FOR UPDATE OF task_item",
        "GetTagRelationAsync",
        "CurrentTransaction",
    ),
    "Repositories/TagRepository.cs": (
        "ITagRepository",
        "owner_user_id",
        "FOR UPDATE",
        "ExistsOwnedByNormalizedNameAsync",
        "CurrentTransaction",
    ),
}
for relative, tokens in repo_requirements.items():
    require_tokens(INFRA / relative, *tokens)

query_requirements = {
    "Persistence/Queries/ProjectQueries.cs": (
        "IProjectQueries",
        "AsNoTracking()",
        "Select(project => new ProjectReadModel",
        "LongCountAsync",
    ),
    "Persistence/Queries/TaskQueries.cs": (
        "ITaskQueries",
        "AsNoTracking()",
        "TaskSortOptions",
        "EF.Functions.ILike",
        "TaskTags.Any",
        "LongCountAsync",
    ),
    "Persistence/Queries/TagQueries.cs": (
        "ITagQueries",
        "AsNoTracking()",
        "Select(tag => new TagReadModel",
        "LongCountAsync",
    ),
}
for relative, tokens in query_requirements.items():
    require_tokens(INFRA / relative, *tokens)

# IQueryable may exist internally, but never in public Application contracts.
for path in (APP / "Common" / "Abstractions").glob("*.cs"):
    if "IQueryable" in path.read_text(encoding="utf-8"):
        errors.append(f"{path.relative_to(ROOT)}: IQueryable escaped Infrastructure")

# Every application write handler must propagate the typed persistence result.
write_handlers = list(APP.rglob("*Handler.cs"))
write_handlers = [p for p in write_handlers if "unitOfWork" in p.read_text(encoding="utf-8")]
if len(write_handlers) != 13:
    errors.append(f"expected 13 write handlers using IUnitOfWork, found {len(write_handlers)}")
for path in write_handlers:
    text = path.read_text(encoding="utf-8")
    if "Result saveResult = await unitOfWork.SaveChangesAsync" not in text:
        errors.append(f"{path.relative_to(ROOT)}: write result is not captured")
    if "saveResult.IsFailure" not in text:
        errors.append(f"{path.relative_to(ROOT)}: persistence failure is not propagated")

# Lock order in task mutations must remain Project -> Task -> Tag.
for feature in ("AddTagToTask", "RemoveTagFromTask"):
    path = APP / "Tasks" / feature / f"{feature}Handler.cs"
    text = require(path)
    project_pos = text.find("projectRepository.GetOwnedForUpdateAsync")
    task_pos = text.find("taskRepository.GetOwnedForUpdateAsync")
    tag_pos = text.find("tagRepository.GetOwnedForUpdateAsync")
    if not (0 <= project_pos < task_pos < tag_pos):
        errors.append(f"{path.relative_to(ROOT)}: canonical lock order Project -> Task -> Tag is broken")

for feature in ("CreateTask", "UpdateTask", "DeleteTask"):
    path = APP / "Tasks" / feature / f"{feature}Handler.cs"
    text = require(path)
    if "projectRepository.GetOwnedForUpdateAsync" not in text:
        errors.append(f"{path.relative_to(ROOT)}: missing Project row lock")

# PostgreSQL integration contract.
require_tokens(
    TESTS / "RepositoryAndQueryTests.cs",
    "WriteRepositories_AreOwnerScoped",
    "ForUpdateRepositories_RequireExplicitTransaction",
    "QueryObjects_AreOwnerScopedAndNoTracking",
    "TaskSearch_AppliesFiltersSortingAndPagination",
    "Assert.Empty(dbContext.ChangeTracker.Entries())",
)
require_tokens(
    TESTS / "ConcurrencyTests.cs",
    "VersionInterceptor_IncrementsAllMutableAggregateVersions",
    "ConcurrentUpdatesWithSameVersion_ProduceOneSuccessAndOneConflict",
    "ConcurrentNormalizedTagCreates_ProduceOneSuccessAndOneConflict",
    "ArchiveRace_SerializesProjectDependentMutations",
    "ArchiveProjectHandler",
    "ProjectMutation.CreateTask",
    "ProjectMutation.UpdateTask",
    "ProjectMutation.DeleteTask",
    "ProjectMutation.AddTag",
    "ProjectMutation.RemoveTag",
    "CanonicalLockOrder_CompletesWithoutDeadlock",
    "AsyncBarrier",
    "TaskCompletionSource",
)

# No Task.Delay-only synchronization in concurrency tests.
concurrency_text = require(TESTS / "ConcurrencyTests.cs")
if "Task.Delay" in concurrency_text:
    errors.append("ConcurrencyTests.cs must use explicit synchronization barriers, not Task.Delay")

# Infrastructure must not reference the API layer.
infra_project = require(INFRA / "TaskFlow.Infrastructure.csproj")
if "TaskFlow.Api" in infra_project:
    errors.append("Infrastructure must not reference Api")

# Basic source-level guard that all FOR UPDATE SQL remains parameterized/interpolated.
for path in (INFRA / "Repositories").glob("*.cs"):
    text = path.read_text(encoding="utf-8")
    if "FOR UPDATE" in text and "FromSqlInterpolated" not in text:
        errors.append(f"{path.relative_to(ROOT)}: FOR UPDATE must use parameterized FromSqlInterpolated")

if errors:
    print("Stage 6 Infrastructure contract verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 6 Infrastructure contract verification passed.")
