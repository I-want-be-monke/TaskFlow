# TaskFlow — Stage 13

TaskFlow реализуется по архитектурным этапам. **Stages 0–13 завершены в этом snapshot**: backend, PostgreSQL, security, observability, one-shot DbMigrator, standalone Blazor WASM client foundation и теперь полный обязательный browser CRUD flow.

## Что добавлено на Stage 13

- `/auth/login` и `/auth/register`;
- authenticated layout/navigation + logout;
- `/projects` со списком, pagination и созданием;
- `/projects/{id}` с project state, archive/restore/delete и task workspace;
- `/projects/{id}/edit`;
- `/projects/{id}/tasks/new`;
- `/tasks/{id}/edit`;
- `/tags` с create/edit/delete и pagination;
- task filters по status/priority/tag/due/search;
- task sort + pagination;
- task create/edit/delete;
- Task↔Tag attach/detach;
- DataAnnotations validation;
- loading/empty/error states;
- explicit `409 version_conflict -> Reload latest` UX;
- safe text rendering без `MarkupString`/`innerHTML`;
- component/source smoke guard `verify_client_ui_stage13.py`;
- executable UI state/form tests;
- `scripts/verify-stage13.sh`.

## Обязательные UI routes

```text
/auth/login
/auth/register
/projects
/projects/{id}
/projects/{id}/edit
/projects/{id}/tasks/new
/tasks/{id}/edit
/tags
```

Protected routes используют `[Authorize]`. Login/Register остаются anonymous.

## UI architecture

```text
Razor page/component
  -> AuthApiClient / ProjectsApiClient / TasksApiClient / TagsApiClient
     -> ApiHttpClient
        -> AntiforgeryHandler
           -> same-origin /api/v1/...
```

Razor files:

- не создают `HttpClient`/`HttpRequestMessage`;
- не содержат `/api/v1` URLs;
- не читают auth cookie;
- не хранят bearer/refresh token;
- не используют `localStorage/sessionStorage` для authentication;
- не используют `MarkupString`/`innerHTML` для user-controlled data.

## Authentication UI

Login/Register используют `AuthApiClient`. После успешной identity mutation Stage 12 boundary обновляет antiforgery token и `/auth/me` state.

Refresh страницы восстанавливает session через:

```text
ClientBootstrapper
-> GET /api/v1/auth/antiforgery
-> GET /api/v1/auth/me
-> AuthenticationStateProvider
```

Logout находится в `MainLayout` и также использует `AuthApiClient`.

## Projects

`/projects`:

- paged list;
- create form;
- loading/empty/error states.

`/projects/{id}`:

- project details;
- edit link;
- archive/restore;
- delete confirmation;
- task list + filters + sort + pagination;
- create/edit/delete Task;
- attach/detach Tag.

Archived Project отображается read-only для Task/TaskTag mutations, что совпадает с Domain/Application invariant.

## Tasks

Поддержаны:

```text
status: Todo / InProgress / Done
priority: Low / Medium / High
search text
tag filter
due after / due before
sort
pagination
```

Due date из browser local datetime преобразуется в UTC `DateTimeOffset` перед API request и обратно в local datetime при редактировании.

## Tags и Task↔Tag

`/tags` поддерживает create/edit/delete и pagination.

На текущем API v1 нет отдельного read-endpoint `GET task/{id}/tags`. Поэтому Project UI не притворяется, что знает relation state: пользователь выбирает Tag и явно выполняет `Attach` или `Detach`. Оба backend operation уже идемпотентны.

Для selector все Tags загружаются через `TagsApiClient.ListAllAsync()` страницами по 100, поэтому UI не обрезает список на первой странице.

## Optimistic concurrency UX

Любой typed `ApiProblemException` с version/concurrency conflict превращается в UI state:

```text
409
-> запись не перезаписывается
-> показывается сообщение о concurrent change
-> Reload latest
-> повторная загрузка актуального Version/data
```

Project edit, Task edit, project actions, Task delete и Tag edit/delete используют server-issued `Version`.

## Validation/error states

Client-side DataAnnotations повторяют documented bounds:

```text
Project.Name       1..120
Project.Description <= 2000
Task.Title         1..200
Task.Description   <= 4000
Tag.Name           1..64
Auth.UserName      1..64
```

Server RFC7807 errors остаются authoritative. Network/unexpected client failure показывает generic safe UI error без раскрытия internal details.

## Frontend tests

Executable xUnit tests проверяют:

```text
form validation
version-conflict UI state
due datetime UTC/local mapping
```

Stage 12 tests продолжают проверять:

```text
ProblemDetails parsing
/auth/me session restore
antiforgery header rules
401 -> anonymous
409 typed conflict
```

`verify_client_ui_stage13.py` дополнительно проверяет route inventory, CRUD action wiring, filters/pagination, logout, conflict UI и security boundary Razor layer.

## Запуск

Frontend:

```bash
dotnet run --project src/TaskFlow.Client/TaskFlow.Client.csproj
```

Для полного локального приложения нужен same-origin reverse-proxy/container topology, который добавляется на Stage 14.

## Полная проверка Stage 13

```bash
./scripts/verify-stage13.sh
```

Скрипт запускает static architecture checks Stages 0–13, затем:

```bash
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Для runtime части нужны .NET SDK `10.0.401` и Docker.

## Документы

Подробное объяснение решений:

```text
docs/STAGE_13_RATIONALE.md
```

## Следующий этап

Stage 14 — Docker и локальный production-like запуск: отдельные API/frontend/migrator images, PostgreSQL, reverse proxy, one-shot migrations и same-origin topology.
