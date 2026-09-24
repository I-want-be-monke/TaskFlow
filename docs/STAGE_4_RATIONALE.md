# Stage 4 — Tasks, Tags and TaskTag Application features

## 1. Goal

Stage 4 completes the v1 Application use-case layer before persistence is implemented. The result must still compile conceptually over Domain only: no EF Core, Npgsql, ASP.NET Core, `HttpContext`, controllers or database-specific code is introduced here.

The implemented use cases are:

```text
Tasks
- CreateTask
- GetTask
- ListTasks
- UpdateTask
- DeleteTask
- AddTagToTask
- RemoveTagFromTask

Tags
- CreateTag
- GetTag
- ListTags
- UpdateTag
- DeleteTag
```

Each use case has a dedicated feature directory and a small explicit contract: Command/Query, Validator and Handler.

## 2. Why Task mutation handlers use transactions already

The architecture requires the cross-aggregate invariant:

> an archived Project must reject Task and TaskTag mutations atomically.

Therefore `CreateTask`, `UpdateTask`, `DeleteTask`, `AddTagToTask` and `RemoveTagFromTask` already depend on `ITransactionManager` and obtain the owning Project through `GetOwnedForUpdateAsync`.

At Stage 4 these are only Application ports. No claim is made that a real row lock exists yet. Stage 6 must map `GetOwnedForUpdateAsync` to PostgreSQL locking.

Keeping the transaction boundary in Application now is important because it makes the business operation explicit before Infrastructure exists. Stage 6 can then implement the port without redesigning handlers.

## 3. Lock-order contract prepared for Stage 6

The intended database lock order remains:

```text
1. Project
2. TaskItem
3. Tag / TaskTag relation
```

For commands addressed only by `taskId`, the handler first performs an owner-scoped non-locking Task lookup to discover the immutable `ProjectId`. Inside the transaction it then:

```text
lock owned Project
-> verify Project is Active
-> lock/reload owned Task
-> if TaskTag mutation: lock/reload owned Tag
-> read/write TaskTag relation
-> SaveChanges
```

The preliminary Task read does not establish correctness by itself. Correctness is based on the re-read inside the transaction after the Project lock. Since `TaskItem.ProjectId` is immutable in the Domain model, the Project identity discovered before the transaction cannot be reassigned by a normal update.

## 4. Ownership and BOLA boundary

No Task/Tag request model accepts `OwnerUserId`.

Handlers always use:

```text
currentActor.UserId
```

Repository/query operations are owner-scoped. A missing resource and a resource owned by another user are intentionally indistinguishable to the use case and become `NotFound`.

For TaskTag mutation, both sides are checked independently:

```text
owned Task
+ owned Tag
-> relation may be changed
```

This prevents constructing a relation by combining a user's Task with another user's Tag.

## 5. Archived Project behavior

The following commands all check Project state inside the transaction boundary:

```text
CreateTask
UpdateTask
DeleteTask
AddTagToTask
RemoveTagFromTask
```

If the Project is `Archived`, the handler returns a typed `ForbiddenByState` error with code:

```text
tasks.project_archived
```

No write is sent to `IUnitOfWork` in that path.

## 6. Task optimistic-concurrency contract

`UpdateTaskCommand` and `DeleteTaskCommand` carry an expected `Version`.

Stage 4 performs an application-level stale-version check after the Task is reloaded for update:

```text
if task.Version != command.Version
    -> Conflict
```

This gives the use case a stable typed error contract now. It does not close the race between the version check and SQL UPDATE. Stage 6 must configure `Version` as an EF concurrency token and convert the real database write race to the same logical `Conflict`.

## 7. Tag normalized uniqueness

The Domain already defines tag normalization:

```text
Trim().ToUpperInvariant()
```

Stage 4 uses the same normalization before checking:

```text
ExistsOwnedByNormalizedNameAsync(ownerUserId, normalizedName, excludingTagId, ct)
```

For create, `excludingTagId = null`. For update, the current Tag ID is excluded so renaming a tag without changing its normalized value does not conflict with itself.

A detected duplicate returns:

```text
ErrorType.Conflict
tags.duplicate_name
```

This pre-check exists for a clean Application contract and unit-testable behavior. It is not race-safe by itself. Stage 5 must create the unique PostgreSQL constraint `(owner_user_id, normalized_name)` and Stage 6 must map a concurrent unique-constraint violation into the same clean Conflict result.

## 8. Tag versioning

`UpdateTagCommand` and `DeleteTagCommand` also carry expected `Version`. Their handlers perform the same Stage-4 pre-check as Task handlers.

Again, database-level lost-update protection is deferred to Stage 6, where EF/PostgreSQL becomes available.

## 9. TaskTag persistence contract

No new generic repository or ORM abstraction was introduced. TaskTag persistence remains adjacent to the Task persistence boundary through explicit methods on `ITaskRepository`:

```text
GetTagRelationAsync
AddTagAsync
RemoveTag
```

This choice keeps the Stage-2 decision of explicit resource repositories and avoids introducing `IRepository<T>` just for a join entity.

`GetTagRelationAsync` remains owner-scoped so even relation lookup cannot become an unscoped resource path.

## 10. Idempotency

`PUT /tasks/{taskId}/tags/{tagId}` is specified as idempotent in the architecture. Therefore `AddTagToTask` returns success without writing when the relation already exists.

`RemoveTagFromTask` is also implemented as a no-op success when the relation is already absent. This aligns with normal idempotent DELETE semantics and makes retries safe at the Application level.

Stage 6 still needs the composite `(task_id, tag_id)` primary key to make duplicate insertion impossible under concurrency.

## 11. Read paths

`GetTask`, `ListTasks`, `GetTag` and `ListTags` use query ports rather than write repositories.

The Application layer never receives `IQueryable`. The future Infrastructure implementation is expected to perform:

```text
owner-scoped predicate
-> AsNoTracking
-> filter/order
-> projection to read model
-> pagination
```

`ListTasks` delegates filtering rules to the existing `TaskSearchQuery`, including bounded pagination and sort whitelist.

## 12. Time handling

No Stage-4 handler reads `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly.

`TimeProvider` is injected into handlers that create/change timestamps:

```text
CreateTask
UpdateTask
AddTagToTask
CreateTag
UpdateTag
```

This keeps the use cases deterministic in tests and preserves the Domain rule that time is supplied from outside.

## 13. Cancellation

All async ports end in `CancellationToken`. Stage-4 handlers propagate the request token to the preliminary owner-scoped lookup, transaction manager, locked reads, uniqueness checks and unit-of-work save.

This preserves the graceful-shutdown contract established by the architecture.

## 14. Tests added

Stage 4 adds behavior tests for:

```text
Task create/get/list/update/delete
Tag create/get/list/update/delete
TaskTag add/remove
validation failures
unauthenticated actor
foreign-owned Task -> NotFound
foreign-owned Tag -> NotFound
archived Project blocks all Task/TaskTag mutations
Task version mismatch -> Conflict
Tag version mismatch -> Conflict
duplicate normalized Tag -> Conflict
TaskTag cross-owner rejection
AddTag idempotency
RemoveTag idempotency
CancellationToken propagation on representative paths
```

Architecture tests additionally check that:

```text
Task/Tag request contracts contain no OwnerUserId
versioned mutations contain Version
Task mutation handlers require transaction + Project/Task ports
TaskTag handlers require ITagRepository
for-update/relation/uniqueness lookups are owner-scoped
```

No mocking library was added; small explicit in-memory test doubles keep the package graph unchanged.

## 15. What Stage 4 intentionally does not implement

The following belong to later stages and are not silently simulated here:

```text
EF Core DbContext and Fluent mappings
PostgreSQL schema and migrations
real SELECT ... FOR UPDATE behavior
Version increment / EF concurrency token
DbUpdateConcurrencyException mapping
unique-constraint violation mapping
TaskTag composite PK enforcement
AsNoTracking SQL projections
HTTP DTO/controllers
Identity/cookie/CSRF
```

## 16. Verification

Run:

```bash
./scripts/verify-stage4.sh
```

The script performs all Stage 0–4 static architecture checks, verifies forbidden Application references and `IQueryable` leakage, then executes locked restore, Release build and all tests when the pinned .NET SDK is available.

The artifact-generation environment used for this snapshot has no .NET SDK and cannot download it, so the source checks can be executed here but runtime `dotnet restore/build/test` is not reported as having run.

## 17. Resulting handoff to Stage 5

After Stage 4 the business/use-case surface for v1 is complete. Stage 5 can now focus exclusively on persistence implementation:

```text
ApplicationUser
TaskFlowDbContext
Identity + business + Data Protection schema
Project/Task/Tag/TaskTag mappings
indexes/check constraints/delete behaviors
Version concurrency-token mapping
initial migration
PostgreSQL integration tests
```

No HTTP layer is needed to begin Stage 5.
