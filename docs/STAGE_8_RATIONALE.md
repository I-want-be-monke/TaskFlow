# Stage 8 — Authentication, authorization and CSRF

## 1. Цель этапа

Stage 8 превращает Stage 7 HTTP API из transport-only boundary в безопасную browser-session модель.

Архитектурный контракт:

```text
ASP.NET Core Identity
+ HttpOnly same-origin cookie
+ shared Data Protection key ring
+ fallback authorization policy
+ antiforgery token for unsafe requests
+ owner-scoped Application access
```

На этом этапе не вводятся bearer/refresh tokens, custom password hashing, session storage или отдельный auth database.

## 2. Почему ASP.NET Core Identity

Identity уже предоставляет критические security primitives:

```text
password hashing
password validation
security stamp
user normalization
failed access counter
lockout
cookie principal creation
UserManager / SignInManager
```

TaskFlow не реализует собственные hash/salt/token algorithms.

`ApplicationUser` остаётся Infrastructure type. Domain/Application не получают ссылки на Identity, claims, cookies или `HttpContext`.

## 3. Почему используется IdentityCore, а не roles/UI stack

TaskFlow v1 не имеет roles и встроенного Razor Identity UI.

Поэтому Composition Root использует:

```text
AddIdentityCore<ApplicationUser>()
.AddSignInManager()
.AddEntityFrameworkStores<TaskFlowDbContext>()
```

и отдельно подключает Identity cookie schemes.

Это даёт UserManager/SignInManager/store/cookie security без ненужной role schema и UI.

## 4. Один TaskFlowDbContext остаётся владельцем auth schema

Stage 5 уже создал:

```text
auth_users
auth_user_claims
auth_user_logins
auth_user_tokens
data_protection_keys
```

Stage 8 не создаёт второй DbContext и не добавляет вторую migration history.

Identity использует тот же `TaskFlowDbContext`, что и business schema, сохраняя архитектурный контракт одного владельца схемы v1.

## 5. Authentication cookie

Main application cookie настроен как:

```text
Name         __Host-TaskFlow.Auth
HttpOnly     true
Secure       Always
SameSite     Strict
Path         /
ExpireTime   8 hours
Sliding      true
```

`__Host-` prefix выбран потому, что cookie является host-only, Secure и имеет `Path=/`.

Register/login вызывают:

```text
SignInAsync(..., isPersistent: false)
```

То есть `Remember me` отсутствует в v1. Cookie не является долговременным пользовательским credential, а authentication ticket имеет конечный lifetime.

## 6. Почему browser не получает bearer token

В архитектуре v1 browser authentication — cookie-based.

Поэтому auth endpoints не возвращают:

```text
access token
refresh token
JWT
bearer token
```

Browser JavaScript не читает authentication cookie благодаря `HttpOnly`.

Для восстановления UI auth-state будущий Blazor client будет использовать:

```text
GET /api/v1/auth/me
```

## 7. Register

`POST /api/v1/auth/register`:

```text
validate HTTP DTO
-> Trim user name
-> create ApplicationUser
-> UserManager.CreateAsync(user, password)
-> Identity hashes password and initializes security data
-> SignInManager.SignInAsync(isPersistent: false)
-> 201 AuthUserResponse
```

`OwnerUserId` или server-controlled Identity fields не принимаются из клиента.

Identity validation errors возвращаются через RFC7807 `400` с code:

```text
auth.registration_failed
```

## 8. Login and user enumeration

Login использует:

```text
PasswordSignInAsync(
    userName,
    password,
    isPersistent: false,
    lockoutOnFailure: true)
```

Любой non-success result — неправильный password, отсутствующий user, locked out и т.п. — получает одинаковый внешний ответ:

```text
401
auth.invalid_credentials
Invalid credentials.
```

Это не даёт внешнему клиенту различать существование аккаунта по login response.

Identity `AccessFailedCount` остаётся внутренним механизмом и увеличивается при неверном password.

## 9. Lockout

Identity options сохраняют:

```text
Lockout.AllowedForNewUsers = true
```

а login всегда использует:

```text
lockoutOnFailure = true
```

Stage 8 фиксирует воспроизводимый baseline: `MaxFailedAccessAttempts = 5` и `DefaultLockoutTimeSpan = 15 минут`. Это security policy текущей реализации; на Stage 9 её можно вынести в typed configuration без изменения auth flow.

## 10. Logout и Me

`POST /auth/logout`:

```text
authenticated
+ valid antiforgery token
-> SignOutAsync
-> 204
```

`GET /auth/me`:

```text
authenticated
-> UserManager.GetUserAsync(HttpContext.User)
-> AuthUserResponse
```

`me` не декодирует cookie вручную и не доверяет client-supplied user id.

## 11. Fallback authorization: default deny

Вместо `[Authorize]` на каждом business action используется:

```text
FallbackPolicy = RequireAuthenticatedUser
```

Это означает: новый endpoint автоматически закрыт, если разработчик явно не добавил `[AllowAnonymous]`.

На Stage 8 `[AllowAnonymous]` есть только у:

```text
GET  /auth/antiforgery
POST /auth/register
POST /auth/login
```

`logout`, `me`, Projects, Tasks, Tags и TaskTag остаются authenticated по умолчанию.

## 12. Object-level authorization остаётся в Application/persistence

Authentication отвечает только на вопрос:

```text
кто пользователь?
```

Она не заменяет object-level authorization.

Project/Task/Tag lookup по-прежнему выполняется owner-scoped:

```text
currentActor.UserId
-> repository/query owner predicate
```

Чужой UUID остаётся неотличим от отсутствующего объекта и возвращает `404`.

Таким образом BOLA нельзя обойти просто наличием валидной auth cookie.

## 13. Antiforgery architecture

Cookie authentication автоматически прикладывается браузером к same-origin requests, поэтому unsafe operations требуют отдельного CSRF proof.

Настройка:

```text
HeaderName = X-XSRF-TOKEN
Cookie     = __Host-TaskFlow.Antiforgery
HttpOnly   = true
Secure     = Always
SameSite   = Strict
Path       = /
```

Antiforgery cookie может быть HttpOnly: JavaScript не должен читать её. Browser автоматически отправляет cookie, а JS хранит только отдельный request token.

## 14. Почему request token выдаётся отдельным GET endpoint

`GET /api/v1/auth/antiforgery` вызывает:

```text
IAntiforgery.GetAndStoreTokens(HttpContext)
```

Сервер:

```text
устанавливает antiforgery cookie
+ возвращает request token в JSON
```

Будущий Blazor client хранит request token только в памяти вкладки и добавляет его к unsafe same-origin requests.

## 15. Какие методы требуют CSRF

Safe set ограничен:

```text
GET
HEAD
OPTIONS
```

Все остальные methods проверяются через:

```text
IAntiforgery.ValidateRequestAsync
```

Поэтому POST/PUT/PATCH/DELETE защищены автоматически, включая endpoints, которые будут добавлены позже.

## 16. Почему используется ApiAntiforgeryFilter

Стандартная antiforgery validation должна быть интегрирована с общим API error contract.

TaskFlow использует небольшой global authorization filter:

```text
safe method -> skip
unsafe method -> ValidateRequestAsync
failure -> RFC7807 400
```

Ответ:

```text
code = security.csrf_validation_failed
content-type = application/problem+json
```

Это сохраняет общий Stage 7 контракт: expected API failures не меняют произвольным образом формат ответа.

## 17. Antiforgery lifecycle при auth-state change

Antiforgery request token связан с текущим user context.

Поэтому lifecycle:

```text
initial anonymous page
-> fetch token

successful register/login
-> old request token считается устаревшим
-> fetch authenticated token

successful logout
-> clear old token
-> fetch anonymous token
```

Integration test явно проверяет, что anonymous request token после register не используется для authenticated mutation и что новый token решает проблему.

## 18. Shared Data Protection

Auth cookies и antiforgery tokens защищаются ASP.NET Core Data Protection.

Если key ring хранить на локальном filesystem одного pod, replica B не сможет гарантированно читать protected state replica A.

Поэтому:

```text
AddDataProtection()
.SetApplicationName("TaskFlow")
.PersistKeysToDbContext<TaskFlowDbContext>()
```

Key ring находится в PostgreSQL `data_protection_keys` и общий для replicas.

Stage 8 integration test поднимает две независимые `WebApplicationFactory` instance с одной PostgreSQL БД и проверяет:

```text
cookie from replica A -> /me on replica B
antiforgery from replica A -> unsafe Project create on replica B
```

Это проверяет stateless/multi-replica contract без sticky sessions.

## 19. Middleware order

Текущий Stage 8 pipeline:

```text
ExceptionHandler
Authentication
Authorization
MVC authorization filters / antiforgery
Controllers
```

RateLimiter, forwarded headers, request timeouts и resource limits появятся Stage 9 и займут предусмотренные архитектурой позиции.

## 20. API authentication failures

Cookie scheme настроен возвращать:

```text
RFC7807 401 instead of redirect-to-login
RFC7807 403 instead of redirect-to-access-denied
```

Это важно для API/Blazor client: HTML redirects не являются API error protocol.

ASP.NET Core 10 сам имеет API-oriented cookie behavior, но TaskFlow фиксирует это явно в cookie events, чтобы контракт не зависел от endpoint classification details.

## 21. Security tests

HTTP security suite использует:

```text
WebApplicationFactory<Program>
+ HTTPS base address
+ real TaskFlow Program.cs
+ PostgreSQL Testcontainer
+ real Identity EF store
+ real Data Protection EF key store
```

Проверяются:

```text
fallback auth -> 401
register/login/logout/me
cookie attributes
CSRF RFC7807 response
safe GET vs unsafe verbs
request-token refresh after auth change
generic invalid credentials
failed-login AccessFailedCount
foreign Project/Task/Tag/TaskTag -> 404
multi-replica cookie
multi-replica antiforgery
```

## 22. NuGet choice

Для full-pipeline integration tests добавлен:

```text
Microsoft.AspNetCore.Mvc.Testing 10.0.12
```

Он находится только в IntegrationTests и не является production dependency API.

Версия централизована в `Directory.Packages.props`, а lock graph обновлён, поэтому `restore --locked-mode` остаётся обязательным build contract.

## 23. Что намеренно не делается на Stage 8

Следующие задачи принадлежат Stage 9+:

```text
rate limiting
typed SecurityOptions/CorsOptions/ProxyOptions
request body limits
request timeouts
trusted forwarded headers
health live/ready
edge security headers
structured security logging
DbMigrator runtime process
Blazor auth state provider
```

Это не пробелы Stage 8, а границы последующих этапов архитектуры.

## 24. Definition of Done Stage 8

Stage 8 считается реализованным, когда:

```text
Identity uses TaskFlowDbContext
cookie auth configured securely
Data Protection key ring stored in PostgreSQL
fallback policy denies anonymous by default
auth endpoints have correct anonymous/authenticated split
login failure is generic and lockout-on-failure enabled
- actual lockout is verified after the configured failed-attempt threshold
unsafe requests require antiforgery
CSRF failure uses RFC7807
owner/BOLA semantics remain 404
cookie and antiforgery work across replicas
locked NuGet graph includes HTTP test host
all Stage 0-8 architecture/source checks pass
```
