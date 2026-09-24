# TaskFlow — Stage 12

TaskFlow реализуется по архитектурным этапам. **Stages 0–12 завершены в этом snapshot**: repository/build foundation, Domain/Application, PostgreSQL persistence/concurrency, REST API, cookie/CSRF security, production hardening, structured observability, one-shot DbMigrator и теперь standalone Blazor WebAssembly client foundation.

## Что добавлено на Stage 12

- `TaskFlow.Client` переведён на `Microsoft.NET.Sdk.BlazorWebAssembly`;
- standalone WASM bootstrap (`Program.cs`, `App.razor`, `wwwroot/index.html`);
- `ApiProblemReader` + typed `ApiProblemException`;
- `ApiAuthenticationStateProvider` с восстановлением session только через `/api/v1/auth/me`;
- `AntiforgeryTokenProvider` с token только в памяти вкладки;
- `AntiforgeryHandler`, автоматически добавляющий `X-XSRF-TOKEN` к unsafe requests;
- `AuthApiClient` с корректным antiforgery lifecycle после register/login/logout;
- `ProjectsApiClient`, `TasksApiClient`, `TagsApiClient`;
- отдельные client request/response models без ссылок на серверные assemblies;
- typed `409` conflict surface для будущего reload-required UX;
- frontend boundary tests;
- `scripts/verify-stage12.sh`.

## Client architecture

```text
Razor component
  -> feature API client
     -> ApiHttpClient
        -> AntiforgeryHandler
           -> same-origin /api/v1/...
```

Razor components не создают `HttpRequestMessage`, не управляют CSRF и не знают transport details.

## Browser session boundary

Authentication остаётся server-owned cookie session:

```text
browser cookie (HttpOnly)
  -> не читается WASM code

GET /api/v1/auth/me
  -> ApiAuthenticationStateProvider
  -> ClaimsPrincipal только в памяти client process
```

Client не хранит bearer/refresh tokens в `localStorage`/`sessionStorage`.

## Antiforgery lifecycle

```text
initial bootstrap
  -> GET /api/v1/auth/antiforgery

POST/PUT/PATCH/DELETE
  -> AntiforgeryHandler
  -> X-XSRF-TOKEN
  -> request отправляется ровно один раз

successful register/login
  -> clear previous request token
  -> fetch token bound to authenticated identity
  -> refresh /auth/me

successful logout
  -> mark anonymous
  -> clear old token
  -> fetch anonymous token

401 from API
  -> mark anonymous
  -> clear stale antiforgery token
```

Safe `GET/HEAD/OPTIONS` запросы не требуют antiforgery header.

## Typed API clients

```text
AuthApiClient
ProjectsApiClient
TasksApiClient
TagsApiClient
```

Все URL относительные и начинаются с `/api/v1/...`; environment-specific API hostname во frontend не зашивается.

Mutating API clients не реализуют automatic retry. Это особенно важно для POST/PUT/DELETE: повтор пользовательской mutation должен быть явным UI-действием, а не скрытым transport retry.

## Error contract

`ApiProblemReader` читает RFC7807 response и сохраняет:

```text
status
code
title
detail
traceId
```

`ApiProblemException` передаёт typed problem выше в UI. Для optimistic-concurrency `409` доступен `IsVersionConflict`, поэтому Stage 13 сможет показать reload-required UX без разбора текста ошибки.

## Frontend tests

Тесты Stage 12 проверяют:

```text
ProblemDetails parsing
auth state restored through /auth/me
unsafe request receives X-XSRF-TOKEN
safe request does not receive antiforgery header
401 changes auth state to anonymous
409 version conflict is surfaced as typed API problem
```

Дополнительный static verifier проверяет:

```text
Client has no server ProjectReference
Client uses standalone Blazor WebAssembly SDK
Razor components do not build HTTP requests
no localStorage/sessionStorage auth token storage
no bearer/refresh token contract
all feature URLs are relative /api/v1/...
unsafe requests are not auto-retried
WASM/NuGet lock graph is pinned
```

## Запуск клиента

Для локального development frontend и API должны быть доступны через same-origin topology/reverse proxy, предусмотренную архитектурой. Сам WASM проект запускается:

```bash
dotnet run --project src/TaskFlow.Client/TaskFlow.Client.csproj
```

Полный production proxy/container topology добавляется на Stages 14+; Stage 12 намеренно не внедряет environment-specific backend hostname в Client.

## Полная проверка Stage 12

```bash
./scripts/verify-stage12.sh
```

Скрипт запускает static architecture checks Stages 0–12, затем:

```bash
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Для runtime части нужны .NET SDK `10.0.401` и Docker.

## Документы

Подробное объяснение Stage 12:

```text
docs/STAGE_12_RATIONALE.md
```

## Следующий этап

Stage 13 — Blazor UI: страницы auth/projects/tasks/tags, forms, loading/error states, conflict UX и основной пользовательский CRUD flow поверх уже единой client API boundary.
