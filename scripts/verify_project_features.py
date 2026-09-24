#!/usr/bin/env python3
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
PROJECTS = ROOT / "src" / "TaskFlow.Application" / "Projects"

FEATURES = {
    "CreateProject": ("CreateProjectCommand.cs", "CreateProjectValidator.cs", "CreateProjectHandler.cs"),
    "GetProject": ("GetProjectQuery.cs", "GetProjectValidator.cs", "GetProjectHandler.cs"),
    "ListProjects": ("ListProjectsQuery.cs", "ListProjectsValidator.cs", "ListProjectsHandler.cs"),
    "UpdateProject": ("UpdateProjectCommand.cs", "UpdateProjectValidator.cs", "UpdateProjectHandler.cs"),
    "ArchiveProject": ("ArchiveProjectCommand.cs", "ArchiveProjectValidator.cs", "ArchiveProjectHandler.cs"),
    "RestoreProject": ("RestoreProjectCommand.cs", "RestoreProjectValidator.cs", "RestoreProjectHandler.cs"),
    "DeleteProject": ("DeleteProjectCommand.cs", "DeleteProjectValidator.cs", "DeleteProjectHandler.cs"),
}

errors: list[str] = []

for feature, files in FEATURES.items():
    folder = PROJECTS / feature
    for filename in files:
        path = folder / filename
        if not path.is_file():
            errors.append(f"missing Stage 3 file: {path.relative_to(ROOT)}")

request_files = [
    PROJECTS / "CreateProject" / "CreateProjectCommand.cs",
    PROJECTS / "GetProject" / "GetProjectQuery.cs",
    PROJECTS / "ListProjects" / "ListProjectsQuery.cs",
    PROJECTS / "UpdateProject" / "UpdateProjectCommand.cs",
    PROJECTS / "ArchiveProject" / "ArchiveProjectCommand.cs",
    PROJECTS / "RestoreProject" / "RestoreProjectCommand.cs",
    PROJECTS / "DeleteProject" / "DeleteProjectCommand.cs",
]
for path in request_files:
    text = path.read_text(encoding="utf-8")
    if re.search(r"\bOwnerUserId\b", text):
        errors.append(f"{path.relative_to(ROOT)}: OwnerUserId must come from ICurrentActor, not the request")

for feature in ("UpdateProject", "ArchiveProject", "RestoreProject", "DeleteProject"):
    path = PROJECTS / feature / f"{feature}Command.cs"
    text = path.read_text(encoding="utf-8")
    if not re.search(r"\blong\s+Version\b", text):
        errors.append(f"{path.relative_to(ROOT)}: mutating command must carry expected Version")

for feature in FEATURES:
    path = PROJECTS / feature / f"{feature}Handler.cs"
    text = path.read_text(encoding="utf-8")
    if "ICurrentActor" not in text:
        errors.append(f"{path.relative_to(ROOT)}: handler must use ICurrentActor")
    if "IsAuthenticated" not in text:
        errors.append(f"{path.relative_to(ROOT)}: handler must enforce authenticated actor")
    if "CancellationToken" not in text:
        errors.append(f"{path.relative_to(ROOT)}: handler must accept CancellationToken")
    if re.search(r"DateTime(?:Offset)?\.(?:UtcNow|Now)", text):
        errors.append(f"{path.relative_to(ROOT)}: direct system time access is forbidden")
    if "GetByIdAsync(" in text:
        errors.append(f"{path.relative_to(ROOT)}: unscoped GetByIdAsync is forbidden")

for feature in ("ArchiveProject", "RestoreProject"):
    path = PROJECTS / feature / f"{feature}Handler.cs"
    text = path.read_text(encoding="utf-8")
    for required in ("ITransactionManager", "ExecuteAsync", "GetOwnedForUpdateAsync"):
        if required not in text:
            errors.append(f"{path.relative_to(ROOT)}: missing transaction/lock contract token: {required}")

for feature in ("UpdateProject", "ArchiveProject", "RestoreProject", "DeleteProject"):
    path = PROJECTS / feature / f"{feature}Handler.cs"
    text = path.read_text(encoding="utf-8")
    if ".Version != command.Version" not in text:
        errors.append(f"{path.relative_to(ROOT)}: expected-version pre-check is missing")
    if "VersionConflict" not in text:
        errors.append(f"{path.relative_to(ROOT)}: version conflict must return typed Conflict")

create_handler = (PROJECTS / "CreateProject" / "CreateProjectHandler.cs").read_text(encoding="utf-8")
if "currentActor.UserId" not in create_handler:
    errors.append("CreateProjectHandler must derive OwnerUserId from ICurrentActor")

for feature in ("GetProject", "ListProjects", "UpdateProject", "ArchiveProject", "RestoreProject", "DeleteProject"):
    path = PROJECTS / feature / f"{feature}Handler.cs"
    text = path.read_text(encoding="utf-8")
    if "currentActor.UserId" not in text:
        errors.append(f"{path.relative_to(ROOT)}: owner-scoped access must use currentActor.UserId")

for feature in ("CreateProject", "UpdateProject", "ArchiveProject", "RestoreProject", "DeleteProject"):
    path = PROJECTS / feature / f"{feature}Handler.cs"
    text = path.read_text(encoding="utf-8")
    if "IUnitOfWork" not in text or "SaveChangesAsync" not in text:
        errors.append(f"{path.relative_to(ROOT)}: mutating handler must save through IUnitOfWork")

if errors:
    print("Stage 3 Project feature verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 3 Project feature verification passed.")
