#!/usr/bin/env python3
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "TaskFlow.Application"

errors: list[str] = []

for path in APP.rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    relative = path.relative_to(ROOT)
    forbidden_tokens = (
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "TaskFlow.Infrastructure",
        "TaskFlow.Api",
        "HttpContext",
    )
    for token in forbidden_tokens:
        if token in text:
            errors.append(f"{relative}: forbidden Application dependency token: {token}")

    if re.search(r"\bIQueryable(?:\s*<|\b)", text):
        errors.append(f"{relative}: IQueryable must not escape Infrastructure")

for name in ("IProjectRepository.cs", "ITaskRepository.cs", "ITagRepository.cs"):
    path = APP / "Common" / "Abstractions" / name
    text = path.read_text(encoding="utf-8")
    if re.search(r"\bGetByIdAsync\s*\(", text):
        errors.append(f"{path.relative_to(ROOT)}: unscoped GetByIdAsync is forbidden")
    if "GetOwnedByIdAsync" not in text or "Guid ownerUserId" not in text:
        errors.append(f"{path.relative_to(ROOT)}: owner-scoped lookup contract is missing")

for name in ("IProjectQueries.cs", "ITaskQueries.cs", "ITagQueries.cs"):
    path = APP / "Common" / "Abstractions" / name
    text = path.read_text(encoding="utf-8")
    if re.search(r"\bGetByIdAsync\s*\(", text):
        errors.append(f"{path.relative_to(ROOT)}: unscoped query lookup is forbidden")
    if "GetOwnedByIdAsync" not in text or "Guid ownerUserId" not in text:
        errors.append(f"{path.relative_to(ROOT)}: owner-scoped query contract is missing")

model_files = [
    APP / "Projects" / "ProjectReadModel.cs",
    APP / "Tasks" / "TaskReadModel.cs",
    APP / "Tags" / "TagReadModel.cs",
    APP / "Tasks" / "TaskSearchQuery.cs",
]
for path in model_files:
    text = path.read_text(encoding="utf-8")
    for property_name in ("OwnerUserId", "CreatedAt", "UpdatedAt", "PasswordHash", "SecurityStamp"):
        if re.search(rf"\b{property_name}\b", text):
            errors.append(f"{path.relative_to(ROOT)}: server-controlled field exposed: {property_name}")

query_text = (APP / "Tasks" / "TaskSearchQuery.cs").read_text(encoding="utf-8")
for required in (
    "Guid? ProjectId",
    "DomainTaskStatus? Status",
    "TaskPriority? Priority",
    "Guid? TagId",
    "DateTimeOffset? DueBefore",
    "DateTimeOffset? DueAfter",
    "string? SearchText",
    "int Page",
    "int PageSize",
    "string Sort",
):
    if required not in query_text:
        errors.append(f"TaskSearchQuery is missing documented field: {required}")

error_type_text = (APP / "Common" / "Errors" / "ErrorType.cs").read_text(encoding="utf-8")
for category in (
    "Validation",
    "Unauthenticated",
    "Forbidden",
    "NotFound",
    "Conflict",
    "ForbiddenByState",
    "InfrastructureFailure",
):
    if category not in error_type_text:
        errors.append(f"ErrorType is missing category: {category}")

if errors:
    print("Stage 2 Application contract verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 2 Application contract verification passed.")
