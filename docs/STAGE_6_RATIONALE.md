# Stage 6 — repositories, queries, transactions and concurrency

## 1. Цель этапа

Stage 6 соединяет уже готовый Application Core с реальным PostgreSQL persistence без протекания EF Core в Domain/Application.

Архитектурный контракт этого этапа требует:

```text
ProjectRepository
TaskRepository
TagRepository
ProjectQueries
TaskQueries
TagQueries
UnitOfWork
TransactionManager
Version increment policy
owner-scoped access
AsNoTracking read path
Project row locks
optimistic concurrency
PostgreSQL concurrency tests
```

После Stage 6 Application handlers можно выполнять против PostgreSQL без HTTP layer.

## 2. Почему write и read paths разделены

Write repositories работают с Domain entities, потому что команды должны вызывать domain methods и сохранять инварианты.

Read queries возвращают только application read models:

```text
ProjectReadModel
TaskReadModel
TagReadModel
PagedResult<T>
```

Read path не материализует aggregate graph и не отдаёт `IQueryable` в Application.

Базовый pipeline:

```text
owner scope
-> AsNoTracking
-> filters / ordering
-> Select(read model)
-> Skip/Take
```

Это уменьшает tracking overhead и удерживает EF/Npgsql полностью внутри Infrastructure.

## 3. Owner-scoped repositories

Публичного пользовательского `GetById(id)` нет.

Реализации требуют владельца:

```text
ProjectRepository.GetOwnedByIdAsync(ownerUserId, projectId)
TaskRepository.GetOwnedByIdAsync(ownerUserId, taskId)
TagRepository.GetOwnedByIdAsync(ownerUserId, tagId)
```

Task ownership определяется через join к Project:

```text
TaskItem.ProjectId
-> Project.Id
-> Project.OwnerUserId
```

UUID остаётся идентификатором, но не механизмом авторизации.

`TaskTag` relation также читается только после owner scope для Task Project и Tag.

## 4. Почему FOR UPDATE реализован raw SQL

Критичный invariant:

```text
Archived Project запрещает Task/TaskTag mutations
```

Обычного optimistic concurrency на дочерней Task недостаточно. ArchiveProject и CreateTask могут менять разные строки и одновременно пройти предварительные проверки.

Поэтому команды, зависящие от `Project.Status`, сериализуются PostgreSQL row lock на Project.

Реализации `GetOwnedForUpdateAsync` используют parameterized `FromSqlInterpolated`:

```sql
SELECT *
FROM projects
WHERE id = @projectId
  AND owner_user_id = @ownerUserId
FOR UPDATE;
```

Task lock использует owner-scoped join:

```sql
SELECT task_item.*
FROM task_items AS task_item
JOIN projects AS project ON project.id = task_item.project_id
WHERE task_item.id = @taskId
  AND project.owner_user_id = @ownerUserId
FOR UPDATE OF task_item;
```

Никакой SQL string concatenation не используется.

## 5. Почему FOR UPDATE запрещён без transaction

PostgreSQL row lock имеет смысл только пока транзакция открыта. Если выполнить locking SELECT вне явного transaction boundary, lock будет освобождён сразу после statement и межагрегатный invariant фактически не будет защищён.

Поэтому repositories fail-fast проверяют:

```text
Database.CurrentTransaction != null
```

и бросают `InvalidOperationException` при неправильном внутреннем использовании.

Это не пользовательская ошибка, а programming invariant Infrastructure layer.

## 6. TransactionManager

`EfTransactionManager` реализует Application port `ITransactionManager`.

Базовая семантика:

```text
BEGIN READ COMMITTED
-> execute one Application operation
-> COMMIT
```

При exception выполняется rollback.

Если transaction уже существует, manager не создаёт вложенную физическую транзакцию и выполняет callback в текущей boundary.

Network calls внутри transaction по-прежнему запрещены архитектурой; текущие handlers выполняют только DB/domain операции.

## 7. Canonical lock order

Чтобы разные команды не брали одни и те же rows в разном порядке, используется фиксированный порядок:

```text
1. Project
2. TaskItem
3. Tag / TaskTag
```

`CreateTask`, `UpdateTask`, `DeleteTask` сначала lock Project.

`AddTagToTask` и `RemoveTagFromTask` используют:

```text
Project lock
-> Task lock
-> Tag lock
-> TaskTag read/write
```

Отдельный integration test запускает две competing transactions с тем же lock order и проверяет bounded completion без deadlock.

## 8. Version increment policy

Stage 5 уже настроил `Version` как EF concurrency token, но не определял increment policy.

Stage 6 добавляет `VersionConcurrencyInterceptor`.

Перед write для каждого modified aggregate root:

```text
Project
TaskItem
Tag
```

interceptor выставляет:

```text
Current Version = Original Version + 1
```

Важно использовать именно `OriginalValue + 1`, а не `CurrentValue + 1`: если SaveChanges завершился concurrency failure, повторный вызов interceptor не должен бесконтрольно увеличивать in-memory Version.

## 9. Два уровня optimistic concurrency

Application handlers уже делают pre-check:

```text
command.Version == entity.Version
```

Он полезен для нормального случая и даёт понятный domain/application conflict до write.

Но pre-check не закрывает race:

```text
request A reads Version 1
request B reads Version 1
A writes
B writes
```

Database concurrency token закрывает именно окно между read и UPDATE.

EF выполняет update/delete с original Version в predicate. Если affected rows = 0, возникает `DbUpdateConcurrencyException`.

## 10. Почему IUnitOfWork теперь возвращает Result

До Stage 6 `IUnitOfWork.SaveChangesAsync` не имел реальной provider implementation, поэтому возвращал только `Task`.

После подключения PostgreSQL persistence write может завершиться ожидаемыми конкурентными конфликтами:

```text
DbUpdateConcurrencyException
unique normalized Tag violation
```

Архитектура требует expected errors как typed Result, а не exception-driven API flow. Поэтому контракт стал:

```csharp
Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
```

Каждый write handler обязан проверить `saveResult` и распространить typed error.

Unexpected programming exceptions по-прежнему не маскируются.

## 11. Mapping concurrency conflict

`UnitOfWork` ловит:

```text
DbUpdateConcurrencyException
```

и возвращает:

```text
ErrorType.Conflict
code = persistence.concurrency_conflict
```

HTTP mapping появится только на Stage 7, где этот typed error станет `409 Conflict`.

Infrastructure не знает про controllers или `ProblemDetails`.

## 12. Tag uniqueness race

Application делает friendly pre-check:

```text
ExistsOwnedByNormalizedNameAsync
```

Но два concurrent requests могут одновременно увидеть `false`.

Финальный authority — PostgreSQL unique constraint:

```text
ux_tags_owner_user_id_normalized_name
```

`UnitOfWork` распознаёт SQLSTATE unique violation именно этого constraint и переводит его в:

```text
ErrorType.Conflict
tags.duplicate_name
```

Это сохраняет чистый Application contract даже при реальной гонке.

## 13. Почему provider exception распознаётся по constraint name

SQLSTATE `23505` означает unique violation в целом. В schema могут появиться другие unique constraints.

Маппить любой `23505` в `tags.duplicate_name` было бы неверно.

Поэтому проверяются одновременно:

```text
SQLSTATE == UniqueViolation
ConstraintName == ux_tags_owner_user_id_normalized_name
```

Остальные PostgreSQL write failures не маскируются под Tag conflict.

## 14. ProjectQueries

Project list:

```text
OwnerUserId predicate
-> AsNoTracking
-> CreatedAt DESC
-> Id tie-breaker
-> pagination
-> ProjectReadModel projection
```

`TotalCount` вычисляется по owner-scoped query до pagination.

## 15. TagQueries

Tag list:

```text
OwnerUserId predicate
-> AsNoTracking
-> Name ASC
-> Id tie-breaker
-> pagination
-> TagReadModel
```

Tie-breaker делает pagination детерминированной при одинаковых sort values.

## 16. TaskQueries

Task search реализует Stage 2 `TaskSearchQuery`:

```text
ProjectId?
Status?
Priority?
TagId?
DueBefore?
DueAfter?
SearchText?
Page
PageSize
Sort
```

Owner scope накладывается до пользовательских filters.

Tag filter реализован через `TaskTags.Any`, поэтому list query не материализует relation graph.

Text filter выполняется PostgreSQL `ILIKE` по Title/Description.

## 17. Task sorting

Поддерживается whitelist из Application:

```text
createdAt:asc / desc
dueAt:asc / desc
priority:asc / desc
title:asc / desc
```

Каждый ordering получает `Id` tie-breaker для стабильной pagination.

Priority сортируется по business order:

```text
Low < Medium < High
```

а не по alphabetic string representation в PostgreSQL.

## 18. DueAt sorting

Nullable `DueAt` сортируется детерминированно: задачи без due date помещаются после задач с датой и при ascending, и при descending варианте.

Это избегает provider-default различий в null ordering как части публичного query behavior.

## 19. AsNoTracking verification

Integration test выполняет Project/Task/Tag query objects через fresh DbContext и затем проверяет:

```text
ChangeTracker.Entries() == empty
```

Это доказывает read path поведением, а не только наличием строки `AsNoTracking()` в коде.

## 20. Owner-scope integration tests

Write repository tests создают двух пользователей и проверяют, что foreign owner не может получить:

```text
Project
Task
Tag
```

Query tests делают ту же проверку для read path.

Тем самым owner scope подтверждается на реальном PostgreSQL/EF query translation.

## 21. Concurrent update test

Два DbContext загружают один Project с одной Version.

Обе операции меняют entity и синхронизируются explicit `AsyncBarrier` перед SaveChanges.

Ожидаемый результат:

```text
exactly one Result.Success
exactly one ErrorType.Conflict
```

Это проверяет настоящий lost-update protection.

## 22. Concurrent normalized Tag test

Два DbContext одновременно добавляют:

```text
Backend
"  backend  "
```

Domain нормализует оба имени к одному `NormalizedName`.

Explicit barrier заставляет оба write path дойти до race window до сохранения.

Ожидается один success и один typed `tags.duplicate_name` Conflict.

## 23. ArchiveProject race tests

Архитектурно наиболее опасная гонка — Project archive против дочерней mutation.

Integration suite проверяет все пять сценариев:

```text
ArchiveProject vs CreateTask
ArchiveProject vs UpdateTask
ArchiveProject vs DeleteTask
ArchiveProject vs AddTag
ArchiveProject vs RemoveTag
```

Архивирующая transaction удерживает Project row lock после write, но до commit. Конкурирующая mutation доходит до того же `GetOwnedForUpdateAsync` через explicit synchronization signal.

После commit архивирования mutation получает актуальное `Archived` состояние и возвращает `ForbiddenByState`, а не успевает изменить дочерние данные.

## 24. Почему concurrency tests не используют Task.Delay

Timing-based tests флапают в CI и не доказывают нужный interleaving.

Stage 6 tests используют:

```text
TaskCompletionSource
AsyncBarrier
```

для явного управления critical race window.

Это соответствует архитектурному требованию synchronization barriers, а не случайных задержек.

## 25. Что намеренно не сделано на Stage 6

Stage 6 не реализует HTTP и security composition.

Здесь нет:

```text
controllers
ProblemDetails
cookie auth
CSRF
fallback authorization policy
rate limits
health checks
API middleware
```

Эти concerns принадлежат Stages 7–10.

## 26. Что будет на Stage 7

Следующий этап должен добавить API foundation поверх уже стабильных Application + Persistence boundaries:

```text
Composition Root
DI registrations
ProblemDetails
exception boundary
Result -> HTTP mapping
/api/v1
DTOs
Projects/Tasks/Tags/TaskTag endpoints
201 Location
API contract tests
```

Stage 7 не должен обходить repositories и напрямую inject `TaskFlowDbContext` в controllers.

## 27. Verification

Полный verifier:

```bash
./scripts/verify-stage6.sh
```

Он последовательно запускает все source/architecture guards Stages 0–6, locked restore, Release build и весь test suite с PostgreSQL Testcontainers.

В artifact-generation environment нет `dotnet` и Docker, поэтому runtime test execution здесь не заявляется как выполненный. Source-level contracts, Git integrity и archive integrity проверяются при сборке snapshot.
