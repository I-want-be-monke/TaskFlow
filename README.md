# TaskFlow — Stage 14

TaskFlow реализуется по архитектурным этапам. **Stages 0–14 завершены в этом snapshot**: backend, PostgreSQL, security, observability, one-shot DbMigrator, Blazor WASM CRUD UI и production-like container topology.

## Что добавлено на Stage 14

- `src/TaskFlow.Api/Dockerfile`;
- `src/TaskFlow.DbMigrator/Dockerfile`;
- `src/TaskFlow.Client/Dockerfile`;
- unprivileged NGINX для Blazor static files + reverse proxy;
- HTTPS browser endpoint с runtime-generated self-signed certificate;
- PostgreSQL 18 compose service + persistent volume;
- automatic bootstrap ролей `taskflow_app` / `taskflow_migrator`;
- startup order `PostgreSQL -> DbMigrator -> API -> Frontend`;
- read-only root filesystem для API/migrator/frontend;
- generated local secrets вне Git/image layers;
- commit-SHA image tags;
- container smoke flow с API restart/session/antiforgery/data persistence;
- `scripts/verify_containers_stage14.py` и `scripts/verify-stage14.sh`.

## Production-like topology

```text
Browser
  |
  | HTTPS :8443
  v
TaskFlow.Frontend (NGINX + Blazor WASM)
  |
  | /api/*
  v
TaskFlow.Api :8080 (internal only)
  |
  v
PostgreSQL

TaskFlow.DbMigrator
  -> PostgreSQL
  -> exits before API starts
```

Host ports для PostgreSQL и API не публикуются. Browser traffic входит только через frontend reverse proxy, поэтому frontend и API работают same-origin.

## Быстрый запуск

Нужны:

```text
Docker Engine / Docker Desktop
Docker Compose v2
Git
Python 3
```

Из чистого committed checkout:

```bash
./scripts/compose-up.sh
```

Скрипт:

1. требует clean Git worktree;
2. создаёт `.env.compose.local` со случайными локальными passwords;
3. тегирует TaskFlow images полным текущим Git commit SHA;
4. валидирует Compose interpolation;
5. собирает и запускает stack;
6. ждёт доступности frontend/API.

Открыть:

```text
https://localhost:8443/
```

Локальный certificate self-signed и создаётся при старте frontend container. Для development stack нужно принять browser warning. Certificate/private key не находятся в Git или image layers.

## Startup order

Compose кодирует:

```text
PostgreSQL healthcheck
-> TaskFlow.DbMigrator exits 0
-> API starts and passes /health/live
-> Frontend starts
```

`TaskFlow.Api` по-прежнему не вызывает `Migrate`, `MigrateAsync` или `EnsureCreated` при startup.

## Database least privilege

При первом init пустого volume создаются две application roles:

```text
taskflow_app
  SELECT / INSERT / UPDATE / DELETE
  no schema CREATE

taskflow_migrator
  schema USAGE / CREATE
  migration owner
```

Runtime passwords автоматически генерируются в gitignored `.env.compose.local`. Они не передаются как Docker build args и не попадают в images.

API и migrator используют один configuration key:

```text
ConnectionStrings__Postgres
```

но получают разные credentials.

## Images

Pinned bases:

```text
SDK                 mcr.microsoft.com/dotnet/sdk:10.0.401
ASP.NET runtime      mcr.microsoft.com/dotnet/aspnet:10.0.12
PostgreSQL           postgres:18.6-alpine3.24
Frontend runtime     nginxinc/nginx-unprivileged:1.31.6-alpine3.24
```

TaskFlow release images:

```text
taskflow-api:<commit-sha>
taskflow-migrator:<commit-sha>
taskflow-frontend:<commit-sha>
```

`latest` не используется как release identity.

## Runtime hardening

API, migrator и frontend используют:

```text
non-root USER
read_only root filesystem
tmpfs только для ephemeral /tmp
cap_drop: ALL
no-new-privileges
runtime image без .NET SDK
```

PostgreSQL — единственный stateful service и использует named volume `taskflow-postgres`.

## Same-origin reverse proxy

Blazor продолжает использовать `builder.HostEnvironment.BaseAddress` и относительный `/api/v1/...`.

NGINX:

- раздаёт Blazor assets;
- делает SPA fallback на `index.html`;
- proxy `/api/*` в internal `api:8080`;
- передаёт `X-Forwarded-For` и `X-Forwarded-Proto`;
- является exact trusted proxy для API;
- не публикует API `/health/*` наружу;
- пишет JSON access events в stdout без query string/cookies/request body.

CSP разрешает только same-origin resources и `wasm-unsafe-eval`, необходимый для client-side Blazor WebAssembly.

## API replicas

API не имеет `container_name`, local session store или local Data Protection key directory. Business state и Data Protection keys находятся в PostgreSQL.

Можно поднять дополнительную replica:

```bash
docker compose --env-file .env.compose.local up --scale api=2 -d
```

Sticky session приложению не требуется.

## Container smoke

После запуска:

```bash
./scripts/compose-smoke.sh
```

Сценарий:

```text
HTTPS + antiforgery bootstrap
-> Register
-> Create Project
-> Get Project
-> сохранить antiforgery token
-> restart API
-> /auth/me работает с прежней cookie
-> Project не потерян
-> старый antiforgery token принимается для Project update
-> authenticated /health/ready succeeds
-> Delete Project
-> Logout
```

Для проверки с полностью пустым PostgreSQL volume:

```bash
./scripts/compose-down.sh --volumes
./scripts/compose-up.sh
./scripts/compose-smoke.sh
```

## Остановка

Сохранить БД volume:

```bash
./scripts/compose-down.sh
```

Удалить stack и DB volume:

```bash
./scripts/compose-down.sh --volumes
```

## Полная проверка Stage 14

```bash
./scripts/verify-stage14.sh
```

Static часть проверяет Stages 0–14. На машине с .NET SDK `10.0.401` и Docker затем выполняются locked restore, Release build, xUnit/Testcontainers suite, container build/start и smoke flow.

## Документы

```text
docs/STAGE_14_RATIONALE.md
```

## Следующий этап

Stage 15 — CI pipeline и quality gates: locked restore, analyzers/tests, vulnerability/secret/container scans, container smoke и E2E gates.
