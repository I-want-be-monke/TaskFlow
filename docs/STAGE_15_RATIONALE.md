# Stage 15 — CI pipeline и автоматические quality gates

## 1. Цель

Stage 15 превращает существующие проверки Stages 0–14 из набора локальных команд в обязательный merge/release contract. Основной принцип: критическое архитектурное свойство не должно зависеть от того, вспомнил ли разработчик вручную запустить нужный test или scanner.

## 2. Почему GitHub Actions

Для репозитория выбран один конкретный CI provider — GitHub Actions. Pipeline хранится рядом с кодом в `.github/workflows/ci.yml`, поэтому относится к тому же codebase и version control. Provider-specific orchestration не переносится в Domain/Application и не влияет на runtime architecture.

Workflow запускается на `push` в `main`, pull request и вручную через `workflow_dispatch`.

## 3. Разделение jobs

Pipeline разбит на четыре независимых gate-группы.

### quality

Проверяет воспроизводимость build graph и дешёвые ошибки раньше дорогих container tests:

```text
checkout
-> setup SDK из global.json
-> restore --locked-mode
-> Release build
-> Stages 0–15 architecture/source verifiers
-> Domain unit tests
-> Application unit tests
```

Warnings уже являются errors через repository-wide `Directory.Build.props`, поэтому отдельный lint-only build не нужен.

### integration_p0

Поднимает реальные PostgreSQL containers через существующий Testcontainers suite и запускает весь `TaskFlow.IntegrationTests` project.

В этот gate входят P0-контракты:

- auth / fallback authorization / CSRF / BOLA;
- optimistic concurrency и Project-first race tests;
- real PostgreSQL schema/FK/unique/check constraints;
- empty-DB migration и DbMigrator advisory lock/least privilege;
- API contract и ProblemDetails;
- shared Data Protection/multi-replica behaviour;
- client boundary/UI-state tests.

Не создаётся отдельный SQLite-fast-path: CI использует тот же PostgreSQL model, что и production-like stack.

### supply_chain

Отделён от build/test, чтобы security scanner failure был самостоятельным обязательным status check.

Он выполняет:

1. machine-readable NuGet vulnerability audit;
2. full-history secret scan.

### containers

Запускается только после `quality`, `integration_p0` и `supply_chain`.

Он:

```text
build/start Stage 14 Compose stack
-> Trivy reports + blocking policy
-> state/session/restart container smoke
-> same-origin frontend E2E smoke
```

Таким образом container smoke не маскирует failure более фундаментального build/security gate.

## 4. GitHub Actions supply-chain hardening

Workflow использует только read-only repository token:

```text
permissions:
  contents: read
```

Action references закреплены полными 40-character commit SHA. Комментарий рядом сохраняет human-readable major version.

Security-sensitive checkout использует `persist-credentials: false`. Secret scan требует `fetch-depth: 0`, потому что leak в старом commit остаётся leak даже после удаления строки из HEAD.

Dependabot следит за `github-actions` ecosystem, но автоматическое обновление action SHA не означает автоматический merge: PR должен пройти текущие gates.

## 5. Locked dependencies

Каждый job, которому нужен .NET graph, использует:

```text
dotnet restore TaskFlow.sln --locked-mode
```

Это сохраняет Stage 0 контракт и не позволяет CI незаметно разрешить другую transitive dependency относительно committed lock files.

SDK берётся из `global.json`; workflow не использует floating `10.0.x` как source of truth.

## 6. Unit и integration boundaries

Domain/Application tests запускаются отдельно как быстрый unit gate.

`TaskFlow.IntegrationTests` запускается целиком, а не только happy-path subset. Это осознанно: security/concurrency tests являются архитектурными constraints, а не optional slow tests.

`verify_ci_stage15.py` дополнительно требует физическое наличие ключевых P0 source files, чтобы job с именем `integration_p0` нельзя было оставить зелёным после случайного удаления самих P0 test suites.

## 7. NuGet vulnerability gate

Используется официальный .NET 10 command `dotnet package list` с:

```text
--include-transitive
--vulnerable
--format json
--output-version 1
```

JSON анализируется repository script, а полный report сохраняется как CI artifact.

Policy v1 строгая: **любая известная NuGet vulnerability блокирует CI**. Для маленького учебного application graph исключения проще исправлять обновлением dependency, чем создавать waiver infrastructure раньше необходимости.

Если в будущем потребуется временное принятие риска, waiver должен стать отдельным version-controlled policy с advisory id, owner и expiry date, а не grep-ignore внутри workflow.

## 8. Secret scan

Используется version-pinned official Gitleaks container `v8.30.1`.

Repository mount read-only, report directory — отдельный writable mount. `--redact` запрещает вывод самого найденного secret value в CI log/report diagnostic text.

Scan охватывает Git history, а не только working tree.

## 9. Container vulnerability policy

Используется official `aquasec/trivy:0.70.0` container и локальный Docker Engine через socket mount.

Сканируются все три release image:

```text
taskflow-api:<commit-sha>
taskflow-migrator:<commit-sha>
taskflow-frontend:<commit-sha>
```

Policy разделяет visibility и release blocking:

- `HIGH,CRITICAL` всегда сохраняются в JSON artifacts;
- fixable `CRITICAL` даёт non-zero exit;
- unfixed `CRITICAL` остаётся видимым report finding, но автоматически не блокирует Stage 15.

Причина: base-image CVE без доступного fix невозможно устранить в application code. Это не скрытие finding — он остаётся в report; policy лишь различает actionable и currently-unfixable risk. Изменение policy является version-controlled change.

## 10. Container smoke

Stage 14 `compose-smoke.sh` уже является сильным system smoke:

```text
HTTPS same-origin
register
create/read/update/delete Project
restart API
same auth cookie remains usable
business data survives
pre-restart antiforgery remains usable
ready health works authenticated
logout
```

Stage 15 не создаёт второй несовместимый deployment harness — CI запускает тот же compose contract, который доступен разработчику локально.

## 11. E2E smoke boundary

`ci_e2e_smoke.py` проверяет browser-facing edge после container smoke:

- `/`, `/auth/login`, `/auth/register`, `/projects`, `/tags` отдают SPA shell;
- Blazor bootstrap присутствует;
- `/api/v1/auth/antiforgery` доступен через тот же HTTPS origin.

Это намеренно smoke, а не финальный browser automation suite. Полный browser CRUD scenario остаётся Stage 16 согласно плану.

## 12. Reports

Scanner reports не коммитятся. `artifacts/` добавлен в `.gitignore` и используется как ephemeral CI/local output.

GitHub Actions хранит supply-chain/container artifacts 14 дней. Это diagnostic output, а не application state и не нарушение 12-factor.

## 13. Failure semantics

Все обязательные gates fail-closed:

- restore drift -> fail;
- warning/analyzer -> fail;
- unit/integration test -> fail;
- architecture guard -> fail;
- known NuGet vulnerability -> fail;
- detected secret -> fail;
- fixable CRITICAL image CVE -> fail;
- migration/container startup -> fail;
- state/session restart smoke -> fail;
- edge E2E smoke -> fail.

`if: always()` используется только для upload diagnostics и cleanup, не для обхода gate result.

## 14. Что не реализуется раньше Stage 16

Stage 15 не добавляет полный Playwright browser CRUD suite и не объявляет Definition of Done v1 выполненным. Его задача — создать automation framework, в который Stage 16 добавит финальный browser E2E и итоговые cross-cutting checks.

## 15. Stage 15 handoff

После Stage 15 repository имеет автоматизированный pre-merge/release gate для build, tests, architecture, supply chain и containers. Следующий этап должен добавить финальный browser E2E flow и провести Definition of Done по всей системе, а не менять уже стабилизированный CI boundary без необходимости.
