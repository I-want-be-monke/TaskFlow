# TaskFlow — Stage 8

TaskFlow реализуется по архитектурным этапам. **Stages 0–8 завершены в этом snapshot**: repository/build foundation, Domain, Application Core, все v1 use cases, PostgreSQL/EF Core persistence/concurrency, HTTP API и теперь полноценная browser-session security pipeline.

## Что готово

- pinned .NET **10.0.401** / C# 14 build contract;
- locked NuGet restore и architecture boundaries;
- Domain/Application из Stages 1–4;
- PostgreSQL schema, migration, repositories, queries, transactions и concurrency из Stages 5–6;
- HTTP API `/api/v1` из Stage 7;
- ASP.NET Core Identity поверх существующего `TaskFlowDbContext`;
- same-origin authentication cookie `__Host-TaskFlow.Auth`;
- `HttpOnly = true`, `Secure = Always`, `SameSite = Strict`, `Path = /`;
- finite authentication ticket lifetime: 8 hours, sliding expiration enabled;
- `Remember me` отсутствует в v1: `SignInAsync(..., isPersistent: false)`;
- Identity password hashing; custom password hash/salt отсутствует;
- lockout включён: 5 неудачных попыток -> 15 минут блокировки; login использует `lockoutOnFailure: true`;
- generic `auth.invalid_credentials` для неверного login и lockout/non-success result;
- shared ASP.NET Core Data Protection key ring в PostgreSQL (`data_protection_keys`);
- единый Data Protection application name `TaskFlow` для всех replicas;
- fallback authorization policy `RequireAuthenticatedUser`;
- anonymous только `auth/antiforgery`, `auth/register`, `auth/login` на текущем этапе;
- `register`, `login`, `logout`, `me`;
- ASP.NET Core Antiforgery с request header `X-XSRF-TOKEN`;
- antiforgery cookie `__Host-TaskFlow.Antiforgery`, `HttpOnly + Secure + SameSite=Strict`;
- все non-GET/HEAD/OPTIONS requests проверяются единым `ApiAntiforgeryFilter`;
- CSRF failure возвращается как RFC7807 `application/problem+json`;
- antiforgery token требуется обновлять после register/login/logout auth-state change;
- BOLA semantics остаются owner-scoped: чужие Project/Task/Tag/TaskTag операции дают `404`;
- HTTP security integration tests используют `WebApplicationFactory` + реальный PostgreSQL Testcontainer;
- multi-replica tests проверяют auth cookie и antiforgery через общий PostgreSQL key ring;
- Git history Stages 0–8 сохраняется в `.git`.

## Auth/session API

```text
GET  /api/v1/auth/antiforgery   anonymous
POST /api/v1/auth/register      anonymous + CSRF
POST /api/v1/auth/login         anonymous + CSRF
POST /api/v1/auth/logout        authenticated + CSRF
GET  /api/v1/auth/me            authenticated
```

### Browser flow

Перед первым unsafe request:

```text
GET /api/v1/auth/antiforgery
-> JSON { token }
-> сервер также устанавливает HttpOnly antiforgery cookie
```

Клиент отправляет request token только в памяти вкладки:

```text
X-XSRF-TOKEN: <token>
```

После успешного register/login:

```text
auth state changed
-> старый request token больше не используется
-> GET /api/v1/auth/antiforgery
-> сохранить новый request token только в памяти
```

После logout:

```text
clear old request token
-> GET /api/v1/auth/antiforgery
-> получить новый anonymous request token
```

## Authentication cookie

```text
Name       = __Host-TaskFlow.Auth
HttpOnly   = true
Secure     = Always
SameSite   = Strict
Path       = /
Ticket TTL = 8 hours
Sliding    = true
Persistent = false for register/login
```

Browser JavaScript не читает auth cookie. Состояние пользователя определяется через:

```text
GET /api/v1/auth/me
```

## CSRF contract

Safe methods:

```text
GET
HEAD
OPTIONS
```

Все остальные методы, включая используемые приложением:

```text
POST
PUT
PATCH
DELETE
```

проходят `IAntiforgery.ValidateRequestAsync`.

При ошибке:

```json
{
  "type": "about:blank",
  "title": "Bad Request",
  "status": 400,
  "detail": "A valid antiforgery token is required for this request.",
  "code": "security.csrf_validation_failed",
  "traceId": "..."
}
```

## Authorization

Глобальная fallback policy:

```text
RequireAuthenticatedUser
```

Контроллеры Projects/Tasks/Tags не нужно помечать `[Authorize]` по отдельности: deny-by-default применяется ко всем endpoint без `[AllowAnonymous]`.

На Stage 8 `[AllowAnonymous]` используется только для:

```text
GET  /api/v1/auth/antiforgery
POST /api/v1/auth/register
POST /api/v1/auth/login
```

`logout` и `me`, как и весь business API, защищены fallback policy.

## Data Protection / multi-replica

Data Protection key ring хранится в общей PostgreSQL таблице:

```text
data_protection_keys
```

и использует:

```text
SetApplicationName("TaskFlow")
```

Поэтому replica B может расшифровать auth cookie и antiforgery token, выпущенные replica A. Sticky sessions для correctness не нужны.

## Security integration tests

Stage 8 добавляет проверки:

```text
anonymous business endpoint -> RFC7807 `401`
register/login/logout/me
cookie security attributes
POST without CSRF -> RFC7807 400
GET without CSRF -> allowed when authenticated
POST/PUT/DELETE without CSRF -> rejected
antiforgery refresh after auth-state change
generic invalid-credentials response
failed password increments Identity AccessFailedCount
foreign Project -> 404
foreign Task -> 404
foreign Tag -> 404
foreign TaskTag relation -> 404
cookie issued by replica A works on replica B
antiforgery issued by replica A works on replica B
```

## Configuration

На Stage 8 API по-прежнему требует:

```text
ConnectionStrings__Postgres
```

Пример:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=taskflow;Username=taskflow_app;Password=change-me'
dotnet run --project src/TaskFlow.Api
```

Typed Options, rate limiting, request limits, trusted proxies и health checks добавляются на Stage 9.

## Проверка Stage 8

Нужны:

```text
.NET SDK 10.0.401
Docker Engine / Docker Desktop
```

Из корня репозитория:

```bash
./scripts/verify-stage8.sh
```

Скрипт выполняет:

```text
Stage 0–8 source/architecture checks
-> dotnet restore TaskFlow.sln --locked-mode
-> Release build
-> Docker availability check
-> unit tests
-> PostgreSQL/Testcontainers persistence/concurrency/API/security tests
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
python3 scripts/verify_security_stage8.py

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

## Что остаётся на Stage 9

Следующий этап добавляет production hardening:

- typed Options + `ValidateOnStart`;
- request body limits;
- request timeouts;
- rate limiting;
- trusted proxy configuration;
- dev-only exact CORS allowlist при необходимости;
- `/health/live` и `/health/ready`;
- проверку, что API startup не выполняет migrations;
- дополнительные security headers на reverse proxy/edge contract.

## Статус проверки этого архива

При создании snapshot выполняются все доступные Python/source architecture checks, XML/JSON checks, NuGet lock-graph checks, `git diff --check`, `git fsck` и archive integrity checks. В текущем artifact-контейнере отсутствуют `dotnet` и Docker, поэтому README **не утверждает**, что runtime `dotnet restore/build/test` или Testcontainers suite были запущены внутри него.

Подробные решения: [`docs/STAGE_8_RATIONALE.md`](docs/STAGE_8_RATIONALE.md).
