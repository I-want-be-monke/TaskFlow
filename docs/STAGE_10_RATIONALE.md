# Stage 10 — Structured logging и observability: rationale

## 1. Цель этапа

Stage 10 превращает уже защищённый HTTP/PostgreSQL backend в диагностируемый production process без нарушения 12-factor Logs и без превращения логов в источник утечки credentials.

Архитектурный contract требует одновременно:

- structured JSON;
- one event per line;
- stdout/stderr only;
- стабильную schema;
- service/release/environment/instance metadata;
- trace/request correlation;
- stable EventId/EventName;
- один completion event на request;
- один основной Error event на unexpected request exception;
- security events;
- slow DB diagnostics без SQL values;
- redaction/allowlist;
- отсутствие sensitive EF logging.

Stage 10 не добавляет remote logging SDK. Shipping, retention, indexing и access control остаются задачей runtime/collector/backend.

## 2. Почему formatter общий и находится в Infrastructure

`Domain` и `Application` не должны зависеть от logging framework. `Infrastructure` уже является внешним adapter layer и может зависеть от `Microsoft.Extensions.Logging`.

Поэтому общий contract расположен в:

```text
TaskFlow.Infrastructure/Observability
```

Там находятся:

```text
TaskFlowLogEvents
TaskFlowJsonConsoleFormatter
TaskFlowJsonConsoleFormatterOptions
TaskFlowLoggingExtensions
```

Это позволяет `TaskFlow.Api` и `TaskFlow.DbMigrator` использовать один сериализатор и один EventId catalog. Если schema изменится, два server processes не разъедутся по несовместимым форматам.

## 3. Почему не использован Serilog/NLog

Для v1 не нужен отдельный logging framework:

- `ILogger<T>` уже является архитектурной абстракцией;
- .NET Console provider поддерживает custom `ConsoleFormatter`;
- нет требования к file sink или synchronous remote sink;
- меньше dependencies и меньше surface area конфигурации;
- проще сохранить locked NuGet graph.

Добавлен только explicit `Microsoft.Extensions.Logging.Console 10.0.12`, централизованно pin'нутый в `Directory.Packages.props`.

## 4. Stable JSON schema

Каждая строка formatter'а имеет обязательный envelope:

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

`schema_version = 1` фиксирует текущий contract для collector/dashboard consumers.

Опциональные поля добавляются только как именованные structured properties. Formatter не сериализует произвольный logger state целиком.

## 5. Allowlist вместо blacklist structured state

Главный механизм защиты — не пытаться перечислить все возможные секреты, а разрешать только известные диагностические поля:

```text
request_id
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
entity UUIDs
process/admin metadata
```

Любой случайный structured field вроде `Password`, `Cookie`, DTO или raw body formatter просто не переносит в JSON output.

Дополнительно `SanitizeMessage` заменяет целиком message, если он выглядит как credential/header/connection-string text. Это defence in depth: корректный production code всё равно не должен строить такие messages.

## 6. Почему exception message не выводится

`Exception.ToString()` включает message. Message может быть сформирован сторонней библиотекой и неожиданно содержать connection data или user input.

Stage 10 пишет для unexpected exceptions:

```text
error_type
exception_stack_trace
```

но не `Exception.Message` и не `Exception.ToString()`.

Это сохраняет серверную stack diagnostics и значительно уменьшает риск утечки secrets.

## 7. Trace/request correlation

ASP.NET Core создаёт входящий `Activity` и поддерживает W3C trace context. Formatter читает:

```text
Activity.Current.TraceId
Activity.Current.SpanId
```

поэтому handler/repository/database events автоматически коррелируются без передачи trace id через Application command models.

Отдельный `request_id` берётся из `HttpContext.TraceIdentifier` и добавляется scope'ом `RequestLoggingMiddleware`.

Это разделяет:

```text
trace_id   -> distributed operation
span_id    -> текущая activity
request_id -> конкретный HTTP request конкретной replica
```

## 8. Один RequestCompleted на запрос

ASP.NET Core framework request-start/finish logs подавлены для production Information flow через category filter `Microsoft.AspNetCore.Hosting.Diagnostics >= Warning`.

Вместо них один собственный middleware пишет:

```text
1001 RequestCompleted
http_method
http_route
http_status_code
duration_ms
trace_id
request_id
user_id?
```

Middleware стоит **снаружи ExceptionHandler**, поэтому после обработанного unexpected exception он видит финальный HTTP 500, а не преждевременный 200.

Для успешных health probes уровень `Debug`. Для 4xx/5xx completion — `Warning`; completion event никогда не является вторым `Error` для request exception.

## 9. Почему raw URL не логируется

Raw path/query может содержать high-cardinality/sensitive values. Stage 10 использует только `RouteEndpoint.RoutePattern.RawText`.

Например:

```text
/api/v1/tasks/{taskId:guid}
```

вместо URL с конкретным UUID и query string.

Если endpoint не определён, записывается стабильное значение `unmatched`, а не raw path.

## 10. Exception logging ровно один раз

`GlobalExceptionHandler` — единственная application-owned точка, которая пишет unexpected request exception как `Error`:

```text
1003 UnhandledException
```

Чтобы ASP.NET Core `ExceptionHandlerMiddleware` не создавал второй console Error event для того же exception, его framework logging category выключена через `LogLevel.None`.

Нижние слои не логируют exception с последующим rethrow. Исключение — adapter может логировать dependency failure, который он **поглощает и преобразует** в typed Result; это уже отдельный outcome, а не duplicate boundary exception.

## 11. EventId ranges

Catalog фиксирует архитектурные диапазоны:

```text
1000-1999 HTTP/API
2000-2999 Application
3000-3999 Persistence/PostgreSQL
4000-4999 Security
5000-5999 Admin/Startup/Migration
6000-6999 external adapters
```

ID и Name являются стабильным машинным contract. Сообщение предназначено для человека и может улучшаться.

## 12. Security event design

`SecurityEventLogger` централизует:

```text
4001 LoginSucceeded
4002 LoginFailed
4003 AccountLockedOut
4004 Logout
4010 AuthorizationDenied
4011 CsrfValidationFailed
4012 RateLimitRejected
4013 SecurityConfigurationError
```

В security event разрешены:

```text
internal user UUID
trusted client IP
route template
outcome
reason_code
```

Запрещены:

```text
username/email
password
Cookie
Authorization
CSRF value
request body
```

Client IP берётся из `RemoteIpAddress` только после Stage 9 `UseForwardedHeaders` trust processing.

## 13. Login/lockout logging без user enumeration

`AuthController` продолжает отдавать одинаковый внешний `auth.invalid_credentials` для invalid password/unknown account/locked account.

Logging при этом различает machine reason через EventId, но не сохраняет supplied username. Поэтому внутреннее расследование возможно, а лог сам не превращается в список зарегистрированных login names.

## 14. CSRF и rate-limit events

`ApiAntiforgeryFilter` пишет `CsrfValidationFailed`, но token не попадает ни в message, ни в structured state.

`RateLimiterOptionsSetup` пишет `RateLimitRejected`, используя уже trusted `RemoteIpAddress`/authenticated UUID. RetryAfter остаётся HTTP response metadata и не требует сохранения request headers.

## 15. SecurityConfigurationError

Stage 9 уже использует `ValidateOnStart`. Stage 10 оборачивает `app.Run()` обработкой `OptionsValidationException` и перед rethrow пишет один `Critical` security event:

```text
4013 SecurityConfigurationError
reason_code = options_validation_failed
```

Полный configuration dump не выводится.

## 16. Slow database operations

`SlowDatabaseCommandInterceptor` добавляется через DI к EF Core DbContext.

Threshold:

```text
Observability__SlowDbThresholdMs
```

Событие:

```text
3002 SlowDatabaseOperation
level = Warning
db_operation = EF CommandSource
duration_ms
```

Код interceptor'а намеренно не обращается к:

```text
DbCommand.CommandText
DbParameter values
```

Тем самым slow-query observability не нарушает sensitive-data policy.

## 17. EF Core framework logging

Сохраняется:

```csharp
EnableSensitiveDataLogging(false)
```

и category:

```text
Microsoft.EntityFrameworkCore.Database.Command -> Warning+
```

Обычные executed SQL commands не создают Information stream. Production logs не заменяют profiler/EXPLAIN.

## 18. Persistence outcome events

`UnitOfWork` поглощает некоторые EF/Npgsql exceptions и преобразует их в typed Result. Поэтому именно здесь допустимо записать outcome, не rethrow'я тот же exception:

```text
3003 ConcurrencyConflict -> Warning
3001 DatabaseUnavailable -> Error
```

Для `DatabaseUnavailable` в log передаётся только безопасный `error_type`, а не exception object/message/connection string.

Expected duplicate Tag conflict остаётся business conflict и не превращается в infrastructure Error.

## 19. Readiness failure

`PostgresReadinessHealthCheck` также пишет `DatabaseUnavailable` при failed connectivity, но не логирует connection string или exception message.

Успешный readiness probe не создаёт Information-шум через request logger.

## 20. Application-significant events

Application layer не превращён в поток "entered/exited handler" logs. Вместо этого transport boundary фиксирует только небольшой набор успешных business outcomes:

```text
2001 ProjectCreated
2002 ProjectArchived
2101 TaskCreated
2102 TaskUpdated
2201 TagCreated
```

В context попадают только entity UUID. Name/title/description и другие user strings не логируются.

Это соответствует правилу low cardinality event taxonomy при сохранении trace/request correlation.

## 21. Startup/shutdown

API пишет:

```text
5001 ApplicationStarted
5002 ApplicationStopping
5003 ApplicationStopped
process_type = api
```

Base formatter автоматически добавляет service/release/environment/instance.

Никакой configuration dump на startup не производится.

## 22. DbMigrator на том же contract

Хотя полноценный migration process — Stage 11, Stage 10 заранее убирает raw `Console.WriteLine` из `TaskFlow.DbMigrator`.

Migrator использует тот же `AddTaskFlowJsonConsole` и пишет lifecycle events с:

```text
service_name = TaskFlow.DbMigrator
process_type = db-migrator
operation_id = <uuid>
```

EventIds `5101-5103` зарезервированы для фактических MigrationStarted/Completed/Failed в Stage 11.

## 23. Instance/release metadata

`ObservabilityOptions` теперь включает optional `InstanceId`.

Разрешение instance id:

```text
Observability__InstanceId
-> TASKFLOW_INSTANCE_ID
-> HOSTNAME
-> MachineName-ProcessId local fallback
```

В production рекомендуется задавать pod/container identity извне.

`ServiceVersion` остаётся runtime config и должен быть immutable release/image/commit identifier.

## 24. Что тестируется

`ObservabilityLoggingTests` проверяет runtime contract formatter'а без внешнего collector:

- одна JSON line;
- обязательные base fields;
- trace/span/request fields;
- EventId/EventName;
- sensitive structured fields dropped;
- exception message не сериализуется;
- request completion ровно один;
- completion 500 не становится Error;
- unexpected exception создаёт один boundary Error и safe RFC7807.

`verify_observability_stage10.py` дополнительно проверяет architecture/source contract:

- EventId catalog;
- middleware position;
- framework duplicate-error suppression;
- security-event wiring;
- DB interceptor не использует CommandText/Parameters;
- DbMigrator shared formatter;
- отсутствие file sink;
- отсутствие `EnableSensitiveDataLogging(true)`;
- отсутствие sensitive tokens непосредственно в log calls.

## 25. Что намеренно не входит в Stage 10

Не добавляются:

- metrics backend;
- OpenTelemetry exporter;
- remote log sink;
- local rotation;
- immutable compliance audit storage;
- client/browser error ingestion endpoint;
- migration execution.

Это сохраняет объём этапа и не смешивает logging contract со следующими deployment/admin задачами.

## 26. Проверка

Полная команда:

```bash
./scripts/verify-stage10.sh
```

Последовательность:

```text
Stages 0-10 static/architecture checks
-> dotnet restore --locked-mode
-> dotnet build Release
-> docker info
-> dotnet test
```

В среде создания snapshot отсутствуют `dotnet` и Docker. Все static checks Stages 0-10 проходят; runtime build/Testcontainers остаются обязательной проверкой на машине с SDK 10.0.401 и Docker.
