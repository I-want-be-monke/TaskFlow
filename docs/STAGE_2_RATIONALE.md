# Stage 2 — Application Core implementation rationale

## Goal

Stage 2 defines the stable boundary between business rules and outer infrastructure. `TaskFlow.Application` still depends only on `TaskFlow.Domain`; it does not know about EF Core, Npgsql, ASP.NET Core, `HttpContext`, controllers, cookies or PostgreSQL.

The stage deliberately implements contracts rather than use-case handlers. Project handlers start at Stage 3 and Task/Tag handlers at Stage 4, so those features can be written against interfaces that are already explicit, testable and owner-scoped.

## Structure

```text
TaskFlow.Application/
├─ Common/
│  ├─ Abstractions/
│  │  ├─ ICurrentActor.cs
│  │  ├─ IUnitOfWork.cs
│  │  ├─ ITransactionManager.cs
│  │  ├─ IProjectRepository.cs
│  │  ├─ ITaskRepository.cs
│  │  ├─ ITagRepository.cs
│  │  ├─ IProjectQueries.cs
│  │  ├─ ITaskQueries.cs
│  │  └─ ITagQueries.cs
│  ├─ Errors/
│  │  ├─ Error.cs
│  │  ├─ ErrorCode.cs
│  │  ├─ ErrorType.cs
│  │  └─ ApplicationErrors.cs
│  ├─ Pagination/
│  │  ├─ Pagination.cs
│  │  └─ PagedResult.cs
│  └─ Results/
│     └─ Result.cs
├─ Projects/
│  └─ ProjectReadModel.cs
├─ Tasks/
│  ├─ TaskReadModel.cs
│  ├─ TaskSearchQuery.cs
│  └─ TaskSortOptions.cs
└─ Tags/
   └─ TagReadModel.cs
```

## Typed Result and Error contract

Expected application failures are data, not exceptions. `Result` and `Result<T>` represent success/failure explicitly; a failed result carries an `Error` containing a stable `ErrorCode`, an `ErrorType`, and a safe message.

The seven error categories are exactly the architecture contract:

- `Validation`;
- `Unauthenticated`;
- `Forbidden`;
- `NotFound`;
- `Conflict`;
- `ForbiddenByState`;
- `InfrastructureFailure`.

Stage 7 can therefore map error categories to HTTP status codes without handlers depending on HTTP. Stable string error codes such as `pagination.invalid_page` and `tasks.invalid_sort` allow API/UI code to react to a specific failure without parsing human text.

Unexpected programmer/runtime failures are not converted into `Result`; they remain exceptions and will be handled once at the future API exception boundary.

## Pagination

`Pagination` owns the common `Page >= 1` and `1 <= PageSize <= 100` rules. `PagedResult<T>` carries items plus `Page`, `PageSize`, `TotalCount`, and computes `TotalPages` using integer arithmetic.

The maximum page size is a shared Application constant so future handlers and Infrastructure query implementations use one rule instead of duplicating magic numbers.

## Current actor boundary

`ICurrentActor` exposes only:

```text
IsAuthenticated
UserId
```

Application never receives `HttpContext`, claims collections, Identity users or cookies. The future API adapter will translate the authenticated request into this small port.

Critically, owner identifiers are not part of client query/request models. Handlers will take the current owner from `ICurrentActor`, preventing callers from choosing another user's owner ID.

## Repository ports

Write repositories work with Domain entities. Their user-resource lookup operations are owner-scoped from the interface itself:

```text
GetOwnedByIdAsync(ownerUserId, resourceId, ct)
```

There is deliberately no public `GetByIdAsync(id)` for Projects, Tasks or Tags. This makes the unsafe access pattern harder to introduce later and prepares the BOLA/object-authorization contract before EF Core exists.

`IProjectRepository` additionally declares `GetOwnedForUpdateAsync`. The implementation is deferred to Stage 6, where PostgreSQL row locking is introduced for the Project.Active cross-aggregate invariant.

No generic `IRepository<T>` exists because the aggregates have different ownership, locking and query semantics.

## Read/query ports

Read paths are separate from write repositories. `IProjectQueries`, `ITaskQueries` and `ITagQueries` return projection-oriented read models and `PagedResult<T>` rather than Domain entities.

`IQueryable` never crosses the Application boundary. Infrastructure will later own `AsNoTracking`, filtering, ordering and SQL translation internally. This prevents Application code from accidentally depending on EF Core behavior.

## Read models

The Stage 2 read models expose only fields needed by the future application/API contract. They do not contain `OwnerUserId`, `CreatedAt`, `UpdatedAt`, password hashes or security stamps. Domain entities themselves are not used as transport models.

The models are C# records because they are value-like data carriers rather than entities with identity-based mutation behavior.

## TaskSearchQuery and sort whitelist

`TaskSearchQuery` implements the architecture fields:

```text
ProjectId?
Status?
Priority?
TagId?
DueBefore?
DueAfter?
SearchText?
Page
PageSize
Sort
```

Validation rejects invalid pagination, empty optional GUIDs, unknown enum values and sort values outside a fixed whitelist. The whitelist is represented by `TaskSortOptions`; Infrastructure will map those symbolic values to explicit LINQ ordering expressions rather than dynamically evaluating arbitrary client strings.

The default is `createdAt:desc`, which is compatible with the planned owner/status/created-at Project/Task listing strategy while keeping the public input constrained.

## Unit of work and transactions

`IUnitOfWork.SaveChangesAsync(CancellationToken)` is the persistence commit boundary. It intentionally does not expose EF Core's `DbContext` or change tracker.

`ITransactionManager.ExecuteAsync<T>` represents the future atomic command boundary. Stage 6 will implement it against PostgreSQL/EF Core. Keeping it as a port lets Stage 3/4 handlers express transactional business operations without importing Infrastructure.

## Cancellation

Every async persistence/query port carries a `CancellationToken`. This is part of the architecture's graceful-shutdown and request-cancellation contract from the beginning rather than being retrofitted later.

## Tests and architecture guards

`TaskFlow.Application.Tests` is now a real xUnit v3/Microsoft Testing Platform project. Tests cover:

- generic and non-generic Result success/failure behavior;
- all seven ErrorType categories;
- ErrorCode validation;
- pagination boundaries and page-count calculation;
- TaskSearchQuery documented filters;
- Page/PageSize validation;
- sort whitelist;
- invalid enum/empty GUID validation;
- Application assembly forbidden dependency references;
- absence of public `IQueryable` contracts;
- absence of unscoped user-resource `GetByIdAsync` ports;
- owner ID as the first argument of owned lookups;
- absence of server-controlled fields in client-facing Application models.

`scripts/verify_application_contracts.py` provides a source-level guard that can run even before the .NET SDK is available, while the reflection tests provide the stronger compiled-assembly check when `dotnet test` runs.

## Deliberately deferred

Stage 2 does not implement:

- Project/Task/Tag handlers and validators;
- EF Core repositories or query implementations;
- PostgreSQL transactions/row locks;
- optimistic concurrency persistence behavior;
- HTTP DTOs/controllers/endpoints;
- authentication adapters;
- ProblemDetails mapping.

Those belong to Stages 3–8 and now have stable Application contracts to build on.
