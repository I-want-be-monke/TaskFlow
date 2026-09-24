# Stage 3 — Project Features implementation rationale

## Goal

Stage 3 implements the complete Project use-case layer on top of the Domain and Application Core created in Stages 1–2. It deliberately stops before EF Core and HTTP: every Project operation is testable through ports, typed results and Domain entities without a database, controller, `HttpContext` or web server.

The implemented use cases are:

```text
CreateProject
GetProject
ListProjects
UpdateProject
ArchiveProject
RestoreProject
DeleteProject
```

Each feature folder contains its request (`Command` or `Query`), `Validator`, and `Handler`.

## Feature structure

```text
TaskFlow.Application/Projects/
├─ Common/
│  ├─ ProjectErrors.cs
│  ├─ ProjectMapping.cs
│  └─ ProjectValidation.cs
├─ CreateProject/
│  ├─ CreateProjectCommand.cs
│  ├─ CreateProjectValidator.cs
│  └─ CreateProjectHandler.cs
├─ GetProject/
├─ ListProjects/
├─ UpdateProject/
├─ ArchiveProject/
├─ RestoreProject/
└─ DeleteProject/
```

The small `Common` helpers only remove duplication shared by Project features; they do not introduce a generic application framework.

## Handler execution order

Handlers follow the architecture order:

1. validate the command/query;
2. verify the current actor is authenticated;
3. obtain `currentActor.UserId`;
4. perform only owner-scoped persistence/query access;
5. execute the Domain method for mutations;
6. save through `IUnitOfWork` and, where required, `ITransactionManager`;
7. return a typed `Result`.

Validation runs before authentication so malformed requests are rejected without touching persistence. Authentication runs before any repository/query call.

## Ownership and BOLA prevention

No Project command or query contains `OwnerUserId`. `CreateProjectHandler` takes the owner exclusively from `ICurrentActor`; all lookup handlers pass the actor's ID to owner-scoped ports.

A missing object and an object owned by another user are intentionally indistinguishable at the Application boundary: both become `projects.not_found`. This preserves the later API rule that foreign-owned resources return 404 rather than exposing their existence with a distinct 403.

## CreateProject

`CreateProjectCommand` accepts only name and description. The handler creates a new GUID internally, derives the owner from `ICurrentActor`, obtains time from injected `TimeProvider`, and calls `Project.Create`.

The Domain remains the final invariant boundary. Application validation mirrors the public length rules to return a typed validation result instead of relying on expected Domain exceptions.

## GetProject and ListProjects

Read use cases use `IProjectQueries`, not the write repository. This preserves the Stage 2 read/write boundary so Infrastructure can later implement `AsNoTracking + projection` without loading aggregates.

`ListProjectsQuery` converts to the shared `Pagination` value and therefore inherits the centralized `Page >= 1` and `1 <= PageSize <= 100` rules.

## UpdateProject

`UpdateProjectCommand` carries the expected `Version`. The handler loads the aggregate owner-scoped, checks the expected version, calls `Project.UpdateDetails`, then saves through `IUnitOfWork`.

The Stage 3 version comparison is an early application conflict check. It is not the final concurrency guarantee: Stage 6 still must configure `Version` as the EF concurrency token and ensure the database update succeeds only when the expected version matches. That closes the race between load and save.

## ArchiveProject and RestoreProject

Archive/restore are already expressed through `ITransactionManager` and `IProjectRepository.GetOwnedForUpdateAsync` because the architecture requires a Project row lock for these operations. At Stage 3 those are ports and test doubles; Stage 6 will implement the transaction and PostgreSQL `FOR UPDATE` semantics.

Expected state conflicts are checked before invoking Domain methods:

- archiving an already archived Project -> `ForbiddenByState`;
- restoring an already active Project -> `ForbiddenByState`.

This keeps expected business conflicts in typed results rather than exception-driven flow.

## DeleteProject

Delete is a hard delete as defined by v1. The handler owner-scopes the lookup, checks expected version, calls `IProjectRepository.Remove`, and commits through `IUnitOfWork`.

No soft-delete flag or hidden archive-on-delete behavior is introduced.

## Time

Mutating handlers that change timestamps receive `TimeProvider`. They never call `DateTime.UtcNow`, `DateTimeOffset.UtcNow` or local system time directly. This keeps time deterministic in tests and preserves the Domain rule that mutation methods receive `now` as an argument.

## Error contract

Project feature errors use stable codes:

```text
auth.unauthenticated
projects.invalid_id
projects.invalid_name
projects.invalid_description
projects.invalid_version
projects.not_found
projects.version_conflict
projects.already_archived
projects.already_active
```

The error category remains the architecture-level semantic contract (`Validation`, `Unauthenticated`, `NotFound`, `Conflict`, `ForbiddenByState`). Stage 7 will translate categories to HTTP without changing handlers.

## Tests

Stage 3 uses hand-written in-memory test doubles instead of a mocking framework. This avoids adding another dependency and makes owner scoping, transaction usage and cancellation propagation explicit.

Tests cover the required Project handler matrix:

- happy paths;
- validation failures;
- unauthenticated actor;
- not found;
- foreign-owned resource -> same `NotFound` result;
- expected-version conflicts;
- archive/restore state conflicts;
- owner derived only from `ICurrentActor`;
- `CancellationToken` propagation;
- transaction/for-update port usage for archive/restore;
- no owner field in Project commands/queries.

## What remains deliberately deferred

Stage 3 does not implement:

- Tasks/Tags/TaskTag handlers (Stage 4);
- EF Core mappings and PostgreSQL schema (Stage 5);
- real repositories, `FOR UPDATE`, database transactions and final optimistic concurrency handling (Stage 6);
- HTTP endpoints/ProblemDetails (Stage 7);
- Identity/cookie auth/CSRF (Stage 8).

This is intentional: Project use cases are complete at the Application layer, while the outer adapters remain replaceable and testable independently.
