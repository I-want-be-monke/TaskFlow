# TaskFlow — Stage 9

TaskFlow реализуется по архитектурным этапам. **Stages 0–9 завершены в этом snapshot**: repository/build foundation, чистый Domain/Application, PostgreSQL persistence/concurrency, HTTP API, browser-session security и production hardening baseline.

## Кратко: что добавлено на Stage 9

- typed Options: `SecurityOptions`, `CorsOptions`, `ProxyOptions`, `ObservabilityOptions`, `RequestLimitOptions`;
- дополнительный `AuthOptions`, потому что `Auth__AllowRegistration` уже был частью configuration contract, но Stage 8 его не применял;
- `ValidateDataAnnotations()`, custom `Validate(...)` и `ValidateOnStart()`;
- Kestrel + middleware request body limit;
- global request timeout через ASP.NET Core RequestTimeouts;
- bounded pagination остаётся `PageSize <= 100` на Application boundary;
- global rate limiting: authenticated user -> user partition, anonymous -> trusted remote IP partition;
- отдельный строгий limiter для `register/login`;
- trusted `X-Forwarded-For` / `X-Forwarded-Proto` только от configured `KnownProxies`/`KnownIPNetworks`;
- в .NET 10 используется `System.Net.IPNetwork`, не obsolete `KnownNetworks`;
- CORS включается только в Development и только по exact origin allowlist;
- security-header contract для reverse proxy/edge;
- `GET /health/live` без PostgreSQL dependency;
- `GET /health/ready` с PostgreSQL connectivity check;
- API startup по-прежнему не выполняет migrations;
- Stage 8 security implementation повторно проверена и усилена конфигурируемым `AllowRegistration`.

## Stage 8 audit

Повторно проверены:

```text
Identity password hashing
HttpOnly + Secure + SameSite=Strict __Host- auth cookie
finite cookie lifetime / non-persistent sign-in
shared PostgreSQL Data Protection key ring
fallback RequireAuthenticatedUser
register/login/logout/me
lockout + generic auth.invalid_credentials
CSRF for POST/PUT/PATCH/DELETE
safe GET/HEAD/OPTIONS
owner-scoped BOLA -> 404
cross-replica cookie/antiforgery contract
no bearer/JWT/browser-storage auth path
```

Нового bypass не найдено. Один реальный недочёт Stage 8 исправлен: `.env.example` уже содержал `Auth__AllowRegistration`, но endpoint регистрации его не учитывал. Теперь при `false` registration возвращает RFC7807 `403 auth.registration_disabled`.

## Configuration contract

Основные environment keys:

```text
ConnectionStrings__Postgres

Auth__AllowRegistration

Security__RateLimit__ApiPermitLimit
Security__RateLimit__LoginPermitLimit
Security__RateLimit__WindowSeconds
Security__RequestTimeoutSeconds
Security__MaxRequestBodyBytes

Cors__AllowedOrigins__0

Proxy__ForwardLimit
Proxy__KnownProxies__0
Proxy__KnownNetworks__0

Observability__ServiceVersion
Observability__SlowDbThresholdMs
```

Все Stage 9 options проходят fail-fast validation при startup.

## Request limits

Default baseline из `.env.example`:

```text
Max request body = 1 MiB
Request timeout  = 30 seconds
PageSize         <= 100
```

Oversized request с известным `Content-Length` получает:

```text
413
application/problem+json
code = http.request_too_large
```

Kestrel также получает тот же `MaxRequestBodySize`, поэтому production server ограничивает body до чтения дорогостоящего payload.

Timeout policy возвращает:

```text
503
code = http.request_timeout
```

и отменяет `HttpContext.RequestAborted`, позволяя handlers/repositories корректно распространить cancellation.

## Rate limiting

Global limiter:

```text
authenticated request -> partition user:<user-id>
anonymous request     -> partition ip:<trusted remote ip>
```

Strict auth limiter дополнительно применяется к:

```text
POST /api/v1/auth/register
POST /api/v1/auth/login
```

Baseline:

```text
API permits    = 100 / 60 sec
Auth permits   = 10 / 60 sec
QueueLimit     = 0
```

При превышении:

```text
429
application/problem+json
code = http.rate_limit_exceeded
Retry-After = <если limiter предоставил metadata>
```

Identity lockout остаётся отдельной второй линией защиты login.

## Trusted proxy contract

Forwarded headers **не обрабатываются вообще**, если не задан ни один trusted proxy/network.

Когда allowlist задан:

```text
X-Forwarded-For
X-Forwarded-Proto
```

принимаются только от `Proxy__KnownProxies` / `Proxy__KnownNetworks`, `ForwardLimit` bounded, header symmetry включена.

Это важно, потому что anonymous rate-limit partition использует уже нормализованный `RemoteIpAddress`, а не напрямую клиентский header.

## CORS

Production deployment — same-origin, поэтому CORS middleware там не включается.

Только `Development` может включить policy из:

```text
Cors__AllowedOrigins__N
```

Origins валидируются как точные HTTP(S) origins. Wildcard `*`, path/query/fragment и trailing slash запрещены. `AllowAnyOrigin + credentials` отсутствует.

## Health checks

```text
GET /health/live
GET /health/ready
```

`/health/live`:

```text
AllowAnonymous
не проверяет PostgreSQL
показывает, что process/HTTP pipeline жив
```

`/health/ready`:

```text
остается под fallback authorization
проверяет TaskFlowDbContext.Database.CanConnectAsync
Healthy -> replica может принимать traffic
Unhealthy -> replica не готова
```

Это сохраняет архитектурный default-deny contract, где anonymous health exception указан только для `/health/live`.

Оба health endpoints исключены из rate limiting, чтобы probe traffic сам не делал replica unhealthy.

## Reverse-proxy security headers

Stage 9 добавляет edge contract:

```text
deploy/reverse-proxy/security-headers.conf
```

Он фиксирует минимум:

```text
Content-Security-Policy
X-Content-Type-Options: nosniff
Referrer-Policy
Permissions-Policy
frame-ancestors 'none'
```

TLS/security headers остаются обязанностью ingress/reverse proxy, как требует архитектура.

## Middleware baseline

```text
ForwardedHeaders
-> ExceptionHandler / ProblemDetails
-> Routing
-> Request body limit
-> RequestTimeouts
-> CORS (Development only)
-> Authentication
-> RateLimiter
-> Authorization
-> MVC antiforgery filter for unsafe requests
-> Controllers
```

## Tests Stage 9

Добавлены/расширены проверки:

```text
invalid DataAnnotation Options -> startup failure
invalid custom CORS Options -> startup failure
request body too large -> 413
strict auth limiter -> 429
request timeout config binding
live works with unavailable PostgreSQL
readiness checker becomes Unhealthy with unavailable PostgreSQL
anonymous /health/ready rejected by fallback auth
forwarded headers accepted only from allowlisted proxy
Auth__AllowRegistration=false blocks register
API startup still contains no Migrate/EnsureCreated
```

PostgreSQL/Testcontainers security/concurrency tests Stages 5–8 остаются частью полного suite.

## Полная проверка

Нужны:

```text
.NET SDK 10.0.401
Docker Engine / Docker Desktop
```

Из корня:

```bash
./scripts/verify-stage9.sh
```

Скрипт выполняет:

```text
Stages 0–9 source/architecture verification
-> dotnet restore --locked-mode
-> Release build
-> Docker availability
-> полный test suite
```

В текущем artifact-контейнере `dotnet` и Docker отсутствуют, поэтому snapshot **не утверждает**, что runtime build/Testcontainers были выполнены здесь. Доступные static checks, JSON/XML validation, Git integrity и archive integrity выполняются перед упаковкой.

Подробное объяснение решений: [`docs/STAGE_9_RATIONALE.md`](docs/STAGE_9_RATIONALE.md).
