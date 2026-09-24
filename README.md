# TaskFlow — Stage 3

TaskFlow is being implemented stage-by-stage from the architecture contract. **Stages 0–3 are complete in this snapshot**: repository/build foundation, framework-independent Domain, Application Core contracts, and the complete Project use-case layer.

## What is ready

- pinned .NET **10.0.401** / C# 14 build contract;
- strict solution dependency direction and locked NuGet restore;
- Domain entities/invariants from Stage 1;
- typed Result/Error, pagination and owner-scoped persistence ports from Stage 2;
- `CreateProject`, `GetProject`, `ListProjects`, `UpdateProject`, `ArchiveProject`, `RestoreProject`, `DeleteProject`;
- one feature folder per use case with Command/Query + Validator + Handler;
- owner is always derived from `ICurrentActor`, never from a client request;
- foreign-owned Project lookup is indistinguishable from missing -> `NotFound`;
- expected `Version` on update/archive/restore/delete with typed conflict result;
- archive/restore expressed through transaction + `GetOwnedForUpdateAsync` ports;
- injected `TimeProvider` for Project mutations;
- no EF Core, ASP.NET Core, Npgsql, `HttpContext` or Infrastructure dependency in Application;
- Project handler unit tests and architecture guards;
- Git history from Stages 0–3 preserved in `.git`.

## Verify Stage 3

From the repository root:

```bash
./scripts/verify-stage3.sh
```

Equivalent core commands:

```bash
python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Expected result: locked restore succeeds, Release build has **0 warnings / 0 errors**, and all Domain/Application tests pass.

## Current boundary

Project use cases are complete at the Application layer. Tasks/Tags/TaskTag features, EF Core/PostgreSQL, real row locks/transactions, HTTP endpoints and auth are intentionally not implemented yet.

The application-level version check added in Stage 3 is not a substitute for database optimistic concurrency. Stage 6 must still configure the `Version` concurrency token and map real write races to `Conflict`.

## Verification status of this archive

Source/architecture checks, JSON/XML validation, Git consistency and archive integrity are executed while creating this snapshot. The generation container does not have the .NET SDK installed and cannot resolve external hosts directly, so the final `dotnet restore/build/test` execution cannot be claimed here. `scripts/verify-stage3.sh` contains the exact reproducible verification sequence for a machine with the pinned SDK.

Detailed decisions: [`docs/STAGE_3_RATIONALE.md`](docs/STAGE_3_RATIONALE.md). Previous stages remain documented in `docs/STAGE_0_RATIONALE.md`, `docs/STAGE_1_RATIONALE.md`, and `docs/STAGE_2_RATIONALE.md`.
