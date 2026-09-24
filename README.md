# TaskFlow — Stage 4

TaskFlow is implemented stage-by-stage from the architecture contract. **Stages 0–4 are complete in this snapshot**: repository/build foundation, framework-independent Domain, Application Core contracts, Project use cases, and the complete Tasks/Tags/TaskTag Application feature layer.

## What is ready

- pinned .NET **10.0.401** / C# 14 build contract;
- strict solution dependency direction and locked NuGet restore;
- Domain entities/invariants from Stage 1;
- typed Result/Error, pagination and owner-scoped persistence/query ports from Stage 2;
- all Project use cases from Stage 3;
- Tasks: `CreateTask`, `GetTask`, `ListTasks`, `UpdateTask`, `DeleteTask`, `AddTagToTask`, `RemoveTagFromTask`;
- Tags: `CreateTag`, `GetTag`, `ListTags`, `UpdateTag`, `DeleteTag`;
- one feature folder per use case with Command/Query + Validator + Handler;
- owner identity always comes from `ICurrentActor`, never from client request models;
- Task ownership is checked owner-scoped and is inherited through its Project persistence contract;
- Project mutations that affect Tasks/TaskTag run through `ITransactionManager` and Project/Task/Tag `GetOwnedForUpdateAsync` ports;
- archived Project blocks Task create/update/delete and TaskTag add/remove;
- Task and Tag must both be owner-scoped before a TaskTag relation is written;
- AddTag and RemoveTag are idempotent at the Application contract level;
- Tag uniqueness is normalized with `Trim().ToUpperInvariant()` and duplicate names return typed `Conflict`;
- update/delete Task and Tag commands carry the expected `Version` and return typed version conflict;
- injected `TimeProvider` is used for Task/Tag/TaskTag creation or mutation timestamps;
- no EF Core, ASP.NET Core, Npgsql, `HttpContext` or Infrastructure dependency in Application;
- unit tests plus architecture guards for Tasks, Tags and TaskTag relationships;
- Git history from Stages 0–4 preserved in `.git`.

## Verify Stage 4

From the repository root:

```bash
./scripts/verify-stage4.sh
```

Equivalent core commands:

```bash
python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
python3 scripts/verify_task_tag_features.py
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Expected result on a machine with the pinned SDK: locked restore succeeds, Release build has **0 warnings / 0 errors**, and all Domain/Application tests pass.

## Current boundary

All v1 Application use cases are now present. EF Core/PostgreSQL mappings, database constraints, real PostgreSQL row locks, database optimistic-concurrency enforcement, and unique-constraint race handling are intentionally left for Stages 5–6.

The Application checks in Stage 4 are **not** substitutes for database guarantees:

- the Project/Task/Tag `GetOwnedForUpdateAsync` methods are ports only; Stage 6 must implement the actual PostgreSQL lock behavior;
- Task/Tag expected-version checks catch stale commands already visible to the handler, while Stage 6 must still protect the read-to-write race with EF/PostgreSQL concurrency;
- Tag duplicate pre-check provides clean behavior in unit tests, while the `(owner_user_id, normalized_name)` unique constraint must remain the final race-safe authority in PostgreSQL.

## Verification status of this archive

Source/architecture checks, request-boundary checks, feature file checks, JSON/XML validation, Git consistency and archive integrity are executed while creating this snapshot. The generation container does not have the .NET SDK installed and external SDK download is unavailable, so final `dotnet restore/build/test` execution is not claimed here. `scripts/verify-stage4.sh` contains the exact reproducible verification sequence.

Detailed decisions: [`docs/STAGE_4_RATIONALE.md`](docs/STAGE_4_RATIONALE.md). Previous stage rationale files remain in `docs/`.
