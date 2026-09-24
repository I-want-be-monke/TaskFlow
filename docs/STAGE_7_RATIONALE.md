# Stage 7 — API foundation: HTTP contract and error handling

## 1. Цель этапа

Stage 7 добавляет HTTP boundary поверх уже готовых Domain, Application и PostgreSQL persistence слоёв.

На этом этапе реализованы:

```text
Composition Root
Dependency Injection
/api/v1 routes
request/response DTO
explicit mapping
RFC7807 ProblemDetails
Result -> HTTP mapping
global exception boundary
Projects/Tasks/Tags/TaskTag controllers
201 Created + Location
API contract/integration tests
```

Authentication/authorization/CSRF намеренно остаются Stage 8.

## 2. Почему controllers не знают про DbContext

Контроллеры зависят только от конкретных Application handlers:

```text
HTTP request
-> Controller
-> Application Handler
-> Application ports
-> Infrastructure adapters
-> PostgreSQL
```

`TaskFlowDbContext` не инжектируется в controllers. Это сохраняет Application boundary и не позволяет HTTP layer обходить owner scope, domain methods, transactions или typed Result contract.

Отдельный architecture test проверяет отсутствие `TaskFlowDbContext` в constructor dependencies controllers.

## 3. Composition Root

По архитектуре DI wiring принадлежит `TaskFlow.Api/Program.cs`.

Здесь зарегистрированы:

```text
TaskFlowDbContext
IProjectRepository -> ProjectRepository
ITaskRepository    -> TaskRepository
ITagRepository     -> TagRepository
IProjectQueries    -> ProjectQueries
ITaskQueries       -> TaskQueries
ITagQueries        -> TagQueries
IUnitOfWork        -> UnitOfWork
ITransactionManager -> EfTransactionManager
ICurrentActor      -> HttpContextCurrentActor
all Application handlers
TimeProvider.System
```

MediatR не вводится: handlers регистрируются напрямую.

API startup не вызывает:

```text
Database.Migrate()
Database.MigrateAsync()
EnsureCreated()
```

Production migration process остаётся отдельным `TaskFlow.DbMigrator` на Stage 11.

## 4. Почему request/response DTO отделены от Application/Domain

HTTP wire contract не должен быть связан с внутренними Domain entities.

Созданы отдельные модели:

```text
CreateProjectRequest
UpdateProjectRequest
ProjectResponse
CreateTaskRequest
UpdateTaskRequest
TaskResponse
CreateTagRequest
UpdateTagRequest
TagResponse
PagedResponse<T>
VersionRequest
```

DTO не содержат:

```text
OwnerUserId
CreatedAt
UpdatedAt
PasswordHash
SecurityStamp
```

`OwnerUserId` по-прежнему берётся из `ICurrentActor` внутри Application.

Mapping выполняется маленькими явными функциями. AutoMapper не добавляется.

## 5. Почему Task status/priority в HTTP представлены строками

Application/Domain используют strongly typed enum:

```text
TaskStatus
TaskPriority
```

Но wire contract Stage 7 возвращает/принимает canonical strings:

```text
Todo
InProgress
Done
Low
Medium
High
```

API boundary явно парсит строки в Domain/Application values и возвращает `400` при неизвестном значении.

Это не даёт Domain enum стать публичным CLR contract контроллера и оставляет возможность независимо менять внутреннее представление при сохранении HTTP contract.

## 6. Route design

Base path:

```text
/api/v1
```

Projects используют resource controller `/api/v1/projects`.

Tasks controller использует общий `/api/v1`, потому что create route вложен в Project:

```text
POST /api/v1/projects/{projectId}/tasks
```

а остальные task operations являются resource routes:

```text
/api/v1/tasks/{taskId}
```

TaskTag relation выражена как idempotent PUT/DELETE relation resource:

```text
PUT    /api/v1/tasks/{taskId}/tags/{tagId}
DELETE /api/v1/tasks/{taskId}/tags/{tagId}
```

## 7. Version в HTTP contract

Update DTO несёт ожидаемую Version в body.

Для state transition:

```text
POST archive
POST restore
```

используется маленький `VersionRequest` body.

Для DELETE version передаётся query parameter:

```text
DELETE /resource/{id}?version=N
```

Так DELETE остаётся без обязательного request body, но optimistic concurrency contract сохраняется.

## 8. 201 Created и Location

Create Project/Task/Tag возвращают:

```text
201 Created
Location -> canonical GET route
response DTO
```

Используются named routes:

```text
GetProject
GetTask
GetTag
```

и `CreatedAtRoute`.

Это исключает ручную сборку URL строк и делает Location связанным с фактическим route definition.

## 9. Typed Result -> HTTP

Application возвращает `Result` / `Result<T>`.

API boundary не угадывает provider exceptions и не содержит business-specific try/catch.

Expected errors преобразуются по `ErrorType`:

```text
Validation             -> 400
Unauthenticated        -> 401
Forbidden              -> 403
NotFound               -> 404
Conflict               -> 409
ForbiddenByState       -> 409
InfrastructureFailure  -> 503
```

Problem body дополнительно содержит:

```text
code
traceId
```

`code` — стабильный machine-readable Application error code, например:

```text
projects.not_found
projects.version_conflict
tasks.project_archived
tags.duplicate_name
persistence.concurrency_conflict
```

## 10. Почему foreign-owned object остаётся 404

Application repositories/queries уже owner-scoped.

Controller не различает:

```text
id не существует
id существует, но принадлежит другому user
```

Оба случая получают Application `NotFound`, которое Stage 7 маппит в `404`.

Это сохраняет BOLA-safe semantics до добавления полноценной authorization pipeline на Stage 8.

## 11. Global exception boundary

Unexpected exceptions обрабатываются единым `IExceptionHandler`.

Внешний ответ:

```text
500 Internal Server Error
application/problem+json
code = http.unexpected_error
traceId = ...
```

Body не содержит:

```text
exception.ToString()
stack trace
SQL
connection string
internal path
```

Handler пока использует стандартный `ILogger`; structured event schema будет доведена на Stage 10.

## 12. Model-binding errors

`[ApiController]` может отклонить malformed JSON/model binding до Application validator.

Чтобы и эти ошибки оставались в общем RFC7807 стиле, настроен `InvalidModelStateResponseFactory`:

```text
400
ValidationProblemDetails
code = http.invalid_request
traceId
```

Таким образом expected API input errors не превращаются в произвольный MVC response shape.

## 13. ICurrentActor на Stage 7

Application уже требует `ICurrentActor`, но cookie Identity появляется только на Stage 8.

Stage 7 добавляет только HTTP adapter:

```text
HttpContext.User
-> NameIdentifier/sub claim
-> Guid UserId
```

Adapter:

- не читает user id из body;
- не читает user id из query;
- не доверяет custom identity headers;
- не создаёт claims;
- не регистрирует authentication scheme.

Если authenticated principal отсутствует, `IsAuthenticated == false` и handlers возвращают typed `Unauthenticated`.

На Stage 8 cookie authentication сформирует trusted ClaimsPrincipal, и этот adapter начнёт работать без изменения Application.

## 14. Почему не введён временный header-based auth

Было бы удобно добавить что-то вроде:

```text
X-User-Id: ...
```

чтобы вручную вызвать CRUD до Stage 8.

Это сознательно не сделано: временный identity protocol легко случайно оставить включённым и он противоречит default-deny security модели.

Вместо этого integration test создаёт fixed `ICurrentActor` напрямую в test composition и проверяет controllers against PostgreSQL без изменения production HTTP trust boundary.

## 15. PostgreSQL API integration flow

`ApiCrudPostgresTests` использует тот же PostgreSQL Testcontainer и реальные:

```text
TaskFlowDbContext
repositories
queries
UnitOfWork
EfTransactionManager
handlers
controllers
```

Основной scenario:

```text
seed auth user
-> POST Project
-> PUT Project
-> POST Tag
-> POST Task
-> PUT TaskTag
-> PUT Task
-> GET filtered Task list
-> Archive Project
-> verify Task update -> 409
-> Restore Project
-> DELETE TaskTag
-> DELETE Task
-> DELETE Tag
-> DELETE Project
```

Также отдельно проверяются:

```text
foreign-owned Project -> 404 ProblemDetails
stale Version -> 409 ProblemDetails
```

## 16. API architecture tests

Stage 7 tests проверяют:

```text
all documented routes exist
201 endpoints use CreatedAtRoute
request DTO do not expose server-controlled fields
contract properties do not expose Domain types
controllers do not inject TaskFlowDbContext
controller return contracts do not return Domain entities
all ErrorType values map to expected HTTP status
unexpected error body is sanitized
```

Дополнительно `scripts/verify_api_stage7.py` защищает source-level constraints ещё до runtime test execution.

## 17. Что сознательно не входит в Stage 7

Не добавлены:

```text
ASP.NET Core Identity registration/login endpoints
cookie authentication
fallback authorization policy
CSRF/antiforgery
lockout
rate limiting
request body limits
request timeout
health checks
security headers
production proxy configuration
structured JSON logging schema
DbMigrator runtime logic
```

Эти требования имеют отдельные этапы 8–11 и не смешиваются с базовым HTTP contract.

## 18. Definition of Done Stage 7

Stage 7 считается завершённым, когда:

```text
/api/v1 endpoints существуют
controllers используют handlers, не DbContext
Result/Error -> RFC7807 mapping стабилен
unexpected exception -> safe 500
request/response DTO отделены от Domain
201 returns Location
foreign-owned -> 404
version/state conflicts -> 409
PostgreSQL API CRUD integration contract описан тестами
API startup не запускает migrations
Stage 0–7 architecture checks проходят
```

Полный runtime verification выполняется:

```bash
./scripts/verify-stage7.sh
```
