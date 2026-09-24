# Stage 5 — PostgreSQL and EF Core persistence model

## 1. Goal

Stage 5 introduces the first framework-specific persistence code while preserving the boundaries established in Stages 0–4.

The architecture requires this stage to deliver:

```text
ApplicationUser
one TaskFlowDbContext
Identity schema
business DbSets
Data Protection key store
Fluent configurations
indexes / check constraints / FK delete behavior
enum-to-string conversion
Version concurrency-token mapping
initial migration
PostgreSQL integration tests
```

It does **not** implement repositories, query objects, row locks or transaction/concurrency error mapping. Those remain Stage 6 concerns.

## 2. Package choices

The persistence stack is centralized in `Directory.Packages.props` and referenced only where it belongs.

```text
Microsoft.EntityFrameworkCore                         10.0.12
Microsoft.EntityFrameworkCore.Design                  10.0.12 (PrivateAssets=all)
Microsoft.AspNetCore.Identity.EntityFrameworkCore     10.0.12
Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 10.0.12
Npgsql.EntityFrameworkCore.PostgreSQL                 10.0.3
Npgsql                                                10.0.3 (integration tests)
Testcontainers.PostgreSql                             4.15.0 (integration tests)
```

The EF design package is private tooling. It supports local `dotnet ef` work but does not become an API/runtime transitive dependency.

No EF/Npgsql reference was added to Domain or Application.

## 3. Why there is one DbContext

`TaskFlowDbContext` is the single production owner of the v1 schema:

```text
Identity users + technical user tables
business tables
Data Protection keys
migration history
```

It derives from:

```csharp
IdentityUserContext<ApplicationUser, Guid>
```

and implements:

```csharp
IDataProtectionKeyContext
```

This follows the architecture decision to avoid separate Identity/business contexts and separate migration histories in v1. One context gives one transactional schema boundary and one migration sequence.

`IdentityUserContext` is used instead of role-oriented Identity contexts because v1 authorization is ownership-based and the architecture does not introduce application roles.

## 4. Identity stays outside Domain/Application

`ApplicationUser` lives in Infrastructure and derives from `IdentityUser<Guid>`.

The Domain still stores only business-owned identifiers such as:

```text
Project.OwnerUserId
Tag.OwnerUserId
```

No Domain entity knows about `IdentityUser`, cookies, claims, `UserManager`, `HttpContext` or EF Core.

That keeps authentication persistence as an adapter rather than turning Identity into the business model.

## 5. Why Domain got private setters

EF needs a materialization path for persisted state. The server-controlled immutable properties received `private set`:

```text
Id
OwnerUserId / ProjectId
CreatedAt
TaskTag keys
```

This does not expose mutation to Application callers. Public ownership and identity reassignment remain impossible, while EF can materialize entities without mapping attributes or public mutable DTO-style entities.

Domain invariants remain enforced by the public factory/mutation methods.

## 6. Fluent mapping only

All database mapping is located under:

```text
TaskFlow.Infrastructure/Persistence/Configurations
```

Domain classes contain no `[Key]`, `[Column]`, `[Table]`, `[Timestamp]` or provider-specific attributes.

This preserves the architecture rule:

```text
Domain -> no EF Core / ASP.NET Core / Npgsql
```

and keeps PostgreSQL naming, lengths, conversions and relationships replaceable from the Infrastructure side.

## 7. PostgreSQL naming and types

Every important table and column name is explicit. The business schema uses `snake_case`:

```text
projects.owner_user_id
projects.created_at
task_items.project_id
task_items.due_at
tags.normalized_name
task_tags.task_id
task_tags.tag_id
```

Time values are mapped to:

```text
timestamp with time zone
```

which is PostgreSQL `timestamptz`.

The mapping does not depend on a global naming-convention package. That is deliberate: schema names are part of the architecture contract and are visible directly in each configuration and migration.

## 8. Identity schema

The main user table is:

```text
auth_users
```

with a UUID primary key and the required Identity fields. `user_name` and `normalized_user_name` use the architecture limit of 64 characters, and normalized username has a unique index.

Because the chosen base context supports user claims/logins/tokens, the technical tables are also explicitly renamed:

```text
auth_user_claims
auth_user_logins
auth_user_tokens
```

Domain/Application never reference these tables.

## 9. Shared Data Protection key store

`TaskFlowDbContext` implements `IDataProtectionKeyContext` and maps `DataProtectionKey` to:

```text
data_protection_keys
```

This is required by the later multi-replica cookie/antiforgery design. The actual Data Protection service registration is deferred until the authentication stage, but the persistent shared store exists in the schema now so replicas will not depend on container-local key files.

## 10. Business constraints are duplicated intentionally

Domain validation remains the first line of business correctness, but the database also enforces storage-level invariants.

Examples:

```text
Project name is non-empty
Project status is Active/Archived
Task status is Todo/InProgress/Done
Task priority is Low/Medium/High
updated_at >= created_at
Version >= 1
Tag name is non-empty
```

These constraints are defense in depth. They protect the schema from invalid writes performed outside normal Domain factories, including admin SQL, bugs, older application versions or future adapters.

The integration suite also performs a raw invalid Project insert and verifies PostgreSQL rejects it with the expected check constraint.

## 11. Enum-to-string mapping

The business enums are stored as strings:

```text
ProjectStatus -> varchar(16)
TaskStatus    -> varchar(32)
TaskPriority  -> varchar(16)
```

Strings make the database easier to inspect and avoid coupling persisted meaning to numeric enum ordinals.

The corresponding PostgreSQL check constraints ensure arbitrary strings cannot be written.

## 12. Optimistic concurrency mapping

`Project`, `TaskItem` and `Tag` already carry `Version` from the Domain model.

Stage 5 marks each `Version` property as:

```text
IsConcurrencyToken()
```

and the schema enforces:

```text
version >= 1
```

This establishes the EF metadata needed for lost-update protection.

Stage 5 intentionally does **not** invent the increment/error mapping behavior. Stage 6 must implement the write path that increments the version and maps `DbUpdateConcurrencyException` to the existing typed Application `Conflict` contract.

## 13. Tag uniqueness

The final race-safe uniqueness authority is PostgreSQL:

```text
UNIQUE (owner_user_id, normalized_name)
```

The Stage 4 pre-check remains useful for normal behavior, but two concurrent create/update requests can still race. The unique index closes that race at the database layer.

Stage 5 integration tests prove the database rejects two equivalent normalized names for the same owner.

Stage 6 will map that provider exception to the clean `tags.duplicate_name` conflict instead of exposing a database exception.

## 14. Foreign keys and delete behavior

The schema follows the architecture exactly:

```text
auth_users -> projects     RESTRICT
auth_users -> tags         RESTRICT
projects   -> task_items   CASCADE
task_items -> task_tags    CASCADE
tags       -> task_tags    CASCADE
```

Consequences:

- deleting a Project deletes its Tasks and TaskTag links;
- Tags survive Project deletion;
- deleting a Tag removes only its TaskTag links;
- deleting a user who still owns Projects/Tags is rejected;
- account deletion remains outside the public v1 contract.

Integration tests exercise these behaviors against PostgreSQL rather than inferring them from Fluent configuration text.

## 15. Indexes

The initial migration creates the architecture indexes:

```text
projects(owner_user_id, status, created_at DESC)
task_items(project_id)
task_items(project_id, status)
task_items(project_id, priority)
task_items(due_at) WHERE due_at IS NOT NULL
tags(owner_user_id, normalized_name) UNIQUE
tags(owner_user_id, name)
task_tags(tag_id, task_id)
```

The partial due-date index avoids indexing rows without a due date.

The composite Project index supports the expected owner-scoped status/list access pattern and preserves descending created-time order in the index definition.

## 16. Initial migration and model snapshot

The first migration is:

```text
20260924170000_InitialCreate
```

It builds the Identity, Data Protection and business schema from an empty database.

The model snapshot is **static and self-contained**. It does not call the live `IEntityTypeConfiguration` classes. That detail matters: if a snapshot executed current configurations dynamically, changing a configuration could silently change both the current model and the historical snapshot, preventing EF from detecting a migration difference.

Future migrations should update the snapshot normally through `dotnet ef`.

## 17. Design-time factory

`TaskFlowDbContextFactory` implements:

```text
IDesignTimeDbContextFactory<TaskFlowDbContext>
```

It reads:

```text
ConnectionStrings__Postgres
```

and configures Npgsql without sensitive-data logging.

This lets a developer create later migrations before the API composition root is fully implemented.

The factory is design-time only; it is not an alternate runtime configuration system.

## 18. Locked NuGet restore after adding Infrastructure packages

Adding persistence packages changes not only `TaskFlow.Infrastructure/packages.lock.json`. Projects referencing Infrastructure also see its non-private package dependencies.

Therefore Stage 5 updates the locked graph for:

```text
TaskFlow.Infrastructure
TaskFlow.Api
TaskFlow.DbMigrator
TaskFlow.IntegrationTests
```

The EF design package remains private to Infrastructure and is not propagated to Api/DbMigrator.

This preserves the Stage 0 requirement that a clean checkout can use:

```bash
dotnet restore --locked-mode
```

without silently resolving a different package graph.

## 19. Why Testcontainers and real PostgreSQL

The architecture explicitly forbids replacing persistence integration tests with SQLite.

`TaskFlow.IntegrationTests` therefore uses:

```text
Testcontainers.PostgreSql 4.15.0
postgres:18-alpine
```

A test fixture starts an empty PostgreSQL container, creates a real `TaskFlowDbContext`, and calls `Database.MigrateAsync()` only inside the test/admin context.

This verifies PostgreSQL-specific behavior that SQLite cannot faithfully represent:

```text
timestamptz
partial indexes
PostgreSQL check/FK behavior
Npgsql provider mappings
real unique constraint errors
migration SQL
```

PostgreSQL 18 is selected as the current supported major for this snapshot; production can later pin an exact image digest during the containerization stage.

## 20. Integration tests in Stage 5

The Stage 5 suite checks:

```text
migration from an empty database
expected schema tables
EF table/column snake_case mapping
Version concurrency tokens
required indexes
required check constraints
Tag normalized uniqueness
Project -> Task/TaskTag cascade
Tag -> TaskTag cascade while Task survives
User -> owned resources RESTRICT
actual invalid-status check rejection
timestamptz storage
```

These tests are intentionally persistence-focused. Repository query semantics and concurrency races belong to Stage 6.

## 21. API migrations are still forbidden

The presence of EF migrations does not mean the API should apply them.

The architecture remains:

```text
API startup       -> no Migrate / EnsureCreated
DbMigrator        -> production migration process (Stage 11)
Integration tests -> may call MigrateAsync on disposable test DB
local developer   -> may use dotnet ef
```

This keeps admin work separate from the runtime web process and preserves the 12-factor admin-process requirement.

## 22. What Stage 5 intentionally does not implement

The following are deferred to Stage 6 or later:

```text
ProjectRepository / TaskRepository / TagRepository
ProjectQueries / TaskQueries / TagQueries
IUnitOfWork implementation
ITransactionManager implementation
SELECT ... FOR UPDATE
canonical database lock order implementation
Version increment interceptor/write policy
DbUpdateConcurrencyException -> Conflict mapping
unique violation -> duplicate Tag Conflict mapping
AsNoTracking read projections
filters/sorting/pagination SQL
HTTP endpoints
Identity login/cookie/CSRF pipeline
API startup DI registration
production DbMigrator executable behavior
```

Keeping these out of Stage 5 prevents the persistence schema task from turning into an accidental implementation of several later stages.

## 23. Verification

Run:

```bash
./scripts/verify-stage5.sh
```

It performs Stage 0–5 architecture checks, locked restore, Release build and all tests. The PostgreSQL integration suite requires Docker.

The snapshot-generation environment used here does not contain the .NET SDK or Docker, so only source/architecture/lock/serialization/Git checks can be executed in that environment. No claim is made that `dotnet restore`, compilation or Testcontainers ran there.

## 24. Handoff to Stage 6

Stage 6 can now implement Application persistence ports against a stable, migration-backed PostgreSQL model:

```text
repositories
query projections
unit of work
transaction manager
Project row locks
Version increments
concurrency exception mapping
unique-race mapping
PostgreSQL concurrency tests
```

The schema contract should not need redesign for those implementations.
