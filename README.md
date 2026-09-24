# TaskFlow — Stage 15

TaskFlow реализуется по архитектурным этапам. **Stages 0–15 завершены в этом snapshot**: backend, PostgreSQL, security, observability, DbMigrator, Blazor WASM CRUD UI, production-like Docker topology и теперь автоматические CI quality gates.

## Что добавлено на Stage 15

- `.github/workflows/ci.yml`;
- GitHub Actions jobs `quality`, `integration_p0`, `supply_chain`, `containers`;
- locked NuGet restore и Release build с analyzers/warnings-as-errors;
- Domain/Application unit tests;
- полный PostgreSQL/Testcontainers integration suite;
- P0 security/concurrency/migration gate;
- NuGet vulnerability gate;
- Gitleaks scan всей Git history;
- Trivy scan всех трёх TaskFlow images;
- container smoke + HTTPS same-origin E2E smoke;
- immutable SHA pinning GitHub Actions;
- Dependabot для GitHub Actions и NuGet;
- CI/security reports как краткоживущие artifacts;
- `scripts/verify_ci_stage15.py`, `verify-static-stage15.sh`, `verify-stage15.sh`.

## CI pipeline

```text
quality
  locked restore
  -> Release build + analyzers
  -> architecture/source guards
  -> Domain tests
  -> Application tests

integration_p0
  locked restore + build
  -> full PostgreSQL IntegrationTests
     including security/concurrency/migrations

supply_chain
  locked restore
  -> NuGet known-vulnerability gate
  -> full-history Gitleaks

quality + integration_p0 + supply_chain GREEN
  -> containers
     compose build/start
     -> Trivy
     -> container session/restart smoke
     -> HTTPS frontend/API E2E smoke
```

Container job не выполняется, пока предыдущие обязательные gates не завершились успешно.

## CI triggers

Pipeline запускается для:

```text
push -> main
pull_request
workflow_dispatch
```

Default `GITHUB_TOKEN` получает только:

```text
contents: read
```

Checkout credentials не сохраняются в security/container jobs. Actions закреплены полными commit SHA, а не mutable major tags.

## Quality gates

### Build

```bash
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
```

`Directory.Build.props` уже содержит `TreatWarningsAsErrors=true`, analyzers и code-style enforcement, поэтому warning/analyzer regression ломает CI.

### Tests

Unit:

```text
TaskFlow.Domain.Tests
TaskFlow.Application.Tests
```

Integration/P0:

```text
TaskFlow.IntegrationTests
```

Этот suite физически содержит, среди прочего:

```text
AuthSecurityTests
ConcurrencyTests
DbMigratorTests
PostgresSchemaTests
```

То есть owner/BOLA/CSRF, concurrency races, empty-DB migrations и PostgreSQL schema contract входят в обязательный gate.

## Dependency vulnerability policy

`check_nuget_vulnerabilities.py` использует .NET 10 machine-readable command:

```bash
dotnet package list \
  --project TaskFlow.sln \
  --include-transitive \
  --vulnerable \
  --format json
```

**Policy:** любая известная NuGet vulnerability в direct или transitive dependency блокирует CI.

Отчёт сохраняется в:

```text
artifacts/nuget-vulnerabilities.json
```

## Secret scan

Используется pinned official image:

```text
zricethezav/gitleaks:v8.30.1
```

Scan выполняется по полной Git history (`fetch-depth: 0`) и использует `--redact`, чтобы найденное secret value не попадало в CI output.

## Container scanning policy

Используется:

```text
aquasec/trivy:0.70.0
```

Сканируются:

```text
taskflow-api:<commit-sha>
taskflow-migrator:<commit-sha>
taskflow-frontend:<commit-sha>
```

Policy:

```text
HIGH + CRITICAL -> всегда записываются в JSON report
fixable CRITICAL -> blocking CI failure
unfixed CRITICAL -> report, но не блокирует release gate автоматически
```

Это делает исключение явным и воспроизводимым вместо ручного игнорирования отдельных CVE в workflow.

## Container + E2E gates

CI использует тот же Stage 14 production-like stack:

```text
PostgreSQL
-> DbMigrator
-> API
-> HTTPS frontend reverse proxy
```

`scripts/compose-smoke.sh` проверяет auth/antiforgery/CRUD, restart API, сохранение session и business data.

`scripts/ci_e2e_smoke.py` дополнительно проверяет browser-facing boundary:

```text
/
/auth/login
/auth/register
/projects
/tags
```

каждый route должен отдать Blazor SPA shell, а `/api/v1/auth/antiforgery` должен быть доступен через тот же HTTPS origin.

Полный browser CRUD E2E остаётся Stage 16; Stage 15 проверяет, что CI уже не может пропустить сломанный deployable stack.

## Reports

GitHub Actions сохраняет на 14 дней:

```text
supply-chain-reports
container-reports
```

Локальный `artifacts/` gitignored и не является application state.

## Dependabot

`.github/dependabot.yml` еженедельно проверяет:

```text
GitHub Actions
NuGet
```

Обновление версии всё равно должно пройти тот же CI перед merge.

## Локальная проверка Stage 15

Только architecture/source contract:

```bash
./scripts/verify-static-stage15.sh
```

Полный gate на машине с .NET SDK 10.0.401 и Docker:

```bash
./scripts/verify-stage15.sh
```

Он выполняет restore/build/tests, dependency + secret scan, production-like containers, image scan, container smoke и E2E smoke.

## Что намеренно остаётся Stage 16

Stage 15 не дублирует финальный Definition of Done. Следующий этап добавляет полный browser E2E пользовательского CRUD flow и финальную сквозную проверку security/concurrency/12-factor перед сдачей.
