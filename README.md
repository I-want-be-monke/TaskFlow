# TaskFlow — Stage 2

TaskFlow is being implemented stage-by-stage from the architecture contract. **Stages 0–2 are complete in this snapshot**: repository/build foundation, framework-independent Domain, and the Application Core contracts required before feature handlers are written.

## What is ready

- pinned .NET **10.0.401** / C# 14 build contract;
- strict solution dependency direction and locked NuGet restore;
- Domain entities/invariants from Stage 1;
- typed `Result`, `Result<T>`, `Error`, `ErrorCode`, `ErrorType`;
- all seven architectural application error categories;
- shared pagination with `PageSize <= 100`;
- `PagedResult<T>`;
- `ICurrentActor`, `IUnitOfWork`, `ITransactionManager`;
- owner-scoped Project/Task/Tag repository ports;
- separate owner-scoped read/query ports;
- Project/Task/Tag read models;
- `TaskSearchQuery` with documented filters and a fixed sort whitelist;
- no `IQueryable` exposed by Application;
- no generic repository;
- real xUnit v3 Application tests plus architecture guards;
- Git history from Stages 0–2 preserved in `.git`.

## Important Stage 2 rules

Application still depends only on Domain. It has no EF Core, ASP.NET Core, Npgsql, `HttpContext` or Infrastructure references.

User-resource persistence interfaces deliberately expose `GetOwnedByIdAsync(ownerUserId, id, ct)` instead of an unsafe unscoped `GetById(id)`. `OwnerUserId` is not accepted by `TaskSearchQuery`; future handlers obtain it from `ICurrentActor`.

Read paths return read models/`PagedResult<T>` rather than Domain entities or `IQueryable`.

## Verify Stage 2

From the repository root:

```bash
./scripts/verify-stage2.sh
```

Equivalent core commands:

```bash
python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Expected result: locked restore succeeds, Release build has **0 warnings / 0 errors**, and all Domain/Application tests pass.

## Current boundary

No feature handlers, EF Core, PostgreSQL repositories, HTTP endpoints or auth implementation have been added yet. Stage 3 can now implement Project use cases entirely against these Application contracts.

## Verification status of this archive

Source/architecture checks, JSON/XML validation, Git consistency and archive integrity are executed while creating this snapshot. The generation container does not have the .NET SDK installed and cannot resolve external hosts directly, so the final `dotnet restore/build/test` execution cannot be claimed here. The pinned SDK remains `10.0.401`, and `scripts/verify-stage2.sh` contains the exact reproducible verification sequence.

Detailed decisions: [`docs/STAGE_2_RATIONALE.md`](docs/STAGE_2_RATIONALE.md). Previous stages: [`docs/STAGE_0_RATIONALE.md`](docs/STAGE_0_RATIONALE.md), [`docs/STAGE_1_RATIONALE.md`](docs/STAGE_1_RATIONALE.md).
