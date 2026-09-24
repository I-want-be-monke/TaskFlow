# TaskFlow — Stage 11

TaskFlow реализуется по архитектурным этапам. **Stages 0–11 завершены в этом snapshot**: repository/build foundation, Domain/Application, PostgreSQL persistence/concurrency, REST API, browser-session security, production hardening, structured observability и отдельный one-shot DbMigrator.

## Что добавлено на Stage 11

- полноценный executable `TaskFlow.DbMigrator`;
- тот же `TaskFlowDbContext` и тот же migrations assembly, что использует Infrastructure;
- `ConnectionStrings__Postgres` как единый config key;
- отдельная production роль `taskflow_migrator` вместо runtime `taskflow_app`;
- PostgreSQL session advisory lock с фиксированным TaskFlow key;
- bounded lock wait через `Migrator__LockTimeoutSeconds`;
- `Database.MigrateAsync()` только в admin process;
- стабильные exit codes `0/2/3/4/130`;
- structured `MigrationStarted/MigrationCompleted/MigrationFailed` по Stage 10 JSON schema;
- PostgreSQL least-privilege template;
- integration tests для empty DB, concurrent migrator, failure exit и DDL privilege separation;
- `scripts/verify-stage11.sh`.

## Production process boundary

```text
web-api
  ConnectionStrings__Postgres -> taskflow_app
  runtime DML
  NO schema migrations

admin-migrate
  ConnectionStrings__Postgres -> taskflow_migrator
  one-shot Database.MigrateAsync()
  exit 0 only after successful migration
```

Оба процесса получают configuration извне. Passwords не находятся в repository/image.

## Advisory lock

Migrator выполняет:

```text
open PostgreSQL connection
-> pg_try_advisory_lock(TaskFlow key)
-> bounded retry
-> Database.MigrateAsync()
-> pg_advisory_unlock(TaskFlow key)
-> exit
```

По умолчанию:

```text
Migrator__LockTimeoutSeconds=30
```

Если другой deployment удерживает lock дольше лимита, второй migrator возвращает exit code `3`, и rollout не должен продолжаться.

## Exit codes

```text
0    success
2    invalid/missing configuration
3    migration advisory-lock timeout
4    migration/database failure
130  cancellation
```

## Local migration run

Используйте отдельную privileged local role/connection string для migrator:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=taskflow;Username=taskflow_migrator;Password=LOCAL_SECRET'
export Migrator__LockTimeoutSeconds=30
export Observability__ServiceVersion=local

dotnet run --project src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj
```

Для API передаётся тот же config key, но другой credential:

```text
Username=taskflow_app
```

`deploy/postgres/least-privilege.sql` показывает grants/default privileges. Role passwords в этот файл намеренно не входят.

## Migration logging

DbMigrator использует общий Stage 10 JSON formatter и stdout/stderr:

```text
5001 ApplicationStarted
5101 MigrationStarted
5102 MigrationCompleted
5103 MigrationFailed
5002 ApplicationStopping
5003 ApplicationStopped
```

Migration scope содержит `operation_id`, `release_id`, `process_type=db-migrator`; connection string и credentials не логируются.

## Integration contract

PostgreSQL/Testcontainers tests проверяют:

```text
empty DB -> migration success
second migrator + held advisory lock -> bounded exit 3
invalid DB credential -> non-zero exit
API role CREATE TABLE -> 42501 insufficient_privilege
migrator role -> required DDL succeeds
migration EventId/EventName stay stable
```

API startup по-прежнему не содержит `Migrate`, `MigrateAsync` или `EnsureCreated`.

## Полная проверка Stage 11

```bash
./scripts/verify-stage11.sh
```

Скрипт запускает static architecture checks Stages 0–11, затем:

```bash
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Для runtime части требуются .NET SDK `10.0.401` и Docker.

## Документы

Краткий рабочий контракт находится в этом `README.md`.

Подробное объяснение решений Stage 11 находится в:

```text
docs/STAGE_11_RATIONALE.md
```

## Следующий этап

Stage 12 — Blazor Client foundation: настоящий standalone WASM client, typed API client, cookie/antiforgery boundary и session restoration через `/api/v1/auth/me`.
