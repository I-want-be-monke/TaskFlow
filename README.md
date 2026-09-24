# TaskFlow — Stage 5

TaskFlow is implemented stage-by-stage from the architecture contract. **Stages 0–5 are complete in this snapshot**: repository/build foundation, Domain, Application Core, all v1 Application use cases, and now the PostgreSQL/EF Core persistence model with the initial migration and real PostgreSQL integration-test contract.

## What is ready

- pinned .NET **10.0.401** / C# 14 build contract;
- locked NuGet restore and preserved dependency direction;
- all Domain/Application work from Stages 1–4;
- EF Core **10.0.12** and Npgsql EF provider **10.0.3** only at the Infrastructure boundary;
- one production `TaskFlowDbContext` owning Identity + business + Data Protection schema;
- `ApplicationUser : IdentityUser<Guid>` without Identity leakage into Domain/Application;
- explicit Fluent mappings for every entity; no EF attributes in Domain;
- PostgreSQL `snake_case` table/column names and `timestamptz` timestamps;
- enum-to-string mappings for Project/Task status and Task priority;
- business check constraints, FK delete behavior and required indexes;
- unique `(owner_user_id, normalized_name)` constraint for Tags;
- `Version` mapped as an EF concurrency token on Project, TaskItem and Tag;
- shared Data Protection key table `data_protection_keys`;
- initial migration `20260924170000_InitialCreate` and a static model snapshot;
- design-time `TaskFlowDbContextFactory` for future local `dotnet ef` commands;
- PostgreSQL Testcontainers integration tests using `postgres:18-alpine`;
- Git history from Stages 0–5 preserved in `.git`.

## Verify Stage 5

Prerequisites:

```text
.NET SDK 10.0.401
Docker Engine / Docker Desktop
```

From the repository root:

```bash
./scripts/verify-stage5.sh
```

The script performs:

```text
Stage 0–5 architecture/source checks
-> dotnet restore TaskFlow.sln --locked-mode
-> Release build
-> Docker availability check
-> all unit + PostgreSQL integration tests
```

Equivalent core commands:

```bash
python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
python3 scripts/verify_task_tag_features.py
python3 scripts/verify_infrastructure_stage5.py

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Expected result on a machine with the pinned SDK and Docker: locked restore succeeds, Release build has **0 warnings / 0 errors**, unit tests pass, Testcontainers starts a clean PostgreSQL 18 instance, the initial migration applies successfully, and the schema/integrity integration tests pass.

## Local EF migration commands

The design-time factory reads the same architecture-standard environment key as runtime configuration:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=taskflow;Username=taskflow_migrator;Password=change-me'
```

Then future migrations can be created from the repository root with:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/TaskFlow.Infrastructure \
  --context TaskFlowDbContext \
  --output-dir Persistence/Migrations
```

Do **not** add `Database.Migrate()` to API startup. Production migration execution remains the responsibility of the separate `TaskFlow.DbMigrator` implemented at Stage 11.

## Current boundary

Stage 5 defines and tests the database model, but it intentionally does **not** implement the persistence ports yet. Stage 6 still owns:

- repositories and read-query implementations;
- `AsNoTracking` projections;
- `IUnitOfWork` / `ITransactionManager` implementations;
- real PostgreSQL `SELECT ... FOR UPDATE` behavior;
- `Version` increment policy and `DbUpdateConcurrencyException` mapping;
- unique-constraint race mapping to typed `Conflict`;
- concurrency/deadlock integration tests.

## Verification status of this archive

All available source/architecture checks, lock-graph consistency checks, JSON/XML parsing, Git checks and archive integrity checks are executed while creating this snapshot. The artifact-generation container does not contain the .NET SDK or Docker, so this response does **not** claim that `dotnet restore/build/test` or the Testcontainers suite ran inside that container. `scripts/verify-stage5.sh` is the reproducible full verification command for a normal development machine.

Detailed decisions: [`docs/STAGE_5_RATIONALE.md`](docs/STAGE_5_RATIONALE.md). Previous stage rationale files remain in `docs/`.
