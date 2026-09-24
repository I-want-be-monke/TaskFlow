# TaskFlow — Stage 6

TaskFlow реализуется по этапам архитектурного контракта. **Stages 0–6 завершены в этом snapshot**: repository/build foundation, Domain, Application Core, все v1 use cases, PostgreSQL/EF Core schema и теперь полноценная реализация persistence ports, transactions, row locks, read queries и optimistic concurrency.

## Что готово

- pinned .NET **10.0.401** / C# 14 build contract;
- locked NuGet restore и сохранённое направление зависимостей;
- Domain/Application из Stages 1–4;
- PostgreSQL/EF Core schema + initial migration из Stage 5;
- реальные `ProjectRepository`, `TaskRepository`, `TagRepository`;
- реальные `ProjectQueries`, `TaskQueries`, `TagQueries`;
- owner-scoped write/read access;
- read path: `AsNoTracking()` → filtering/sorting → projection → pagination;
- `IQueryable` не выходит из Infrastructure;
- `EfTransactionManager` с explicit PostgreSQL transaction boundary;
- `SELECT ... FOR UPDATE` для Project/Task/Tag lock path;
- canonical lock order: `Project -> TaskItem -> Tag/TaskTag`;
- `VersionConcurrencyInterceptor` увеличивает `Version` на `original + 1` для изменяемых aggregate roots;
- `UnitOfWork` возвращает typed `Result` и переводит `DbUpdateConcurrencyException` в `Conflict`;
- unique `(owner_user_id, normalized_name)` race переводится в `tags.duplicate_name` conflict;
- query filters/sorting/pagination для Task search;
- PostgreSQL integration tests для owner scope, no-tracking, row locks, version conflicts, unique races и deadlock regression;
- ArchiveProject concurrency tests против Create/Update/Delete Task и Add/Remove Tag;
- Git history Stages 0–6 сохранена в `.git`.

## Проверка Stage 6

Нужны:

```text
.NET SDK 10.0.401
Docker Engine / Docker Desktop
```

Из корня репозитория:

```bash
./scripts/verify-stage6.sh
```

Скрипт выполняет:

```text
Stage 0–6 architecture/source checks
-> dotnet restore TaskFlow.sln --locked-mode
-> Release build
-> Docker availability check
-> unit tests
-> PostgreSQL/Testcontainers integration + concurrency tests
```

Эквивалентные основные команды:

```bash
python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
python3 scripts/verify_task_tag_features.py
python3 scripts/verify_infrastructure_stage5.py
python3 scripts/verify_infrastructure_stage6.py

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
docker info
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Ожидаемый результат на машине с pinned SDK и Docker: locked restore успешен, Release build без warnings/errors, unit tests зелёные, Testcontainers запускает PostgreSQL 18, migration применяется, persistence/concurrency tests проходят.

## Persistence boundary

Write path:

```text
Application Handler
-> owner-scoped repository
-> Domain method
-> UnitOfWork
-> EF Core / PostgreSQL
```

Read path:

```text
owner-scoped predicate
-> AsNoTracking
-> filters / whitelist sort
-> projection to read model
-> pagination
```

Project-dependent mutations выполняются в transaction boundary и берут locks в одном порядке:

```text
1. Project
2. TaskItem
3. Tag / TaskTag
```

`GetOwnedForUpdateAsync(...)` намеренно требует активную транзакцию. Вызов вне `ITransactionManager` считается ошибкой программирования.

## Optimistic concurrency

`Project`, `TaskItem`, `Tag` имеют `Version` как EF concurrency token. Перед `UPDATE` interceptor выставляет:

```text
current Version = original Version + 1
```

EF формирует write с проверкой исходной версии. Если запись уже изменилась, `DbUpdateConcurrencyException` переводится в typed Application `Conflict`, а не утекает наружу как provider exception.

Application pre-check версии остаётся для понятного normal-path ответа, а database concurrency token закрывает гонку между read и write.

## Что остаётся на Stage 7

Stage 6 заканчивает persistence layer. Следующий этап строит HTTP boundary:

- Composition Root в `TaskFlow.Api/Program.cs`;
- регистрация handlers и Infrastructure adapters;
- `ProblemDetails`;
- typed `Result -> HTTP` mapping;
- `/api/v1` contract;
- request/response DTO;
- Projects/Tasks/Tags/TaskTag controllers/endpoints;
- `201 Created` + `Location`;
- запрет возврата Domain entities из API.

Authentication/authorization/CSRF остаются Stage 8.

## Статус проверки этого архива

При создании snapshot выполнены все доступные source/architecture проверки, XML/JSON проверки, Git checks и archive integrity checks. В текущем artifact-контейнере отсутствуют `dotnet` и Docker, поэтому этот README **не утверждает**, что runtime `dotnet restore/build/test` или Testcontainers suite были запущены внутри него. `./scripts/verify-stage6.sh` — воспроизводимая полная проверка на обычной dev-машине.

Подробные решения: [`docs/STAGE_6_RATIONALE.md`](docs/STAGE_6_RATIONALE.md).
