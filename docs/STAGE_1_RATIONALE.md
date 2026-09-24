# Stage 1 — Domain implementation rationale

## Goal

Stage 1 turns the empty `TaskFlow.Domain` boundary from Stage 0 into the complete v1 business model required before Application use cases are written. The central constraint is that Domain stays framework-independent: no EF Core, ASP.NET Core, Identity, Npgsql, `HttpContext`, repositories or infrastructure services.

## Structure

```text
TaskFlow.Domain/
├─ Common/
│  └─ DomainGuard.cs
├─ Projects/
│  ├─ Project.cs
│  └─ ProjectStatus.cs
├─ Tasks/
│  ├─ TaskItem.cs
│  ├─ TaskStatus.cs
│  └─ TaskPriority.cs
├─ Tags/
│  └─ Tag.cs
└─ TaskTags/
   └─ TaskTag.cs
```

The folders follow business concepts rather than persistence concerns. This keeps the Domain vocabulary aligned with the later feature folders in Application while avoiding EF-centric organization.

## Why entities use private mutation

Mutable business fields have private setters and are changed through explicit methods such as `UpdateDetails`, `Archive`, `Restore`, `Update` and `Rename`. Identity and ownership properties are not publicly mutable. This prevents external code from bypassing entity invariants through property assignment.

The Domain does not expose a method for changing `OwnerUserId`. Ownership transfer is therefore impossible through the normal domain API, matching the architecture contract.

## Project

A new Project is always created as `ProjectStatus.Active`. `Name` is limited to 1..120 characters and `Description` to 2000 characters. Archive and restore are explicit transitions instead of a generic status setter.

Repeated archive of an already archived project and repeated restore of an already active project are rejected as invalid state transitions. The Application layer can decide later whether an endpoint should translate such state conflicts to a typed `Conflict` result.

The rule “an archived project blocks Task/TaskTag mutations” is intentionally not implemented inside `Project`, because it is a cross-aggregate invariant. The architecture explicitly places its atomic enforcement in Application + PostgreSQL transaction/row-lock logic at later stages.

## TaskItem

`TaskItem` owns only task-local invariants: non-empty Project identity, title 1..200, description up to 4000, and known `TaskStatus`/`TaskPriority` values. The Domain allows direct transitions among all defined task statuses because v1 explicitly allows them.

Task ownership is not duplicated on the entity. It is inherited from Project and will be enforced through owner-scoped queries/repositories later.

## Tag

Tag naming is the one place where the architecture explicitly requires normalization. Input is trimmed first; the trimmed value must be 1..64 characters; `NormalizedName` is then computed with `ToUpperInvariant()`.

Uniqueness by `(OwnerUserId, NormalizedName)` cannot be guaranteed by one isolated entity. It will be enforced by PostgreSQL and mapped to an Application conflict in the persistence stages.

## TaskTag

`TaskTag` contains exactly the relation identity required by the architecture: `TaskId`, `TagId`, `CreatedAt`. There is no surrogate ID and no concurrency version. Ownership/cross-owner validation belongs to the Application layer because it requires loading both Task and Tag in owner scope.

## Version strategy

`Project`, `TaskItem` and `Tag` start with `Version = 1`. Domain methods do not increment Version. The architecture assigns lost-update protection and version incrementing to Infrastructure/EF Core, where the concurrency token and actual database update happen atomically.

This avoids pretending that an in-memory entity mutation is the same thing as a successfully committed database version.

## Time strategy

Domain never calls `DateTime.UtcNow` or `DateTimeOffset.UtcNow`. Factories and mutating methods receive `DateTimeOffset now` explicitly. `CreatedAt` and `UpdatedAt` are therefore deterministic and testable without introducing an infrastructure clock dependency into Domain.

The guard rejects update timestamps earlier than `CreatedAt`, matching the database contract that `updated_at >= created_at`.

## Validation and exceptions

Stage 1 does not introduce the Application `Result/Error` contract prematurely; that contract belongs to Stage 2. Domain constructors/methods therefore use standard argument exceptions for invalid values and `InvalidOperationException` for invalid state transitions.

Application validators in later stages will reject expected client errors before invoking the Domain. Domain checks remain the final invariant boundary so invalid state cannot be constructed by trusted server code either.

## Why no EF-friendly attributes or persistence types exist

No `[Key]`, `[Column]`, `[Timestamp]`, EF base class, `DbContext`, `IdentityUser`, `Npgsql` type or persistence interface appears in Domain. Mapping is deferred to Infrastructure Fluent Configuration in Stage 5.

This preserves the allowed dependency direction and makes the Domain unit-testable without database/framework packages.

## Tests

`TaskFlow.Domain.Tests` now uses xUnit v3 with Microsoft Testing Platform for .NET 10. Tests cover:

- Project name/description boundaries;
- Project archive/restore transitions;
- immutable owner API surface;
- Task title/description boundaries;
- required ProjectId;
- unknown TaskStatus/TaskPriority rejection;
- all direct known TaskStatus transitions;
- Tag trimming and invariant normalization;
- Tag length boundaries after trim;
- `Version == 1` for new aggregate roots;
- deterministic supplied timestamps;
- TaskTag identity/time;
- absence of EF Core, ASP.NET Core, Npgsql, Infrastructure and Api assembly references from Domain.

## Deliberately deferred

The following are not Stage 1 responsibilities and are therefore not implemented here:

- typed `Result<T>` / Error categories;
- `ICurrentActor`;
- repository/query ports;
- Project.Active cross-aggregate enforcement;
- owner-scoped database access;
- PostgreSQL uniqueness for Tags;
- EF optimistic concurrency token configuration;
- version incrementing after successful persistence;
- HTTP DTOs/endpoints;
- authentication/authorization/CSRF.

Those appear in the exact later stages defined by the implementation plan.
