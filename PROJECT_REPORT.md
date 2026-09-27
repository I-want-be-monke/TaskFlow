# Отчёт по проекту TaskFlow

## 1. Доменная область TaskFlow

TaskFlow — клиент-серверное приложение для управления личными проектами, задачами и метками. Пользователь создаёт проекты, добавляет задачи, меняет их статус/приоритет, задаёт срок выполнения, создаёт повторно используемые Tags и связывает их с Tasks.

Архивированный Project остаётся доступен для чтения, но блокирует изменения Task и Task↔Tag до восстановления.

## 2. Стек реализации

- C# 14 / .NET 10, SDK закреплён `10.0.401`;
- ASP.NET Core Web API;
- Blazor WebAssembly;
- Entity Framework Core;
- PostgreSQL;
- ASP.NET Core Identity + HttpOnly cookie;
- ASP.NET Core Antiforgery;
- xUnit v3 / Microsoft Testing Platform;
- Testcontainers.PostgreSql;
- NGINX reverse proxy;
- Docker Compose;
- отдельная .NET CLI `TaskFlow.DevCli` для запуска, логов, smoke и служебной orchestration;
- GitHub Actions;
- Microsoft.Playwright 1.63.0 для .NET browser E2E.

## 3. Основные сущности

### User

Identity user с `Guid` identifier. Authentication state хранится в secure HttpOnly cookie; browser не хранит bearer/refresh tokens.

### Project

Содержит owner, name, description, `Active/Archived`, timestamps и `Version`. Archived Project блокирует все Task/TaskTag mutations.

### TaskItem

Принадлежит Project. Содержит title, description, status (`Todo/InProgress/Done`), priority (`Low/Medium/High`), optional due date, timestamps и `Version`.

### Tag

Принадлежит User. Имя нормализуется и уникально в пределах owner. Содержит timestamps и `Version`.

### TaskTag

Связь many-to-many Task↔Tag с composite key `(TaskId, TagId)`.

## 4. Архитектура

Используется модульный монолит + облегчённая Clean Architecture / Ports & Adapters:

```text
Domain          -> ничего
Application     -> Domain
Infrastructure  -> Application + Domain
Api             -> Application + Infrastructure
DbMigrator      -> Infrastructure
Client          -> Blazor/BCL + собственные API models
```

Один `TaskFlowDbContext` владеет business schema, Identity и Data Protection keys.

Browser topology production-like запуска:

```text
Browser
  -> HTTPS NGINX / Blazor WASM
       -> /api/* reverse proxy
            -> ASP.NET Core API
                 -> PostgreSQL

DbMigrator -> PostgreSQL до старта API
```

Frontend и API работают same-origin. Sticky session не требуется: API stateless, business state и Data Protection keys находятся в PostgreSQL.

## 5. CRUD

REST API опубликован под `/api/v1`.

UI поддерживает:

- Project: create/read/list/update/archive/restore/delete;
- Task: create/read/list/filter/sort/update/delete;
- Tag: create/read/list/update/delete;
- TaskTag: attach/detach.

Blazor Razor components не формируют raw HTTP requests: все вызовы проходят через `AuthApiClient`, `ProjectsApiClient`, `TasksApiClient`, `TagsApiClient`.

При optimistic concurrency конфликте API возвращает `409`, а UI показывает reload-required UX вместо silent overwrite.

## 6. Security

- ASP.NET Core Identity;
- secure `__Host-TaskFlow.Auth` HttpOnly cookie;
- `Secure`, `SameSite=Strict`, finite expiration;
- fallback authorization `RequireAuthenticatedUser`;
- owner-scoped repositories/queries против BOLA;
- antiforgery для POST/PUT/PATCH/DELETE;
- явная Identity password policy: 12+ chars, 6+ unique chars, uppercase/lowercase/digit/non-alphanumeric;
- lockout и generic invalid-credentials response;
- rate limiting, body limits, request timeouts;
- trusted proxy allowlist;
- dev-only exact CORS;
- API runtime DB role без DDL;
- отдельная privileged migrator role;
- structured log redaction.

## 7. Concurrency и целостность

Mutable aggregates используют `Version` optimistic concurrency token.

Cross-aggregate invariant archived Project защищается общей транзакцией и PostgreSQL row locks с порядком:

```text
Project -> TaskItem -> Tag/TaskTag
```

Integration tests содержат same-Version race, Tag uniqueness race, ArchiveProject race matrix и deadlock regression.

## 8. Migrations

API никогда не вызывает `Migrate/EnsureCreated` при startup.

Отдельный `TaskFlow.DbMigrator`:

```text
load config
-> open PostgreSQL
-> bounded advisory lock
-> Database.MigrateAsync()
-> unlock
-> exit code
```

Deployment запускает migrator как one-shot process до API.

## 9. Observability

Логи — structured JSON, одна строка на событие, только stdout/stderr. Нет application file sink.

Стабильный event contract включает service/release/environment/instance, trace/span/request ids и EventId/EventName. Не логируются passwords, auth/antiforgery cookies/tokens, connection strings, Data Protection keys и request bodies.

Есть RequestCompleted, security events, slow DB events, lifecycle и migration events.

## 10. Health

```text
/health/live  -> только process/HTTP liveness
/health/ready -> PostgreSQL readiness, остаётся под authentication policy
```

## 11. 12-factor

### 11.1 Codebase

Один Git repository содержит source, tests, migrations, frontend и deploy configuration; один codebase используется для разных deployments.

### 11.2 Dependencies

NuGet dependencies объявлены явно, versions централизованы, lock files коммитятся, CI выполняет `restore --locked-mode`, SDK pinned через `global.json`. Это правило теперь распространяется и на `TaskFlow.DevCli`: он включён в `TaskFlow.sln`, имеет собственный `packages.lock.json` и больше не отключает warnings-as-errors/analyzers/code style.

### 11.3 Config

Environment-specific config и secrets приходят через environment/configuration. Реальные secrets не хранятся в repository/image layers.

### 11.4 Backing services

PostgreSQL подключается через connection string и рассматривается как attached backing service.

### 11.5 Build, release, run

CI build создаёт immutable SHA-tagged images. Release = image + runtime config. Runtime container ничего не компилирует.

### 11.6 Processes

API stateless. Business state, Identity state и Data Protection key ring находятся в PostgreSQL.

### 11.7 Port binding

API слушает configured HTTP port внутри container; frontend NGINX публикует HTTPS port и same-origin proxy.

### 11.8 Concurrency

Несколько одинаковых API replicas поддерживаются без sticky sessions; concurrency защищается DB transaction/locks/version tokens.

### 11.9 Disposability

Processes быстро стартуют, обрабатывают cancellation/SIGTERM, незавершённые DB transactions rollback, обязательных локальных queues/files нет.

### 11.10 Dev/prod parity

Integration tests используют реальный PostgreSQL/Testcontainers; локальный production-like запуск использует те же auth/migration/security boundaries.

### 11.11 Logs

Application logs являются event stream в stdout/stderr; retention/rotation/storage выполняются внешней logging platform/container runtime.

### 11.12 Admin processes

Migrations выполняются отдельным one-shot `TaskFlow.DbMigrator` с PostgreSQL advisory lock и отдельными credentials.

## 12. Тестирование и Definition of Done

Репозиторий содержит:

- Domain/Application unit tests;
- PostgreSQL schema/repository/concurrency integration tests;
- API contract/RFC7807 tests;
- auth/CSRF/BOLA/multi-replica tests;
- DbMigrator/least-privilege tests;
- logging/redaction tests;
- client boundary/UI-state tests;
- C# source/architecture verifier со смысловыми scopes `architecture`, `security`, `containers`, `ci`, `all` в `TaskFlow.DevCli`;
- container/edge smoke на C#;
- финальный Microsoft.Playwright browser CRUD E2E на C#.

Browser flow проходит Register/Login, CRUD Project/Task/Tag, TaskTag attach, filters, refresh/session persistence, cleanup и logout. Archive/concurrency/security/restart invariants дополнительно проверяются unit/integration и container smoke тестами.

## 13. CI

GitHub Actions остаётся тонкой YAML-точкой входа: основные pipeline targets выполняет C# `TaskFlow.DevCli`, а отдельный coverage job отвечает только за сбор и публикацию Cobertura-отчёта:

```text
locked restore
-> Release build + analyzers
-> unit tests
-> PostgreSQL integration/P0 tests
-> Cobertura code coverage artifact (без искусственного threshold)
-> architecture guards
-> NuGet vulnerability scan
-> full-history secret scan
-> container build
-> container vulnerability scan
-> container smoke
-> final Playwright browser E2E
```

## 14. Как запустить приложение локально

Docker build оптимизирован по cache boundaries. API и DbMigrator до `dotnet restore --locked-mode` копируют только dependency descriptors (`global.json`, `Directory.*`, `.csproj`, `packages.lock.json`), а полный source tree — после restore. Для Blazor Client применяется .NET 10-safe вариант: descriptors `TaskFlow.Contracts` и весь `TaskFlow.Client` с Razor-файлами доступны до locked restore, чтобы restore-time internal asset graph совпадал с `packages.lock.json`; остальной repository context копируется после restore.

Основной локальный lifecycle перенесён в отдельную .NET CLI `tools/TaskFlow.DevCli`. Bash и PowerShell-скрипты для запуска приложения больше не требуются. Для обычного запуска нужны .NET SDK `10.0.401` и запущенный Docker Desktop с Docker Compose v2. Локальные PostgreSQL и Python приложению не нужны.

Проверка окружения:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- doctor
```

`doctor` читает SDK contract из `global.json`, выводит required SDK/roll-forward policy, фактически выбранный SDK через `dotnet --version`, отдельно показывает runtime DevCli и затем проверяет Docker CLI, Compose v2 и Docker Engine. Это устраняет прежнюю неоднозначность, когда `Environment.Version` показывался как будто это версия SDK.

Запуск:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- up
```

CLI автоматически создаёт `.env.compose.local`, генерирует локальные секреты через `System.Security.Cryptography`, формирует image tag и выполняет production-like `docker compose up --build --detach --wait`. Compose project name вычисляется из пути конкретной копии репозитория и передаётся через `--project-name`, поэтому разные локальные копии не разделяют контейнеры и PostgreSQL volume. Если env-файл с DB credentials утрачен при существующем persistent volume, DevCli блокирует `up` и требует восстановить env либо явно удалить локальные данные через `down --volumes`.

После запуска DevCli печатает точный адрес, например:

```text
https://localhost:24xxx/
```

HTTPS host port стабильно вычисляется из пути конкретной копии репозитория и также показывается командой `doctor`.

Development certificate self-signed, поэтому локальный браузер может попросить подтвердить исключение.

Состояние и логи:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- status
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- logs --tail 50
```

Smoke-проверки также перенесены в .NET:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- smoke
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- edge-smoke
```

Остановить:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- down
```

Удалить также PostgreSQL volume:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- down --volumes
```

Shell оставлен только как внутренний механизм Linux-контейнеров NGINX/PostgreSQL; пользовательский запуск и verification/CI orchestration выполняются через .NET.

## 15. Как выполнить финальную проверку

Для полной проверки нужны только .NET SDK `10.0.401` и Docker Compose v2. Python не используется. Playwright Chromium устанавливается из C# через официальный .NET package.

Полная проверка проекта:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify
```

Только architecture/source checks:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify-static
```

Полный аналог GitHub CI локально:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- ci all
```
## Финальная доработка: packaging, isolation, contracts и frontend delivery

- Добавлен `TaskFlow.Contracts`: transport DTO и password-policy validation теперь едины для API и Blazor, без зависимостей на Domain/Application/EF Core.
- DevCli генерирует per-repository Compose project name, HTTPS port, backend subnet и proxy IP; `down` умеет очищать legacy `taskflow` stack этой же рабочей директории.
- Добавлен `TaskFlow.DevCli.Tests` и включён в solution/quality pipeline.
- Добавлена команда `pack`, создающая source-only submission ZIP без `.env.compose.local`, `bin/obj`, coverage/TestResults и локальных runtime-файлов.
- NGINX отдаёт precompressed Blazor `.gz`, кэширует fingerprinted `/_framework/` как immutable и не кэширует `index.html`; Docker build выполняет `nginx -t`, поэтому синтаксическая ошибка конфигурации блокирует сборку образа до запуска контейнера.
- Blazor password validation использует ту же policy, что ASP.NET Core Identity; отдельные тесты проверяют синхронизацию правил.

