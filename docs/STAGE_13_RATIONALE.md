# Stage 13 — Blazor UI: полный пользовательский CRUD flow

## 1. Цель

Stage 12 завершил transport/security foundation. Stage 13 использует его как единственную границу и реализует весь обязательный frontend v1 без повторного написания HTTP, cookie или CSRF логики в Razor.

Обязательный flow из архитектуры:

```text
Login/Register
-> Projects
-> Project details
-> project create/edit/archive/restore/delete
-> task create/edit/delete
-> filters/pagination/sort
-> Tags create/edit/delete
-> TaskTag attach/detach
-> conflict/error/validation UX
-> logout
```

## 2. Route inventory

Физически реализованы:

```text
/auth/login
/auth/register
/projects
/projects/{ProjectId}
/projects/{ProjectId}/edit
/projects/{ProjectId}/tasks/new
/tasks/{TaskId}/edit
/tags
```

Business pages помечены `[Authorize]`. Auth pages не требуют authorization metadata.

`App.razor` использует `AuthorizeRouteView`; anonymous access к protected route переводится через `RedirectToLogin` на `/auth/login`.

## 3. Почему Razor не знает HTTP

Stage 13 сохраняет слой:

```text
Razor
-> feature API client
-> shared ApiHttpClient
-> antiforgery handler
-> backend
```

Source guard запрещает в `.razor`:

```text
HttpClient
HttpRequestMessage
ApiHttpClient / RawApiHttpClient
/api/v1 route literals
MarkupString
innerHTML
localStorage/sessionStorage auth state
Bearer token
```

Поэтому изменение transport/security contract остаётся централизованным.

## 4. Login/Register

Обе страницы используют DataAnnotations model и `AuthApiClient`.

UI не читает cookie и не создаёт local token. После успешного login/register сам `AuthApiClient` выполняет Stage 12 lifecycle:

```text
clear old antiforgery request token
-> get token for new identity
-> refresh /auth/me
```

После успеха UI только переходит на `/projects`.

Password очищается из client model после submit attempt.

## 5. Layout и logout

`MainLayout` использует `AuthorizeView`:

Authenticated:

```text
Projects
Tags
current user name
Logout
```

Anonymous:

```text
Login
Register
```

Logout выполняется через `AuthApiClient`, после чего client state становится anonymous и navigation возвращает пользователя на login.

## 6. Projects page

`Projects.razor` объединяет две операции, которые естественно относятся к одному экрану:

```text
paged list
create project
```

Create form не получает OwnerUserId: ownership по-прежнему server-controlled через `ICurrentActor`.

Project description выводится обычной Razor interpolation, поэтому user HTML не исполняется.

## 7. Project details как task workspace

`ProjectDetails.razor` является основным workspace конкретного Project.

Project actions:

```text
edit
archive
restore
delete with explicit confirmation
```

Task actions:

```text
create
edit
delete with confirmation
attach tag
detach tag
```

При `Archived` UI заранее отключает Task/TaskTag mutations. Это только UX; authoritative invariant всё равно остаётся Domain/Application/transaction lock protocol.

## 8. Task filters, sort и pagination

UI покрывает Application `TaskSearchQuery` contract:

```text
projectId
status
priority
tagId
dueBefore
dueAfter
q
page
pageSize
sort
```

Sort values берутся из `TaskSort` Stage 12 client contract, а не собираются произвольной строкой.

Apply filters сбрасывает страницу в `1`, чтобы не оказаться на несуществующей странице после сужения выборки.

## 9. Due date mapping

HTML `datetime-local` не содержит timezone offset.

В standalone WASM ввод рассматривается как local browser time и перед API boundary преобразуется:

```text
local DateTime
-> DateTimeOffset
-> UTC
```

При загрузке edit form `DateTimeOffset` из API переводится обратно в local time.

Это скрыто в `DateTimeMapping`, а не размазано по страницам.

## 10. Tags

`Tags.razor` реализует:

```text
paged list
create
edit
delete confirmation
```

Update/Delete используют `Version`, полученную от API.

При version conflict `Reload latest` отменяет stale edit/delete state и повторно загружает server version.

## 11. Task↔Tag и ограничение API v1

API v1 предоставляет mutation routes:

```text
PUT    /tasks/{taskId}/tags/{tagId}
DELETE /tasks/{taskId}/tags/{tagId}
```

но не предоставляет отдельный relation-read endpoint для конкретной task.

Stage 13 намеренно не меняет backend contract. UI показывает selector существующих Tags и две явные операции:

```text
Attach
Detach
```

Они безопасны для текущего API, потому что обе backend операции идемпотентны.

Чтобы selector не ограничивался первой страницей Tags, `TagsApiClient.ListAllAsync` проходит все страницы с bounded `pageSize=100`.

Если в будущей версии понадобится отображать chips только реально прикреплённых tags, API contract должен получить read model/endpoint явно, а не вычисляться UI догадками.

## 12. Optimistic concurrency UX

Architecture требует не перезаписывать изменения при `409 version_conflict`.

Общий `ApiErrorPanel` проверяет typed `UiProblemState.IsVersionConflict` и показывает:

```text
This record changed on the server
Reload latest
```

Reload callback всегда получает entity заново из API.

Это используется для:

```text
Project edit
Project archive/restore/delete
Task edit/delete
Tag edit/delete
```

Повтор mutation остаётся пользовательским решением. UI не делает hidden retry.

## 13. Validation

Client validation — быстрый UX feedback, не security boundary.

`FormModels.cs` повторяет основные documented bounds:

```text
UserName            1..64
Project.Name        1..120
Project.Description <= 2000
Task.Title          1..200
Task.Description    <= 4000
Tag.Name            1..64
```

Server validation остаётся authoritative и возвращает RFC7807.

## 14. Error states

Разделены два класса ошибок.

Expected API error:

```text
ApiProblemException
-> code/detail/traceId
-> ApiErrorPanel
```

Unexpected/network client failure:

```text
UiProblemState.Unexpected
-> generic safe message
```

UI не показывает stack trace или internal exception message.

## 15. Loading/empty/confirmation states

Страницы имеют explicit states:

```text
loading
empty list
server error
version conflict
confirmation before destructive delete
busy/disabled action
```

Это предотвращает double-click mutation и делает asynchronous API работу видимой пользователю.

## 16. Security rendering

User-controlled fields (`Project.Name/Description`, `Task.Title/Description`, `Tag.Name`, username) выводятся обычной Razor interpolation.

Не используются:

```text
MarkupString
innerHTML
raw HTML rendering
JS auth token storage
```

Это снижает XSS surface и сохраняет cookie-session model Stage 8/12.

## 17. Тесты Stage 13

Новые executable tests:

```text
UiFormValidationTests
UiProblemStateTests
DateTimeMappingTests
```

Они проверяют UI state/validation logic без дополнительного testing framework.

`verify_client_ui_stage13.py` является source/component smoke guard и проверяет:

- обязательные page files/routes;
- `[Authorize]` на business pages;
- login/register forms;
- project CRUD wiring;
- task CRUD/filter/pagination/sort wiring;
- tag CRUD;
- TaskTag attach/detach;
- conflict reload UI;
- logout;
- отсутствие raw HTTP/security anti-patterns в Razor.

Существующие Stage 12 executable transport/security tests продолжают работать.

## 18. Почему не добавлен UI framework

Stage 13 не добавляет MudBlazor/Fluent UI/bUnit или другую внешнюю UI dependency.

Причины:

- стандартных Blazor components достаточно для учебного v1;
- package/lock graph остаётся минимальным;
- UI показывает архитектуру, а не framework-specific abstraction;
- CSS и формы можно заменить позже без изменения API/security boundary.

## 19. Что намеренно остаётся Stage 14+

Не входит в Stage 13:

```text
frontend production Docker image
reverse proxy configuration
same-origin container topology
browser automation E2E
container health/resource config
```

Эти пункты требуют production-like hosting topology и относятся к Stage 14 и последующим этапам.
