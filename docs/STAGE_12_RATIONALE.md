# Stage 12 — Blazor WebAssembly Client Foundation: rationale

## 1. Цель этапа

Stage 12 не реализует полноценные страницы TaskFlow. Его задача — зафиксировать **один client-side security/HTTP boundary**, чтобы Stage 13 строил UI поверх стабильных сервисов, а Razor components не дублировали session, CSRF, RFC7807 и request logic.

Архитектурный контракт Stage 12 требует:

```text
ApiProblemReader
AntiforgeryTokenProvider
AntiforgeryHandler
ApiAuthenticationStateProvider
ProjectsApiClient
TasksApiClient
TagsApiClient
```

Именно эти границы реализованы физически в `TaskFlow.Client`.

## 2. Почему теперь настоящий standalone Blazor WASM

До Stage 12 Client был только compile-time Razor boundary, чтобы ранние backend stages не зависели от ещё нестабильного frontend package graph.

Теперь API/auth contract стабилен, поэтому проект переведён на:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
```

и получил полноценные WASM assets/bootstrap:

```text
Program.cs
App.razor
wwwroot/index.html
wwwroot/css/app.css
```

Client по-прежнему не имеет `ProjectReference` на Domain/Application/Infrastructure/Api.

## 3. Почему frontend не читает auth cookie

Authentication cookie настроена сервером как HttpOnly. WASM code не должен получать доступ к её содержимому и не должен пытаться зеркалировать cookie в browser storage.

Client узнаёт состояние пользователя только через:

```text
GET /api/v1/auth/me
```

`ApiAuthenticationStateProvider` превращает server response в минимальный `ClaimsPrincipal` в памяти вкладки. Источником истины остаётся серверная session, а не client cache.

При refresh страницы bootstrap снова вызывает `/auth/me`, поэтому session восстанавливается без bearer token.

## 4. Два HTTP пути: raw и protected

Нужны два логических transport path.

### RawApiHttpClient

Используется только security foundation для safe bootstrap calls:

```text
GET /api/v1/auth/antiforgery
GET /api/v1/auth/me
```

Он не проходит через `AntiforgeryHandler`, что предотвращает recursion при получении самого CSRF token.

### ApiHttpClient

Используется feature/auth clients. Его transport содержит `AntiforgeryHandler`.

Razor components не получают raw transport и не строят requests вручную.

## 5. Antiforgery token только в памяти

`AntiforgeryTokenProvider` хранит:

```csharp
private string? _token;
```

и ничего не записывает в:

```text
localStorage
sessionStorage
IndexedDB
cookie доступную JavaScript
```

Это соответствует browser-session модели: server auth cookie недоступна JS, а request token существует только в текущем WASM process/tab.

`SemaphoreSlim` сериализует refresh, чтобы несколько одновременных mutation requests не инициировали конкурентные bootstrap token requests.

## 6. Почему handler добавляет token только к unsafe methods

CSRF relevant методы:

```text
POST
PUT
PATCH
DELETE
```

Для них `AntiforgeryHandler` получает token и добавляет:

```text
X-XSRF-TOKEN
```

Safe `GET/HEAD/OPTIONS` не требуют token.

Handler вызывает inner `SendAsync` ровно один раз. Автоматического retry mutation нет.

## 7. Antiforgery lifecycle после identity change

Antiforgery request token связан с authentication state. Поэтому старый anonymous token нельзя считать корректным после login, а authenticated token — после logout.

Реализован lifecycle:

```text
bootstrap
-> refresh antiforgery
-> refresh /auth/me

register/login success
-> receive server response
-> clear previous token
-> fetch authenticated token
-> rebuild client auth state through /auth/me

logout success
-> mark anonymous
-> clear authenticated token
-> fetch anonymous token
```

Если любой protected API request возвращает `401`, handler:

```text
marks auth state anonymous
clears antiforgery token
```

Следующий unsafe request не переиспользует token от истёкшей session.

## 8. Почему добавлен AuthApiClient

Архитектурный minimum перечисляет project/task/tag clients, но login/register/logout — такие же HTTP features и не должны реализовываться будущими Razor pages вручную.

Поэтому добавлен `AuthApiClient`, который централизует:

```text
register
login
logout
antiforgery refresh after identity change
auth state refresh
```

Это не меняет backend auth contract и не создаёт дополнительную token scheme.

## 9. Feature API clients

Stage 12 содержит:

```text
ProjectsApiClient
TasksApiClient
TagsApiClient
```

Они используют `ApiHttpClient`, а не `HttpClient` напрямую.

Все routes относительные:

```text
/api/v1/...
```

Frontend не содержит environment-specific API hostname. Это позволяет будущему reverse proxy обслуживать WASM и API same-origin.

Task↔Tag operations принадлежат `TasksApiClient`, потому что API route contract уже выглядит как:

```text
PUT    /api/v1/tasks/{taskId}/tags/{tagId}
DELETE /api/v1/tasks/{taskId}/tags/{tagId}
```

## 10. Client contracts не импортируются из server assembly

Client имеет собственные небольшие request/response records.

Это намеренно дублирует wire schema вместо ссылки на `TaskFlow.Api`, потому что иначе Browser project стал бы зависеть от server assembly и нарушил Ports & Adapters boundary.

Server-controlled поля ownership/security в create/update request моделях отсутствуют.

## 11. RFC7807 и conflict UX

`ApiProblemReader` извлекает стабильные transport fields:

```text
HTTP status
code
title
detail
traceId
```

`ApiHttpClient` преобразует non-success response в `ApiProblemException`.

В результате future UI не анализирует строку `Detail`. Например optimistic concurrency может проверяться через:

```text
Status = 409
Code = projects.version_conflict / ...
```

и `IsVersionConflict` даёт готовую границу для reload-required UX Stage 13.

## 12. Почему нет automatic retry

Safe GET позже может получить отдельную retry policy, если появится реальная необходимость. Но mutation requests сейчас не retry автоматически.

Причина: POST/PUT/DELETE могут иметь бизнес-эффект, а transport-level uncertainty не означает, что сервер точно не применил операцию.

Скрытый retry создавал бы риск повторного business action и усложнял concurrency semantics.

## 13. Frontend tests

Реальные xUnit tests находятся в существующем `TaskFlow.IntegrationTests`, чтобы не добавлять четвёртый test project и не менять заранее зафиксированную solution structure.

IntegrationTests получил ProjectReference на Client; production Client по-прежнему не ссылается на server projects.

Тесты выполняются без browser renderer/bUnit, потому что Stage 12 проверяет не markup, а HTTP/security boundary:

```text
ApiProblemReaderTests
ApiAuthenticationStateProviderTests
AntiforgeryHandlerTests
ApiConflictSurfaceTests
```

Проверяется:

- RFC7807 parsing;
- `/auth/me` restoration;
- unsafe antiforgery header;
- отсутствие header на safe method;
- `401 -> anonymous`;
- `409 -> typed UI-visible conflict`.

UI rendering/E2E появятся на следующих этапах.

## 14. Static architecture guard

`scripts/verify_client_stage12.py` дополнительно запрещает:

```text
Client -> server ProjectReference
Client -> server namespaces
HttpClient usage в Razor components
localStorage/sessionStorage auth storage
bearer/refresh token browser contract
Polly/automatic retry
absolute backend URLs
non-/api/v1 hard-coded routes
```

Также он проверяет наличие browser-wasm lock target и pinned package graph.

## 15. NuGet / SDK

Stage 12 использует stable .NET 10 servicing packages `10.0.12` для:

```text
Microsoft.AspNetCore.Components.Authorization
Microsoft.AspNetCore.Components.WebAssembly
Microsoft.AspNetCore.Components.WebAssembly.DevServer
```

Client lock graph также фиксирует WebAssembly/ILLink packs `10.0.12`, соответствующие pinned SDK `10.0.401` servicing release.

## 16. Что намеренно НЕ сделано на Stage 12

Не реализуются полноценные:

```text
login/register pages
project/task/tag pages
forms/navigation UX
loading skeletons
conflict modal/toast
browser E2E flow
frontend Docker/reverse proxy image
```

Это ответственность Stages 13–14+. Stage 12 заканчивается там, где Client имеет стабильный API/security boundary и UI больше не должен писать HTTP/security logic самостоятельно.
