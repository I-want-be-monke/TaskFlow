# Stage 11 — отдельный TaskFlow.DbMigrator

## 1. Цель этапа

Stage 11 отделяет изменение PostgreSQL schema от runtime API. После этого deployment может выполнить admin-process миграций до rollout новой версии API, а API продолжает стартовать без `Migrate`, `MigrateAsync` и `EnsureCreated`.

Production process model:

```text
web-api        -> runtime DML, schema не меняет
admin-migrate  -> one-shot TaskFlow.DbMigrator, применяет EF migrations
postgres       -> backing service
```

Это одновременно реализует 12-factor `Admin processes`, уменьшает риск гонок при горизонтальном rollout и позволяет дать API и migrator разные PostgreSQL privileges.

## 2. Dependency boundary

Архитектурный project graph не меняется:

```text
TaskFlow.DbMigrator -> TaskFlow.Infrastructure
```

DbMigrator не ссылается на Application/Domain/Api напрямую. `TaskFlowDbContext`, EF mappings, migration classes и общий JSON logger приходят через Infrastructure.

Persistence NuGet packages не дублируются как direct references в DbMigrator. Это сохраняет правило Stage 5: EF/Npgsql ownership находится в Infrastructure; lock-file DbMigrator получает эти packages транзитивно через project reference.

## 3. Один DbContext и один migrations assembly

Migrator создаёт тот же:

```text
TaskFlow.Infrastructure.Persistence.TaskFlowDbContext
```

и явно указывает:

```csharp
npgsql.MigrationsAssembly(typeof(TaskFlowDbContext).Assembly.FullName)
```

Отдельного admin/Identity DbContext нет. Поэтому API, Identity, Data Protection и business schema используют одну migration history и один источник истины.

## 4. Runtime configuration

И API, и migrator читают один configuration key:

```text
ConnectionStrings__Postgres
```

Но deployment передаёт разные значения:

```text
web-api:
  Username=taskflow_app

admin-migrate:
  Username=taskflow_migrator
```

Credentials не находятся в repository, image layer или command source. `.env.example` содержит только placeholder и пояснение роли.

Дополнительная настройка Stage 11:

```text
Migrator__LockTimeoutSeconds=30
```

Допустимый диапазон при загрузке environment: `1..300` секунд. Некорректная конфигурация завершает process non-zero до migration.

## 5. Advisory lock

Миграции сериализуются PostgreSQL session advisory lock с фиксированным application-specific bigint key:

```text
0x5441534B464C4F57  // ASCII TASKFLOW
```

Алгоритм:

```text
open PostgreSQL connection
-> pg_try_advisory_lock(key)
-> если lock занят: bounded retry
-> timeout -> exit 3
-> определить applied/pending migrations
-> Database.MigrateAsync()
-> pg_advisory_unlock(key)
-> close connection
```

Используется `pg_try_advisory_lock`, а не бесконечный blocking `pg_advisory_lock`: timeout контролируется process через linked `CancellationTokenSource.CancelAfter(...)`.

Lock session-scoped и удерживается на той же connection, на которой EF применяет migrations. Explicit unlock выполняется на той же PostgreSQL session. Если session уже сломана, закрытие connection всё равно освобождает session advisory locks.

Database lock является последней защитой от двух почти одновременных deployment attempts. CI/Kubernetes также не должны намеренно стартовать параллельные migration jobs.

## 6. One-shot exit codes

Стабильные exit codes:

```text
0    success
2    configuration error
3    advisory-lock timeout
4    migration/database failure
130  cancellation/shutdown
```

Deployment должен продолжать rollout API только при `0`.

`Console.CancelKeyPress` переводится в cancellation token; незавершённый process не маскируется success code.

## 7. Structured migration logging

DbMigrator использует тот же `TaskFlowJsonConsoleFormatter`, что API. Никакого `Console.WriteLine`, file sink или отдельного admin-log формата нет.

Lifecycle:

```text
5001 ApplicationStarted
5002 ApplicationStopping
5003 ApplicationStopped
```

Migration events:

```text
5101 MigrationStarted
5102 MigrationCompleted
5103 MigrationFailed
```

Scope:

```text
process_type = db-migrator
operation_id = UUID конкретного запуска
release_id   = Observability__ServiceVersion
```

`MigrationStarted` содержит:

```text
from_schema_version
target_schema_version
```

`MigrationCompleted`:

```text
applied_count
duration_ms
```

`MigrationFailed` содержит safe `error_type`/`failed_migration`; connection string и credentials не логируются. EF sensitive-data logging остаётся выключенным.

## 8. Least privilege

В production предусмотрены две роли:

```text
taskflow_app
  SELECT / INSERT / UPDATE / DELETE
  sequence usage
  no CREATE / ALTER / DROP schema DDL

taskflow_migrator
  schema CREATE/migration privileges
  используется только one-shot admin process
```

`deploy/postgres/least-privilege.sql` задаёт grants/default privileges без password material. Создание LOGIN roles и передача passwords остаются задачей deployment secret/provisioning layer.

Главный security invariant:

```text
компрометация web-api runtime credential не должна давать право менять schema
```

## 9. Integration tests

Stage 11 добавляет отдельный PostgreSQL Testcontainers fixture, который стартует пустую PostgreSQL 18 DB без автоматического `MigrateAsync`.

Проверяются сценарии:

### Empty database

Создаётся fresh database и роль migrator с `USAGE, CREATE` на `public`. `DbMigratorApplication` должен вернуть `0`, а после выполнения должны существовать `projects` и `__EFMigrationsHistory`.

### Bounded concurrent migrator

Первая PostgreSQL session вручную удерживает тот же `MigrationRunner.AdvisoryLockKey`. Второй migrator запускается с 500 ms timeout и обязан вернуть `LockTimeout`, а не зависнуть неопределённо.

### Migration failure

Migrator получает invalid DB password. Process-level runner должен вернуть non-zero (`MigrationFailed`) вместо необработанного success/zero.

### API role cannot DDL

Для роли `taskflow_app_*` явно отзывается `CREATE ON SCHEMA public`. `CREATE TABLE` должен завершаться PostgreSQL `42501 insufficient_privilege`.

### Migrator role can DDL

Роль `taskflow_migrator_*` получает `USAGE, CREATE` и успешно выполняет DDL/migration.

### Stable event catalog

Проверяются `5101/5102/5103`, чтобы dashboards/deployment alerts не зависели от меняющегося message text.

## 10. Почему API всё ещё не мигрирует

Stage 11 verifier сканирует весь `src/` и требует, чтобы production вызов:

```text
Database.MigrateAsync(...)
```

существовал только в:

```text
src/TaskFlow.DbMigrator/MigrationRunner.cs
```

Дополнительно API source проверяется на отсутствие:

```text
MigrateAsync
Database.Migrate
EnsureCreated
EnsureCreatedAsync
```

Integration-test fixtures могут применять migration для подготовки disposable test database — это не production startup behavior.

## 11. Проверка

Static + runtime contract:

```bash
./scripts/verify-stage11.sh
```

Он выполняет все verifier'ы Stages 0–11, затем:

```text
dotnet restore --locked-mode
dotnet build Release
docker info
dotnet test
```

Runtime integration suite требует .NET SDK из `global.json` и Docker для PostgreSQL Testcontainers.

## 12. Граница Stage 11 / Stage 12

Stage 11 не меняет HTTP/API/browser contract. Он завершает backend deployment/admin-process boundary.

Следующий этап строит Blazor Client foundation поверх уже стабильных:

```text
API v1
Identity cookie + CSRF
owner-scoped CRUD
PostgreSQL migrations
structured observability
```
