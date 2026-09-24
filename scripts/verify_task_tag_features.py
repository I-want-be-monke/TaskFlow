#!/usr/bin/env python3
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "TaskFlow.Application"
TASKS = APP / "Tasks"
TAGS = APP / "Tags"

TASK_FEATURES = {
    "CreateTask": ("CreateTaskCommand.cs", "CreateTaskValidator.cs", "CreateTaskHandler.cs"),
    "GetTask": ("GetTaskQuery.cs", "GetTaskValidator.cs", "GetTaskHandler.cs"),
    "ListTasks": ("ListTasksQuery.cs", "ListTasksValidator.cs", "ListTasksHandler.cs"),
    "UpdateTask": ("UpdateTaskCommand.cs", "UpdateTaskValidator.cs", "UpdateTaskHandler.cs"),
    "DeleteTask": ("DeleteTaskCommand.cs", "DeleteTaskValidator.cs", "DeleteTaskHandler.cs"),
    "AddTagToTask": ("AddTagToTaskCommand.cs", "AddTagToTaskValidator.cs", "AddTagToTaskHandler.cs"),
    "RemoveTagFromTask": ("RemoveTagFromTaskCommand.cs", "RemoveTagFromTaskValidator.cs", "RemoveTagFromTaskHandler.cs"),
}

TAG_FEATURES = {
    "CreateTag": ("CreateTagCommand.cs", "CreateTagValidator.cs", "CreateTagHandler.cs"),
    "GetTag": ("GetTagQuery.cs", "GetTagValidator.cs", "GetTagHandler.cs"),
    "ListTags": ("ListTagsQuery.cs", "ListTagsValidator.cs", "ListTagsHandler.cs"),
    "UpdateTag": ("UpdateTagCommand.cs", "UpdateTagValidator.cs", "UpdateTagHandler.cs"),
    "DeleteTag": ("DeleteTagCommand.cs", "DeleteTagValidator.cs", "DeleteTagHandler.cs"),
}

errors: list[str] = []

for root, features in ((TASKS, TASK_FEATURES), (TAGS, TAG_FEATURES)):
    for feature, files in features.items():
        folder = root / feature
        for filename in files:
            path = folder / filename
            if not path.is_file():
                errors.append(f"missing Stage 4 file: {path.relative_to(ROOT)}")

request_files = []
for root, features in ((TASKS, TASK_FEATURES), (TAGS, TAG_FEATURES)):
    for feature, files in features.items():
        request_files.append(root / feature / files[0])

for path in request_files:
    text = path.read_text(encoding="utf-8")
    if re.search(r"\bOwnerUserId\b", text):
        errors.append(f"{path.relative_to(ROOT)}: OwnerUserId must come from ICurrentActor")

for path in (
    TASKS / "UpdateTask" / "UpdateTaskCommand.cs",
    TASKS / "DeleteTask" / "DeleteTaskCommand.cs",
    TAGS / "UpdateTag" / "UpdateTagCommand.cs",
    TAGS / "DeleteTag" / "DeleteTagCommand.cs",
):
    if not re.search(r"\blong\s+Version\b", path.read_text(encoding="utf-8")):
        errors.append(f"{path.relative_to(ROOT)}: expected Version is required")

for root, features in ((TASKS, TASK_FEATURES), (TAGS, TAG_FEATURES)):
    for feature in features:
        handler = root / feature / f"{feature}Handler.cs"
        text = handler.read_text(encoding="utf-8")
        if "ICurrentActor" not in text:
            errors.append(f"{handler.relative_to(ROOT)}: handler must use ICurrentActor")
        if "IsAuthenticated" not in text:
            errors.append(f"{handler.relative_to(ROOT)}: handler must enforce authentication")
        if "currentActor.UserId" not in text:
            errors.append(f"{handler.relative_to(ROOT)}: owner-scoped access must use currentActor.UserId")
        if "CancellationToken" not in text:
            errors.append(f"{handler.relative_to(ROOT)}: handler must accept CancellationToken")
        if re.search(r"DateTime(?:Offset)?\.(?:UtcNow|Now)", text):
            errors.append(f"{handler.relative_to(ROOT)}: direct system time access is forbidden")
        if "GetByIdAsync(" in text:
            errors.append(f"{handler.relative_to(ROOT)}: unscoped GetByIdAsync is forbidden")

for feature in ("CreateTask", "UpdateTask", "DeleteTask", "AddTagToTask", "RemoveTagFromTask"):
    handler = TASKS / feature / f"{feature}Handler.cs"
    text = handler.read_text(encoding="utf-8")
    for required in ("ITransactionManager", "ExecuteAsync", "IProjectRepository", "GetOwnedForUpdateAsync"):
        if required not in text:
            errors.append(f"{handler.relative_to(ROOT)}: missing transaction/project lock contract token: {required}")
    if "ProjectStatus.Archived" not in text or "ProjectArchived" not in text:
        errors.append(f"{handler.relative_to(ROOT)}: archived Project must block Task/TaskTag mutation")

for feature in ("UpdateTask", "DeleteTask"):
    handler = TASKS / feature / f"{feature}Handler.cs"
    text = handler.read_text(encoding="utf-8")
    if ".Version != command.Version" not in text or "VersionConflict" not in text:
        errors.append(f"{handler.relative_to(ROOT)}: typed task version conflict is required")

for feature in ("UpdateTag", "DeleteTag"):
    handler = TAGS / feature / f"{feature}Handler.cs"
    text = handler.read_text(encoding="utf-8")
    if ".Version != command.Version" not in text or "VersionConflict" not in text:
        errors.append(f"{handler.relative_to(ROOT)}: typed tag version conflict is required")

for feature in ("AddTagToTask", "RemoveTagFromTask"):
    handler = TASKS / feature / f"{feature}Handler.cs"
    text = handler.read_text(encoding="utf-8")
    for required in ("ITaskRepository", "ITagRepository", "GetOwnedForUpdateAsync", "GetTagRelationAsync"):
        if required not in text:
            errors.append(f"{handler.relative_to(ROOT)}: TaskTag owner/lock contract missing token: {required}")

create_tag = (TAGS / "CreateTag" / "CreateTagHandler.cs").read_text(encoding="utf-8")
update_tag = (TAGS / "UpdateTag" / "UpdateTagHandler.cs").read_text(encoding="utf-8")
for name, text in (("CreateTagHandler", create_tag), ("UpdateTagHandler", update_tag)):
    if "ExistsOwnedByNormalizedNameAsync" not in text or "DuplicateName" not in text:
        errors.append(f"{name}: duplicate normalized tag name must map to Conflict")

add_tag = (TASKS / "AddTagToTask" / "AddTagToTaskHandler.cs").read_text(encoding="utf-8")
if "existingRelation is not null" not in add_tag or "Result.Success()" not in add_tag:
    errors.append("AddTagToTaskHandler: repeated PUT semantics must be idempotent")

repo_task = (APP / "Common" / "Abstractions" / "ITaskRepository.cs").read_text(encoding="utf-8")
repo_tag = (APP / "Common" / "Abstractions" / "ITagRepository.cs").read_text(encoding="utf-8")
for token in ("GetOwnedForUpdateAsync", "GetTagRelationAsync", "AddTagAsync", "RemoveTag"):
    if token not in repo_task:
        errors.append(f"ITaskRepository missing Stage 4 contract: {token}")
for token in ("GetOwnedForUpdateAsync", "ExistsOwnedByNormalizedNameAsync"):
    if token not in repo_tag:
        errors.append(f"ITagRepository missing Stage 4 contract: {token}")

if errors:
    print("Stage 4 Tasks/Tags feature verification failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    raise SystemExit(1)

print("Stage 4 Tasks/Tags feature verification passed.")
