# TaskFlow — Stage 1

TaskFlow is implemented incrementally according to the architecture contract. **Stage 0 and Stage 1 are complete in this repository snapshot**: the reproducible repository/build foundation plus a framework-independent Domain model with unit tests.

## What is ready

- .NET SDK pinned to **10.0.401** via `global.json`;
- 6 production projects and 3 test projects with architecture-safe references;
- `Nullable`, warnings-as-errors, code-style enforcement and .NET analyzers;
- Central Package Management and committed NuGet lock files;
- Microsoft Testing Platform selected for .NET 10 test execution;
- Domain model: `Project`, `TaskItem`, `Tag`, `TaskTag`;
- enums: `ProjectStatus`, `TaskStatus`, `TaskPriority`;
- aggregate invariants expressed in code, not comments;
- deterministic timestamps: Domain receives `DateTimeOffset now` from the caller and never reads the system clock;
- `Version = 1` on new mutable aggregate roots, ready for optimistic concurrency in the persistence stages;
- xUnit v3 Domain unit tests, including framework-independence verification;
- Git history preserved from Stage 0 plus Stage 1 commits.

## Stage 1 domain rules implemented

`Project` starts as `Active`, validates name/description lengths, supports archive/restore and does not expose owner mutation. `TaskItem` validates project identity, title/description boundaries, known status/priority values and allows direct transitions between all known task statuses. `Tag` trims its name, enforces the 64-character limit after trimming and computes `NormalizedName` with `ToUpperInvariant()`. `TaskTag` preserves the `(TaskId, TagId)` relation identity and creation time.

Cross-aggregate rules that require Application/DB coordination are intentionally **not** implemented inside entities. In particular, blocking Task/TaskTag mutations for an archived Project belongs to later Application/transaction stages.

## Verify Stage 1

From the repository root:

```bash
./scripts/verify-stage1.sh
```

Equivalent commands:

```bash
python3 scripts/verify_project_references.py
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Expected result: locked restore succeeds, build has **0 warnings / 0 errors**, and all Domain tests pass.

## Current stage boundary

EF Core, ASP.NET Core Identity, Npgsql, repositories, HTTP endpoints and cross-aggregate archived-project checks are deliberately absent from `TaskFlow.Domain`. They are introduced only in the later stages defined by the architecture.

## Verification status of this archive

Static repository, XML/JSON, dependency-boundary and source-level checks were executed while creating the archive. The generation environment does not contain the .NET SDK, so the final `dotnet restore/build/test` commands could not be executed here. The repository includes the exact commands needed to run them with the pinned SDK.

For the original repository/build decisions see [`docs/STAGE_0_RATIONALE.md`](docs/STAGE_0_RATIONALE.md). For Stage 1 design decisions see [`docs/STAGE_1_RATIONALE.md`](docs/STAGE_1_RATIONALE.md).
