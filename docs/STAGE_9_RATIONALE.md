# Stage 9 — Security hardening, limits, health and configuration

## 1. Цель

Stage 9 не добавляет новых бизнес-функций. Его задача — сделать уже защищённый Stage 8 API пригодным для production deployment за reverse proxy: конфигурация должна быть fail-fast, ресурсы bounded, abuse ограничен, forwarded headers нельзя spoof'ить, а platform должна различать liveness и readiness.

## 2. Результат повторного аудита Stage 8

Перед hardening повторно просмотрены Composition Root, Identity cookie, `AuthController`, `HttpContextCurrentActor`, antiforgery filter и security integration tests.

Подтверждено:

- Identity остаётся единственным password hashing mechanism;
- auth cookie имеет `__Host-`, `HttpOnly`, `Secure=Always`, `SameSite=Strict`, `Path=/`;
- login/register не возвращают bearer/refresh token;
- login использует `lockoutOnFailure=true`;
- любой non-success login result возвращает одинаковый `auth.invalid_credentials`;
- fallback policy закрывает business controllers;
- current actor читает authenticated claims, не request headers/query;
- unsafe verbs проходят `IAntiforgery.ValidateRequestAsync`;
- owner-scoped repositories/queries сохраняют BOLA semantics `foreign -> 404`;
- Data Protection key ring общий через PostgreSQL.

Найден один configuration gap: `Auth__AllowRegistration` существовал в `.env.example`, но Stage 8 endpoint его не использовал. Stage 9 добавляет `AuthOptions` и тест `RegistrationCanBeDisabledByConfiguration`. Это не меняет browser-session protocol, а завершает уже объявленный configuration contract.

## 3. Typed Options и fail-fast

Архитектура требует пять typed option models:

```text
SecurityOptions
CorsOptions
ProxyOptions
ObservabilityOptions
RequestLimitOptions
```

Они реализованы напрямую. Дополнительно введён маленький `AuthOptions`, чтобы закрыть обнаруженный Stage 8 configuration gap.

Каждая группа проходит startup validation. Используются оба уровня:

```text
ValidateDataAnnotations()
custom Validate(...)
ValidateOnStart()
```

DataAnnotations задают простые bounds. Custom validators выражают cross-field/semantic правила, например:

```text
LoginPermitLimit <= ApiPermitLimit
CORS origin is exact HTTP(S) origin
no wildcard CORS
Proxy KnownProxies parse as IPAddress
Proxy KnownNetworks parse as CIDR IPNetwork
ServiceVersion has no surrounding whitespace
```

Fail-fast принцип важен: pod с некорректной security/configuration не должен становиться ready и обнаруживать ошибку только на первом пользовательском запросе.

## 4. Почему RequestLimitOptions bind к Security section

Исходный environment contract уже определяет:

```text
Security__RequestTimeoutSeconds
Security__MaxRequestBodyBytes
```

Поэтому `RequestLimitOptions` bind'ится к той же секции `Security`, но читает только свои свойства. `SecurityOptions` из той же секции читает `RateLimit`.

Это позволяет сохранить имена environment variables и одновременно разделить C# responsibilities.

## 5. Request body limits

Защита двухуровневая.

### Kestrel

`KestrelServerOptions.Limits.MaxRequestBodySize` задаёт hard server limit для production traffic.

### API middleware

`RequestBodyLimitMiddleware`:

- выставляет `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize`, когда feature writable;
- до MVC/model binding проверяет известный `Content-Length`;
- deterministic oversized request возвращает RFC7807 `413 http.request_too_large`.

Middleware нужен ещё и для deterministic integration test под TestServer, где Kestrel transport не участвует.

## 6. Request timeouts

ASP.NET Core `RequestTimeouts` используется как global default policy.

Timeout берётся из validated `RequestLimitOptions` и возвращает:

```text
503 Service Unavailable
code = http.request_timeout
```

RequestTimeouts отменяет `HttpContext.RequestAborted`; Application/Infrastructure уже принимают `CancellationToken`, поэтому Stage 9 использует существующий end-to-end cancellation contract вместо собственного timeout thread/task mechanism.

## 7. Bounded pagination

Stage 9 не переносит pagination limit в HTTP middleware, потому что правильная граница уже создана в Stage 2:

```text
Pagination.MaximumPageSize = 100
```

Projects/Tasks/Tags validate pagination в Application. Таким образом прямой вызов handler также bounded, а ограничение нельзя обойти другим transport adapter.

## 8. Rate limiting

Используется ASP.NET Core Rate Limiting middleware.

### Global limiter

После Authentication partition key выбирается так:

```text
authenticated -> server-issued user id claim
anonymous     -> HttpContext.Connection.RemoteIpAddress
```

User id не является unbounded client-controlled arbitrary string: TaskFlow принимает только валидный Guid claim authenticated principal.

Anonymous IP вычисляется уже после ForwardedHeaders middleware, поэтому при reverse proxy используется только доверенное forwarded значение.

### Login/register limiter

`register` и `login` получают named `authentication-strict` limiter, partitioned по client IP.

Этот limiter выполняется дополнительно к global limiter. Identity lockout остаётся независимым secondary control.

Queue выключена (`QueueLimit=0`), чтобы приложение не копило burst requests в памяти.

Rejected request получает `429 http.rate_limit_exceeded`; при наличии limiter metadata добавляется `Retry-After`.

## 9. Trusted forwarded headers

`X-Forwarded-*` напрямую доверять нельзя, потому что иначе клиент может подделать scheme/IP и обойти IP-based controls.

Stage 9 configuration поддерживает:

```text
Proxy:KnownProxies
Proxy:KnownNetworks
Proxy:ForwardLimit
```

На .NET 10 используется:

```text
System.Net.IPNetwork
ForwardedHeadersOptions.KnownIPNetworks
```

а obsolete `KnownNetworks` намеренно не используется.

Особенно важно поведение empty allowlist:

```text
нет configured proxy/network
-> ForwardedHeaders = None
-> forwarded headers полностью игнорируются
```

Мы не очищаем trusted collections и одновременно не включаем headers, потому что конфигурация “accept any forwarder” небезопасна.

## 10. CORS

Production architecture same-origin, поэтому CORS middleware не нужен и не запускается.

В `Development` разрешён exact allowlist. Custom validation запрещает:

```text
*
origin with path
origin with query/fragment
trailing slash
non-HTTP(S) scheme
duplicate origins
```

При exact origins разрешены credentials, headers и methods. Комбинации `AllowAnyOrigin + AllowCredentials` в коде нет.

## 11. Health semantics

### `/health/live`

Liveness не содержит DB check (`Predicate = _ => false`) и явно `AllowAnonymous`, как указано в default-deny разделе архитектуры.

Это значит: даже при полном outage PostgreSQL живой HTTP process продолжает отвечать liveness probe.

### `/health/ready`

Readiness выбирает только checks с tag `ready`; сейчас это `PostgresReadinessHealthCheck` с `Database.CanConnectAsync`.

`/health/ready` **не получает `AllowAnonymous`**. Это намеренно следует архитектурному разделу default deny, где anonymous exception перечислен только для `/health/live`.

Integration contract поэтому проверяет два аспекта отдельно:

- anonymous ready endpoint отклоняется fallback policy;
- сам PostgreSQL readiness check становится `Unhealthy`, когда DB недоступна.

Deployment/platform, которому нужен HTTP readiness endpoint, должен обращаться к нему через доверенный внутренний/authenticated probe channel. Если архитектурное решение будет изменено на anonymous internal-only readiness, это нужно сначала явно изменить в source-of-truth architecture.

## 12. Health endpoints и rate limiting

Оба health routes получают `DisableRateLimiting()`.

Probe cadence — инфраструктурный control plane traffic. Если включить обычный per-IP limiter, kube/ingress probes могут сами создать ложный `429` и вывести здоровую replica из traffic.

Authorization semantics при этом не изменяются.

## 13. Security headers на edge

Архитектура помещает browser security headers на TLS-terminating reverse proxy/ingress, а не в application code.

Stage 9 добавляет:

```text
deploy/reverse-proxy/security-headers.conf
```

с baseline:

```text
Content-Security-Policy
X-Content-Type-Options: nosniff
Referrer-Policy
Permissions-Policy
frame-ancestors 'none'
```

Полный reverse-proxy/container deployment остаётся Stage 14; сейчас файл является проверяемым edge contract.

## 14. Middleware order

Pipeline зафиксирован так:

```text
ForwardedHeaders
ExceptionHandler
Routing
RequestBodyLimit
RequestTimeouts
CORS (Development only)
Authentication
RateLimiter
Authorization
MVC authorization filter / Antiforgery
Controllers
```

Почему именно так:

- proxy normalization должна произойти до IP-based rate limit;
- exception boundary оборачивает application pipeline;
- route metadata нужна timeout/CORS/rate/authorization components;
- body limits действуют до model binding;
- Authentication выполняется до global user partition limiter;
- RateLimiter выполняется до Authorization/handler work;
- antiforgery остаётся внутри MVC authorization phase для unsafe controller actions.

## 15. API startup не владеет migrations

Stage 9 verifier повторно запрещает в `TaskFlow.Api/Program.cs`:

```text
Database.Migrate
MigrateAsync
EnsureCreated
```

Schema lifecycle остаётся обязанностью отдельного `DbMigrator`, как было определено ранее.

## 16. Tests

Stage 9 добавляет integration/source contracts:

```text
invalid annotation option -> startup failure
invalid custom CORS origin -> startup failure
oversized Content-Length -> 413 RFC7807
strict auth limiter -> 429 RFC7807
request timeout option -> configured default policy
live -> 200 with unavailable PostgreSQL
anonymous ready -> 401 by fallback policy
PostgreSQL readiness -> Unhealthy when DB unavailable
trusted proxy -> X-Forwarded-For consumed
untrusted proxy -> X-Forwarded-For ignored
registration can be disabled by config
```

`verify_hardening_stage9.py` дополнительно фиксирует source architecture, middleware order, absence of wildcard CORS, `KnownIPNetworks`, edge headers, pagination bound и отсутствие runtime migrations.

## 17. Проверка в текущей среде

В artifact environment доступны Python/Git, но отсутствуют `dotnet` и Docker.

Поэтому здесь выполняются и могут быть подтверждены:

```text
Stages 0–9 Python/source verifiers
JSON/XML parsing
project/dependency checks
Git diff/fsck/status
ZIP integrity
```

Но `dotnet restore`, compiler/analyzer execution и Testcontainers runtime suite должны быть запущены командой:

```bash
./scripts/verify-stage9.sh
```

на машине с .NET SDK 10.0.401 и Docker.
