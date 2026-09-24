# Stage 14 — Docker и production-like локальный запуск

## 1. Цель

Stage 14 упаковывает готовые process types в воспроизводимую container topology. Docker не становится новым business layer: Domain/Application/Infrastructure/API/Client contracts Stages 0–13 сохраняются.

Основной порядок:

```text
PostgreSQL ready
-> DbMigrator
-> API
-> Frontend reverse proxy
```

Новый разработчик не создаёт БД schema вручную и не запускает migrations из API.

## 2. Отдельные deployable images

Созданы:

```text
src/TaskFlow.Api/Dockerfile
src/TaskFlow.DbMigrator/Dockerfile
src/TaskFlow.Client/Dockerfile
```

Они соответствуют трём process types:

```text
web-api
admin-migrate
web-frontend
```

Каждый Dockerfile multi-stage: SDK используется только для locked restore/publish, final image содержит только runtime.

## 3. Pinned toolchain/runtime

Build stage использует pinned SDK `10.0.401`, совпадающий с `global.json`.

API/Migrator final stage использует ASP.NET runtime `10.0.12`. Frontend final stage — unprivileged NGINX `1.31.6-alpine3.24`. PostgreSQL compose service — `18.6-alpine3.24`.

TaskFlow images получают release tag из полного Git commit SHA, а не `latest`.

## 4. Build context и secrets

Build context — repository root, потому что project graph зависит от root build artifacts (`global.json`, central package versions, project references, lock files).

`.dockerignore` исключает `.git`, local env files, `bin/obj`, IDE state, test artifacts и generated archives.

Passwords не нужны во время build и не передаются как build args.

## 5. Generated runtime config

`scripts/compose-up.sh` требует clean Git tree. Это связывает image tag с точным source commit.

Если `.env.compose.local` отсутствует, скрипт создаёт его с restrictive permissions и случайными значениями для:

```text
POSTGRES_SUPERUSER_PASSWORD
TASKFLOW_APP_DB_PASSWORD
TASKFLOW_MIGRATOR_DB_PASSWORD
```

Файл gitignored. `.env.compose.example` содержит только placeholders.

## 6. PostgreSQL 18 volume

Для official PostgreSQL 18 data volume монтируется в:

```text
/var/lib/postgresql
```

Persistent state хранится только в named volume `taskflow-postgres`.

API/migrator/frontend root filesystems disposable/read-only.

## 7. Database bootstrap и least privilege

На первом init пустого PostgreSQL volume выполняются:

```text
10-create-roles.sh
20-least-privilege.sql
```

Создаются LOGIN roles:

```text
taskflow_app
taskflow_migrator
```

Role passwords приходят только из runtime environment.

Least privilege contract:

```text
taskflow_app
  schema USAGE
  tables SELECT/INSERT/UPDATE/DELETE
  runtime sequence privileges
  no CREATE ON schema

taskflow_migrator
  schema USAGE/CREATE
  owner будущих migration objects
```

`ALTER DEFAULT PRIVILEGES FOR ROLE taskflow_migrator` гарантирует DML grants API-role для объектов будущих migrations.

Bootstrap superuser credential не передаётся ни API, ни DbMigrator.

## 8. One-shot migration ordering

`migrator` запускается только после `postgres: service_healthy`.

`api` запускается только после `migrator: service_completed_successfully`.

Таким образом migration failure блокирует rollout API. Stage 11 PostgreSQL advisory lock остаётся DB-level защитой от двух параллельных migration attempts.

API startup по-прежнему содержит ноль `Migrate/EnsureCreated` calls.

## 9. Same-origin browser boundary

Browser-facing origin:

```text
https://localhost:8443
```

Frontend NGINX:

```text
/          -> Blazor static files / SPA fallback
/api/*     -> http://api:8080
/health/*  -> 404 externally
```

PostgreSQL и API host ports не публикуются.

Это сохраняет cookie/CSRF модель Stage 8: browser обращается к frontend и API с одним origin, production CORS не нужен.

## 10. Почему local stack использует HTTPS

Auth и antiforgery cookies настроены `Secure=Always`. Ослаблять этот contract в Docker development было бы dev-only bypass.

Поэтому frontend container генерирует self-signed local certificate/private key при startup в `/tmp`.

Key material:

- не хранится в Git;
- не попадает в image layer;
- не передаётся build arg;
- исчезает вместе с ephemeral container `/tmp`.

Production должен использовать normal externally managed TLS certificate на ingress/edge.

## 11. Frontend runtime hardening

Используется `nginxinc/nginx-unprivileged`; process слушает unprivileged ports `8080/8443`.

Compose дополнительно задаёт:

```text
read_only: true
cap_drop: ALL
no-new-privileges:true
tmpfs /tmp
```

Такие же filesystem/capability restrictions применены к API и migrator.

## 12. Trusted proxy

Stage 9 требует explicit trust. Frontend получает фиксированный address по умолчанию `172.30.14.10`, а API получает тот же address как `Proxy__KnownProxies__0`.

`X-Forwarded-*` от других peers не считается trusted.

Subnet и proxy IP можно переопределить через `TASKFLOW_BACKEND_SUBNET`/`TASKFLOW_PROXY_IP`, если default Docker subnet конфликтует на машине разработчика.

## 13. Blazor CSP

Strict baseline уточнён для WebAssembly:

```text
script-src 'self' 'wasm-unsafe-eval'
connect-src 'self'
style-src 'self'
object-src 'none'
frame-ancestors 'none'
```

`wasm-unsafe-eval` нужен клиентскому Blazor runtime; arbitrary external scripts/origins не разрешаются.

## 14. Frontend logs

NGINX пишет access events в stdout JSON. Он не логирует query string, cookies, request body или auth headers.

Поскольку reverse proxy не знает ASP.NET route templates, он не подделывает `http_route`; используется дополнительное безопасное поле `http_path=$uri` без query string.

`service_version` получает non-secret immutable Git SHA при image build.

## 15. API image и health

API final image содержит только ASP.NET runtime и `curl` для internal `/health/live` probe.

API не публикует host port. Frontend — единственная browser-facing точка.

Readiness остаётся защищён fallback authorization и не публикуется через frontend. Smoke test проверяет `/health/ready` внутри container network с уже выданной authenticated cookie.

## 16. Stateless replicas

API service не задаёт `container_name`, поэтому Compose может запускать несколько replicas.

Все replicas используют:

```text
same PostgreSQL
same Identity state
same Data Protection key table
same immutable release image/config
```

Application sticky session не нужен. Multi-replica cookie/Data Protection behavior отдельно покрыт Stage 8 integration tests.

## 17. Container smoke

`scripts/compose_smoke.py` использует реальный HTTPS reverse-proxy boundary и cookie jar:

```text
GET antiforgery
-> Register
-> refresh antiforgery
-> Create/Get Project
-> получить token до restart
-> restart API
-> прежняя auth cookie работает через /auth/me
-> Project сохранён
-> token, выданный до restart, принимается для PUT Project
-> internal authenticated readiness = healthy
-> Delete Project
-> Logout
```

Это проверяет одновременно Secure cookie, CSRF lifecycle, shared Data Protection key persistence, PostgreSQL state и disposability API process.

Для migration from empty volume:

```bash
./scripts/compose-down.sh --volumes
./scripts/compose-up.sh
./scripts/compose-smoke.sh
```

Успешный CRUD после clean startup означает, что one-shot migrator применил schema до API startup.

## 18. Почему smoke — отдельный script

Container smoke проверяет assembled release topology, а не один .NET assembly. Он должен выполняться после image build и потому не прячется внутри unit tests.

На Stage 15 этот script станет CI gate.

## 19. Static Stage 14 guard

`scripts/verify_containers_stage14.py` проверяет без Docker daemon:

- три multi-stage Dockerfile;
- non-SDK final stages;
- non-root runtime;
- pinned image versions;
- compose service inventory;
- startup dependency order;
- отсутствие public API/PostgreSQL ports;
- read-only/capability hardening;
- separate app/migrator DB credentials;
- exact trusted proxy;
- same-origin `/api` proxy;
- CSP/HTTPS runtime generation;
- persistent PostgreSQL 18 volume path;
- commit-SHA release tags;
- отсутствие literal DB passwords;
- smoke-flow inventory;
- shared PostgreSQL Data Protection;
- отсутствие migrations в API startup.

## 20. Полная проверка

```bash
./scripts/verify-stage14.sh
```

Static Stages 0–14 выполняются всегда. Runtime часть требует .NET SDK `10.0.401` и Docker/Compose: locked restore, Release build, xUnit/PostgreSQL tests, container startup и smoke flow.

## 21. Граница Stage 14 / Stage 15

Stage 14 создаёт runnable release artifacts и smoke commands.

Stage 15 автоматизирует их как CI quality gates и добавляет dependency/secret/container scans и E2E gate.
