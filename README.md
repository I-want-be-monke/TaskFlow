# TaskFlow — Stage 16 / v1 Definition of Done

TaskFlow реализован по архитектурным этапам **0–16**. Этот snapshot завершает v1: backend, PostgreSQL, security, concurrency, observability, отдельный DbMigrator, Blazor WASM CRUD UI, production-like Docker topology, CI quality gates и финальный browser E2E / Definition of Done.

## Что добавлено на Stage 16

- `scripts/final_browser_e2e.py` — настоящий Playwright/Chromium E2E;
- обязательный UI flow Register/Login → Project/Task/Tag CRUD → archive/restore → cleanup/logout;
- runtime BOLA `404` и CSRF `400` probes;
- browser `409 version_conflict` + `Reload latest` UX;
- server-side archived Project mutation block check;
- API restart + сохранение cookie-session/business data/pre-restart antiforgery;
- health live/ready runtime checks;
- runtime PostgreSQL least-privilege probe;
- real-value container log redaction check;
- `scripts/verify_final_stage16.py`;
- `scripts/verify-static-stage16.sh` и `scripts/verify-stage16.sh`;
- CI теперь запускает final architecture guard + pinned Playwright `1.63.0` browser E2E;
- `docs/STAGE_16_DOD.md` — финальная матрица DoD;
- `docs/STAGE_16_RATIONALE.md` — rationale финального этапа;
- `Отчёт.md` — корневой отчёт для сдачи.

## Финальный browser flow

```text
Register
-> Logout
-> Login
-> Create Project
-> Edit Project
-> Create Task
-> Edit Task
-> Create Tag
-> Attach Tag to Task
-> Filter/List Tasks
-> page refresh restores session
-> BOLA foreign UUID -> 404
-> unsafe request without CSRF -> 400
-> stale Project version -> 409 + Reload latest
-> Archive Project
-> Task mutation blocked
-> Restore Project
-> restart API
-> same session + data + antiforgery remain valid
-> health + DB least privilege + log redaction
-> Delete Task
-> Delete Tag
-> Delete Project
-> Logout
```

## Финальный CI pipeline

```text
quality
  restore --locked-mode
  -> Release build + analyzers
  -> Stages 0–16 architecture guards
  -> Domain/Application unit tests

integration_p0
  -> full PostgreSQL IntegrationTests
     auth/CSRF/BOLA/multi-replica
     concurrency/deadlock
     schema/migrations/least privilege
     logging/client contracts

supply_chain
  -> NuGet vulnerability gate
  -> full-history Gitleaks

quality + integration_p0 + supply_chain GREEN
  -> containers
     Compose build/start
     -> Trivy
     -> restart/session container smoke
     -> same-origin edge smoke
     -> Playwright Chromium final browser E2E
```

Все обязательные gates fail-closed. `continue-on-error` для release-critical проверок не используется.

## Production-like topology

```text
Browser
  -> https://localhost:8443 (NGINX + Blazor WASM)
       -> /api/*
            -> TaskFlow.Api
                 -> PostgreSQL

TaskFlow.DbMigrator -> PostgreSQL before API rollout
```

API/PostgreSQL host ports наружу не публикуются. Browser authentication использует secure HttpOnly `__Host-TaskFlow.Auth`; Data Protection keys находятся в PostgreSQL, поэтому API не требует sticky session.

## Быстрый запуск

Требуются Git и Docker Compose v2:

```bash
./scripts/compose-up.sh
```

Открыть:

```text
https://localhost:8443/
```

Development certificate self-signed и создаётся при старте frontend container в `/tmp`.

Остановить:

```bash
./scripts/compose-down.sh
```

Удалить также PostgreSQL volume:

```bash
./scripts/compose-down.sh --volumes
```

## Финальная проверка

Только source/architecture/DoD contract:

```bash
./scripts/verify-static-stage16.sh
```

Полный Definition of Done на машине с .NET SDK `10.0.401`, Docker и Python 3:

```bash
python3 -m pip install playwright==1.63.0
python3 -m playwright install --with-deps chromium
./scripts/verify-stage16.sh
```

`verify-stage16.sh` выполняет:

```text
Stages 0–16 static guards
-> locked restore
-> Release build
-> Domain tests
-> Application tests
-> full PostgreSQL IntegrationTests
-> clean-volume production-like Compose startup
-> Stage 14 container smoke
-> edge smoke
-> final Playwright browser E2E
```

## Ключевые архитектурные границы v1

```text
Domain          -> nothing
Application     -> Domain
Infrastructure  -> Application + Domain
Api             -> Application + Infrastructure
DbMigrator      -> Infrastructure
Client          -> Blazor/BCL + own API contracts only
```

Дополнительно:

- один `TaskFlowDbContext` владеет Identity + business + Data Protection schema;
- owner-scoped access для user resources;
- Project-first row lock protocol;
- `Version` optimistic concurrency;
- cookie auth + antiforgery;
- API startup не выполняет migrations;
- browser не хранит bearer/refresh tokens;
- structured JSON logs только stdout/stderr;
- PostgreSQL integration tests, SQLite не используется.

## Финальные документы

- `Отчёт.md` — итоговый отчёт для сдачи;
- `docs/STAGE_16_DOD.md` — матрица Definition of Done;
- `docs/STAGE_16_RATIONALE.md` — решения финального этапа;
- `docs/STAGE_0_RATIONALE.md` … `docs/STAGE_15_RATIONALE.md` — история реализации по этапам.
