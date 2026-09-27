# TaskFlow — .NET full-stack приложение

TaskFlow — full-stack приложение на **.NET 10**: ASP.NET Core API, Blazor WebAssembly, EF Core/PostgreSQL, отдельный DbMigrator и production-like Docker Compose topology.

Служебная автоматизация реализована на .NET в `TaskFlow.DevCli`. Для запуска и проверок проекта не требуются Bash/PowerShell launcher-файлы или Python scripts.


### Frontend health check

The frontend image explicitly installs `curl`, and Docker Compose probes `http://127.0.0.1:8080/healthz` with that binary. The image build also runs `nginx -t` against the effective configuration (using a temporary build-only certificate), so NGINX syntax errors fail during `docker build` instead of surfacing later as an opaque `unhealthy` container. Failed `up` runs print Compose status, frontend health state, and recent frontend logs automatically.

## Что теперь отвечает за запуск и CI/CD

Весь developer tooling находится в одном проекте:

```text
tools/TaskFlow.DevCli/
```

Он отвечает за:

- запуск/остановку Docker Compose;
- status/logs/doctor;
- container и edge smoke;
- source/architecture verification;
- локальный полный verification pipeline;
- NuGet vulnerability audit;
- Gitleaks и Trivy orchestration;
- Playwright Chromium browser E2E через официальный `Microsoft.Playwright` для .NET;
- те же CI targets, которые вызывает GitHub Actions.

Python проекту и CI больше не нужен.

## Требования

- .NET SDK `10.0.401` — закреплён в `global.json`;
- Docker Desktop / Docker Engine;
- Docker Compose v2 (`docker compose`).

Локально устанавливать PostgreSQL, NGINX, Python, Node.js или Playwright CLI отдельно не требуется.

`doctor` читает требуемый SDK прямо из `global.json`, отдельно показывает выбранный SDK (`dotnet --version`) и runtime, на котором запущен DevCli. Поэтому версия runtime больше не маскируется под версию SDK.

## Быстрый запуск

Из корня проекта:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- doctor
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- up
```

После запуска DevCli сам выведет адрес, например `https://localhost:24xxx/`. Для новой копии репозитория HTTPS-порт вычисляется стабильно из пути проекта, поэтому несколько копий TaskFlow можно запускать параллельно без общего порта по умолчанию. Точный порт также показывает `doctor`.

Локальный TLS-сертификат self-signed, поэтому браузер может показать предупреждение.

Остановить приложение:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- down
```

Остановить и удалить PostgreSQL volume:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- down --volumes
```

### Изоляция Docker Compose между копиями проекта

`TaskFlow.DevCli` больше не использует общий жёстко заданный Compose project name `taskflow`. Для каждой папки репозитория вычисляются стабильные уникальные Compose project name, HTTPS host port, backend subnet и proxy IP. Поэтому разные копии TaskFlow не делят контейнеры, network, PostgreSQL volume и стандартный host port. При необходимости значения можно явно переопределить через `TASKFLOW_COMPOSE_PROJECT_NAME`, `TASKFLOW_HTTPS_PORT`, `TASKFLOW_BACKEND_SUBNET` и `TASKFLOW_PROXY_IP`.

`.env.compose.local` содержит пароли ролей PostgreSQL и должен соответствовать persistent volume этой конкретной копии проекта. Если env-файл был удалён, но Docker volume остался, команда `up` намеренно остановится вместо генерации новых несовместимых паролей. Восстановите прежний `.env.compose.local` либо, если локальные данные не нужны, выполните:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- down --volumes
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- up
```

`down` и `down --volumes` умеют очистить ресурсы текущего Compose project даже при отсутствующем `.env.compose.local`, используя Docker Compose labels. Эти же команды теперь автоматически очищают legacy stack `taskflow`, если Docker labels подтверждают, что он был создан из этой же папки проекта.

## Полезные команды

```powershell
# состояние контейнеров
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- status

# последние 50 строк логов всех контейнеров
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- logs --tail 50

# только API
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- logs --tail 50 api

# API/container smoke
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- smoke

# HTTPS frontend + same-origin API smoke
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- edge-smoke
```

Все команды также работают из Linux/macOS — нужно только использовать `/` вместо `\` в пути к `.csproj`.

## Проверка проекта без shell/Python scripts

DevCli больше не использует номера этапов разработки. Static verification разделена на смысловые scopes:

```powershell
# архитектурные границы, CRUD/use-case структура, API, migrator, client
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify-static architecture

# auth/antiforgery/hardening/browser security boundary
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify-static security

# Dockerfile/Compose invariants
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify-static containers

# GitHub Actions/coverage/browser automation wiring
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify-static ci

# все static guards (также используется по умолчанию)
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify-static all
```

Local verification тоже принимает смысловой scope:

```powershell
# build + unit tests
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify architecture

# build + unit/integration security checks
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify security

# production-like Compose + Trivy + smoke + browser E2E
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify containers

# полный pipeline; `verify` без scope эквивалентен `verify all`
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- verify all
```

Полный `verify all` последовательно выполняет static guards, SDK check из `global.json`, locked restore основного solution, Release build, unit tests, PostgreSQL integration tests, supply-chain gates, production-like Compose smoke и browser E2E.


## Docker build cache

Dockerfile для API и DbMigrator отделяют dependency restore от полного копирования исходников: до `dotnet restore --locked-mode` попадают только dependency descriptors, после чего выполняется `COPY . .` и `dotnet publish --no-restore`. Для Blazor Client используется специальный .NET 10-safe вариант: dependency descriptors `TaskFlow.Contracts` и весь `TaskFlow.Client` (включая Razor-файлы) копируются до locked restore, чтобы `Microsoft.AspNetCore.App.Internal.Assets` и lock-file graph оставались согласованными. Полный repository context всё равно копируется только после restore.

## Чистая упаковка проекта

Для сдачи не нужно вручную архивировать папку с `bin/`, `obj/` и локальными секретами. DevCli создаёт source-only ZIP и исключает `.git`, `.env.compose.local`, `bin/obj`, `node_modules`, TestResults, coverage, local DB/log files, IDE/OS-мусор и вложенные архивы:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- pack
```

По умолчанию файл создаётся в `artifacts/submission/TaskFlow-submission.zip`. Можно передать свой путь вторым аргументом.

## Shared transport contracts

HTTP request/response DTO больше не дублируются между API и Blazor. Они находятся в `src/TaskFlow.Contracts` и используются обоими приложениями. API-specific mapping/parsing остаётся в `TaskFlow.Api`, а client-only query model для списка задач остаётся в `TaskFlow.Client`.

## Frontend static assets

NGINX использует готовые `.gz` assets Blazor через `gzip_static on`. Только fingerprinted файлы внутри `/_framework/` получают `Cache-Control: public, max-age=31536000, immutable`; bootstrap-файлы со стабильными именами (например `blazor.webassembly.js`) требуют revalidation через `no-cache`, а `index.html` получает `no-store`. Security headers вынесены в один include и сохраняются для cache-specific locations.

## Тесты DevCli

`tests/TaskFlow.DevCli.Tests` покрывает стабильность Compose project defaults, генерацию `.env.compose.local`, фильтрацию submission archive и CLI parsing. Эти тесты входят в `TaskFlow.sln` и выполняются quality pipeline вместе с Domain/Application unit tests.

## Playwright теперь тоже .NET

Browser flow находится в:

```text
tools/TaskFlow.DevCli/BrowserE2eCommands.cs
```

Установить Chromium через .NET:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- browser-install
```

На Linux CI вместе с системными зависимостями:

```text
dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- browser-install --with-deps
```

Запустить browser E2E:

```powershell
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- browser-e2e
```

`browser-e2e` сам вызывает установку Chromium, если не указан `--skip-browser-install`.


### Password policy

ASP.NET Core Identity policy задана явно: минимум 12 символов, минимум 6 уникальных символов, обязательны uppercase, lowercase, цифра и non-alphanumeric символ. Правила вынесены в `TaskFlow.Contracts.Auth.PasswordPolicyRules`: Identity и Blazor registration form используют один источник констант/валидации, а сервер всё равно остаётся authoritative validator.

## CI/CD на C#

GitHub Actions использует тонкий workflow `.github/workflows/ci.yml`: основные quality/integration/supply-chain/container targets делегируются `TaskFlow.DevCli`, а отдельный `coverage` job только запускает pinned `dotnet-coverage`, собирает Cobertura и публикует артефакт.

Те же CI targets можно запускать локально:

```powershell
# build/analyzers/static guards/unit tests
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- ci quality

# PostgreSQL integration tests
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- ci integration

# NuGet audit + Gitleaks
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- ci supply-chain

# Compose + Trivy + smoke + .NET Playwright
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- ci containers

# всё последовательно
dotnet run --project .\tools\TaskFlow.DevCli\TaskFlow.DevCli.csproj -- ci all
```

CI сохраняет параллельные jobs (`quality`, `integration`, `coverage`, `supply_chain`, `containers`), чтобы проверки не превращались в один длинный непрозрачный job. `coverage` запускает все test projects под pinned `dotnet-coverage` и публикует Cobertura-отчёт `code-coverage-cobertura` на 14 дней. Coverage используется как наблюдаемая метрика качества: искусственного fail-threshold нет.

## Качество TaskFlow.DevCli

`TaskFlow.DevCli` теперь входит в `TaskFlow.sln` и подчиняется тем же repository-wide правилам, что и основной код: `TreatWarningsAsErrors`, code-style enforcement, .NET analyzers и lock-file restore. Для DevCli добавлен собственный `tools/TaskFlow.DevCli/packages.lock.json`, а CI восстанавливает его с `--locked-mode`. Локальные отключения этих правил из `.csproj` удалены.

Известные analyzer findings в DevCli также устранены: Playwright installer сделан `static`, URL parsing больше не вызывает LINQ на индексируемом массиве, а `JsonSerializerOptions` для audit-output кэшируется и переиспользуется.

## Архитектурные границы

```text
Domain          -> nothing
Contracts       -> BCL/DataAnnotations only
Application     -> Domain
Infrastructure  -> Application + Domain
Api             -> Application + Infrastructure + Contracts
DbMigrator      -> Infrastructure
Client          -> Blazor/BCL + Contracts
```

Основные свойства v1:

- PostgreSQL + EF Core;
- отдельный DbMigrator, API не выполняет migrations;
- owner-scoped resources;
- optimistic concurrency через `Version`;
- cookie authentication + antiforgery;
- Data Protection keys в PostgreSQL;
- Blazor WASM без bearer token storage в browser storage;
- structured JSON logs;
- production-like Compose topology с NGINX reverse proxy;
- Gitleaks / Trivy / NuGet vulnerability gates;
- .NET Playwright browser E2E.

## Что осталось shell-based

Пользовательских launcher/verification shell scripts нет. Shell используется только как нативный внутренний механизм Linux-контейнеров:

- NGINX container entrypoint генерирует локальный self-signed TLS certificate;
- официальный PostgreSQL image вызывает init hook для создания least-privilege DB roles.

Это не требует Bash/Git Bash/PowerShell от пользователя и не участвует в запуске команд с хоста.

## Документация

- `PROJECT_REPORT.md` — итоговый отчёт по архитектуре, реализации и проверкам проекта.
