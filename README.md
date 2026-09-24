# TaskFlow — Stage 7

TaskFlow реализуется по архитектурным этапам. **Stages 0–7 завершены в этом snapshot**: repository/build foundation, Domain, Application Core, все v1 use cases, PostgreSQL/EF Core persistence/concurrency и теперь HTTP API foundation.

## Что готово

- pinned .NET **10.0.401** / C# 14 build contract;
- locked NuGet restore и сохранённые dependency boundaries;
- Domain/Application из Stages 1–4;
- PostgreSQL schema, migration, repositories, queries, transactions и concurrency из Stages 5–6;
- Composition Root в `TaskFlow.Api/Program.cs`;
- `TaskFlowDbContext` подключён через `ConnectionStrings__Postgres`;
- Application handlers и Infrastructure adapters зарегистрированы напрямую через DI;
- `AddProblemDetails` + единый `GlobalExceptionHandler`;
- typed `Result/Error -> RFC7807 ProblemDetails` mapping;
- стабильный `ProblemDetails.extensions.code`;
- API base path `/api/v1`;
- отдельные request/response DTO; Domain entities не выходят в HTTP contract;
- явный mapping DTO <-> Application models, без AutoMapper;
- Projects CRUD + archive/restore;
- Tasks CRUD + filters/sorting/pagination;
- Tags CRUD;
- Task ↔ Tag attach/detach;
- `201 Created` для Project/Task/Tag возвращает named route для `Location`;
- `HttpContextCurrentActor` только читает уже существующий user-id claim; authentication scheme пока не вводится;
- PostgreSQL API integration test проходит цепочку controller -> handler -> repository/query -> PostgreSQL;
- API contract tests проверяют routes, RFC7807, DTO boundaries, foreign-owned `404`, version/state `409`;
- Git history Stages 0–7 сохранена в `.git`.

## API v1

Projects:

```text
GET    /api/v1/projects
GET    /api/v1/projects/{projectId}
POST   /api/v1/projects
PUT    /api/v1/projects/{projectId}
POST   /api/v1/projects/{projectId}/archive
POST   /api/v1/projects/{projectId}/restore
DELETE /api/v1/projects/{projectId}?version=...
```

Tasks:

```text
GET    /api/v1/tasks
GET    /api/v1/tasks/{taskId}
POST   /api/v1/projects/{projectId}/tasks
PUT    /api/v1/tasks/{taskId}
DELETE /api/v1/tasks/{taskId}?version=...
PUT    /api/v1/tasks/{taskId}/tags/{tagId}
DELETE /api/v1/tasks/{taskId}/tags/{tagId}
```

Tags:

```text
GET    /api/v1/tags
GET    /api/v1/tags/{tagId}
POST   /api/v1/tags
PUT    /api/v1/tags/{tagId}
DELETE /api/v1/tags/{tagId}?version=...
```

Task list supports:

```text
?projectId=...
&status=Todo
&priority=High
&tagId=...
&dueBefore=...
&dueAfter=...
&q=...
&page=1&pageSize=50
&sort=createdAt:desc
```

## Error contract

Expected Application errors become RFC7807 `ProblemDetails`:

```text
Validation             -> 400
Unauthenticated        -> 401
Forbidden              -> 403
NotFound               -> 404
Conflict               -> 409
ForbiddenByState       -> 409
InfrastructureFailure  -> 503
Unexpected exception   -> 500
```

Every expected problem carries a stable application error code:

```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "The resource was changed by another operation. Reload it and retry.",
  "code": "persistence.concurrency_conflict",
  "traceId": "..."
}
```

Production-facing bodies do not include stack traces, SQL, connection strings, cookies or internal paths.

## Stage 7 auth boundary

Stage 7 intentionally **does not configure cookie authentication, authorization policy or CSRF**. Those belong to Stage 8.

`HttpContextCurrentActor` only adapts an already-authenticated `ClaimsPrincipal` into the Application `ICurrentActor` interface. It does not trust ad-hoc headers/query parameters as identity and does not create an authentication mechanism.

Therefore a normal standalone Stage 7 HTTP request has no authenticated actor until Stage 8 is implemented. The Stage 7 PostgreSQL API integration tests inject a fixed `ICurrentActor` directly into handlers/controllers to verify the HTTP/Application/Persistence contract without introducing a temporary insecure production protocol.

## Configuration for API startup

At this stage the API requires:

```text
ConnectionStrings__Postgres
```

Example:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=taskflow;Username=taskflow_app;Password=change-me'
dotnet run --project src/TaskFlow.Api
```

Typed Options/fail-fast security configuration arrives in Stage 9.

## Проверка Stage 7

Нужны:

```text
.NET SDK 10.0.401
Docker Engine / Docker Desktop
```

Из корня репозитория:

```bash
./scripts/verify-stage7.sh
```

Скрипт выполняет:

```text
Stage 0–7 source/architecture checks
-> dotnet restore TaskFlow.sln --locked-mode
-> Release build
-> Docker availability check
-> unit tests
-> PostgreSQL/Testcontainers persistence/concurrency/API tests
```

Основные команды отдельно:

```bash
python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
python3 scripts/verify_task_tag_features.py
python3 scripts/verify_infrastructure_stage5.py
python3 scripts/verify_infrastructure_stage6.py
python3 scripts/verify_api_stage7.py

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

## Что остаётся на Stage 8

Следующий этап закрывает HTTP API security pipeline:

- ASP.NET Core Identity;
- cookie authentication;
- PostgreSQL Data Protection key store;
- fallback `RequireAuthenticatedUser`;
- register/login/logout/me;
- antiforgery endpoint and validation;
- lockout / generic invalid-credentials response;
- cookie security attributes;
- BOLA/API security matrix;
- multi-replica cookie/antiforgery checks.

## Статус проверки этого архива

При создании snapshot выполняются все доступные Python/source architecture checks, XML/JSON checks, `git diff --check`, `git fsck` и archive integrity checks. В текущем artifact-контейнере отсутствуют `dotnet` и Docker, поэтому README **не утверждает**, что runtime `dotnet restore/build/test` или Testcontainers suite были запущены внутри него.

Подробные решения: [`docs/STAGE_7_RATIONALE.md`](docs/STAGE_7_RATIONALE.md).
