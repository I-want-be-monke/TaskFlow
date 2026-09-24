# TaskFlow — Stage 10

TaskFlow реализуется по архитектурным этапам. **Stages 0–10 завершены в этом snapshot**: repository/build foundation, Domain/Application, PostgreSQL persistence/concurrency, HTTP API, browser-session security, production hardening и structured observability.

## Кратко: что добавлено на Stage 10

- единый structured JSON console formatter для API и DbMigrator;
- `one event = one JSON line`, только stdout/stderr, без file sink;
- стабильная log schema с release/environment/instance metadata;
- W3C `trace_id` / `span_id` + `request_id` correlation;
- стабильный `TaskFlowLogEvents` catalog и диапазоны EventId;
- один `RequestCompleted` event на HTTP request;
- один основной `UnhandledException` Error event на API boundary;
- security events для login/lockout/logout/authz/CSRF/rate-limit/config failures;
- slow PostgreSQL operation logging без SQL text/parameter values;
- application-significant events для Project/Task/Tag;
- explicit suppression framework request/exception duplication и noisy EF command logs;
- allowlist structured fields + sensitive-message redaction;
- `Observability__InstanceId` для replica/process identity;
- logging contract tests и `scripts/verify-stage10.sh`.

## Production log schema

Каждая строка — отдельный JSON object. Базовые поля:

```text
schema_version
@timestamp
log_level
event_id
event_name
message
service_name
service_version
deployment_environment
instance_id
trace_id
span_id
request_id
```

Опционально, только когда применимо:

```text
user_id
http_method
http_route
http_status_code
duration_ms
error_type
security_event_id
outcome
reason_code
client_ip
db_operation
application_event_id
project_id / task_id / tag_id
process_type / operation_id
```

`http_route` — route template, например `/api/v1/tasks/{taskId:guid}`, а не raw URL. Query string не входит в log contract.

## EventId catalog

```text
1000-1999  HTTP / API lifecycle
2000-2999  Application-significant events
3000-3999  Persistence / PostgreSQL
4000-4999  Authentication / Authorization / Security
5000-5999  Admin / Migration / Startup
6000-6999  Future external adapters
```

Текущий стабильный минимум:

```text
1001 RequestCompleted
1002 RequestRejected
1003 UnhandledException

2001 ProjectCreated
2002 ProjectArchived
2101 TaskCreated
2102 TaskUpdated
2201 TagCreated

3001 DatabaseUnavailable
3002 SlowDatabaseOperation
3003 ConcurrencyConflict

4001 LoginSucceeded
4002 LoginFailed
4003 AccountLockedOut
4004 Logout
4010 AuthorizationDenied
4011 CsrfValidationFailed
4012 RateLimitRejected
4013 SecurityConfigurationError

5001 ApplicationStarted
5002 ApplicationStopping
5003 ApplicationStopped
5101 MigrationStarted
5102 MigrationCompleted
5103 MigrationFailed
```

`event_id` и `event_name` считаются контрактом; текст `message` может меняться без поломки dashboards/alerts.

## Request correlation

ASP.NET Core создаёт W3C `Activity` для входящего HTTP request. Formatter автоматически пишет:

```text
trace_id
span_id
```

`RequestLoggingMiddleware` добавляет scope с:

```text
request_id
http_method
```

и после завершения request пишет один `RequestCompleted` с route template, status, duration и `user_id`, если authenticated actor известен.

Успешные `/health/live` и `/health/ready` логируются только на `Debug`, чтобы probes не создавали Information-шум; failure остаётся видимым.

## Exception policy

Unexpected request exception логируется централизованно в `GlobalExceptionHandler`:

```text
1003 UnhandledException
level = Error
error_type
trace_id / request_id
safe route template
stack trace
```

ASP.NET Core framework-category `ExceptionHandlerMiddleware` отключён от console stream, поэтому тот же exception не дублируется вторым Error event.

`RequestCompleted` для HTTP 500 остаётся `Warning`, а не вторым `Error`.

Exception message намеренно не сериализуется formatter'ом. Клиент продолжает получать безопасный RFC7807 без stack trace.

## Sensitive-data policy

Structured fields принимаются formatter'ом по allowlist. Не являются частью log contract:

```text
request/response body
query string
Authorization
Cookie / Set-Cookie
antiforgery token
password / password hash
Data Protection keys
connection string / DB password
secret environment values
raw files
```

Дополнительная message-redaction блокирует сообщения, похожие на credentials/cookie/connection-string data.

EF Core остаётся с:

```csharp
EnableSensitiveDataLogging(false)
```

и category `Microsoft.EntityFrameworkCore.Database.Command` ограничена `Warning+`, поэтому обычный SQL stream не пишется на Information.

## Security events

Security logging использует только server-known/safe context:

```text
security_event_id
event_name
trace_id
request_id
user_id?          # только UUID
client_ip?        # после trusted proxy processing
http_route
outcome
reason_code
```

Login failure не логирует username/password. CSRF event не логирует token. Rate-limit event не доверяет raw `X-Forwarded-For`: используется уже нормализованный `RemoteIpAddress` после Stage 9 trusted-proxy middleware.

## Database observability

`SlowDatabaseCommandInterceptor` получает threshold из:

```text
Observability__SlowDbThresholdMs
```

и пишет `3002 SlowDatabaseOperation` только с:

```text
db_operation
duration_ms
trace_id
```

Он намеренно не читает `CommandText`, parameters или bind values.

`UnitOfWork` пишет typed persistence outcomes:

```text
3003 ConcurrencyConflict  -> Warning, ожидаемый optimistic conflict
3001 DatabaseUnavailable -> Error, dependency failure преобразован в typed Result
```

Поскольку эти exceptions поглощаются/преобразуются persistence boundary, лог здесь не дублирует rethrow на API boundary.

## Application-significant events

Не логируется вход/выход каждого handler. Добавлен только небольшой стабильный набор значимых success events:

```text
ProjectCreated
ProjectArchived
TaskCreated
TaskUpdated
TagCreated
```

В события попадают только внутренние UUID, не user-entered names/descriptions.

## API и DbMigrator используют один формат

Shared implementation находится в:

```text
src/TaskFlow.Infrastructure/Observability/
```

`TaskFlow.Api` запускает formatter как `service_name = TaskFlow.Api`.

`TaskFlow.DbMigrator` уже переведён с `Console.WriteLine` на тот же formatter с `service_name = TaskFlow.DbMigrator` и `operation_id`. Сами migration execution events `5101-5103` будут использованы при реализации migration process на Stage 11.

Никаких local log files, rotation или synchronous remote log sink в приложении нет.

## Configuration

Stage 10 observability keys:

```text
Observability__ServiceVersion=dev
Observability__InstanceId=taskflow-api-local
Observability__SlowDbThresholdMs=500
```

`ServiceVersion` должен быть immutable release id/commit SHA в deployment. `InstanceId` обычно задаётся pod/container environment; если он не указан, formatter использует `TASKFLOW_INSTANCE_ID`, затем `HOSTNAME`, затем process-local fallback.

## Tests Stage 10

Добавлены проверки:

```text
one JSON event per line
required fields stable
trace/span/request correlation fields
EventId/EventName catalog stable
RequestCompleted emitted once
HTTP 500 completion is not second Error event
one explicit unexpected-exception Error event at API boundary
password absent
cookie absent
antiforgery token absent
connection string absent
exception message not serialized
DbMigrator uses same formatter
slow DB logger does not inspect SQL/parameters
no file sink / sensitive EF logging
```

## Полная проверка

Нужны:

```text
.NET SDK 10.0.401
Docker Engine / Docker Desktop
```

Из корня:

```bash
./scripts/verify-stage10.sh
```

Скрипт выполняет:

```text
Stages 0–10 source/architecture verification
-> dotnet restore --locked-mode
-> Release build
-> Docker availability
-> полный test suite
```

В текущем artifact-контейнере `dotnet` и Docker отсутствуют. Поэтому snapshot не утверждает, что runtime build/Testcontainers были выполнены здесь. Все доступные static checks Stages 0–10 проходят перед упаковкой.

Подробное объяснение решений: [`docs/STAGE_10_RATIONALE.md`](docs/STAGE_10_RATIONALE.md).
