# Stage 16 — Final E2E / Definition of Done

## 1. Цель

Stage 16 не меняет бизнес-архитектуру. Его задача — доказать, что накопленные Stages 0–15 работают как единый продукт и что ключевые свойства нельзя случайно удалить без красного CI.

## 2. Почему нужен настоящий browser E2E

Stage 14/15 уже имели HTTP/container smoke, но они не доказывали работу Razor forms, navigation, AuthenticationStateProvider, client validation и conflict UX вместе с backend. Поэтому финальный flow использует headless Chromium через Playwright и проходит UI теми же путями, которыми пользуется человек.

Playwright закреплён версией `1.63.0`; CI устанавливает Chromium явно. Browser context работает с локальным self-signed HTTPS через `ignore_https_errors=True`, не отключая production `Secure` cookie contract.

## 3. Browser flow

`scripts/final_browser_e2e.py` реализует обязательный сценарий архитектуры и расширяет его сквозными проверками:

1. Register через `Register.razor`.
2. Logout и Login через настоящие формы.
3. Project create/edit.
4. Task create/edit.
5. Tag create.
6. Attach Tag to Task.
7. Task search/status/priority/tag/sort filtering.
8. Refresh страницы и восстановление сессии через `/auth/me`.
9. BOLA probe другим пользователем.
10. CSRF rejection без request token.
11. Optimistic concurrency conflict и `Reload latest` UI.
12. Archive Project и реальный server-side запрет Task mutation.
13. Restore Project.
14. Restart API и повторная проверка cookie/data/antiforgery.
15. Cleanup Task/Tag/Project и Logout.

## 4. Как создаётся version conflict

E2E открывает Project edit form и запоминает старую Version косвенно через уже загруженную страницу. Затем тот же authenticated browser context выполняет отдельный API update, увеличивая Version. После этого browser form отправляет stale Version и обязан получить `409`.

Проверяется не только статус API: UI должен показать текст `This record changed on the server.` и кнопку `Reload latest`, после которой форма содержит свежие серверные данные. Silent overwrite запрещён.

## 5. BOLA и CSRF

Для BOLA создаётся второй независимый BrowserContext с собственной cookie-session и foreign Project. Первый пользователь получает `404`, а не данные и не различающий `403`.

Для CSRF выполняется authenticated unsafe POST без `X-XSRF-TOKEN`; ожидается RFC7807 `400`.

Эти runtime probes дополняют, но не заменяют exhaustive Stage 8 `AuthSecurityTests`.

## 6. Archived Project invariant

Project архивируется через UI. Затем E2E получает актуальную Task и пытается выполнить PUT напрямую с корректной Version и корректным antiforgery token. Сервер обязан вернуть `409` с `code=tasks.project_archived`.

Это важно: скрытая кнопка в UI сама по себе не является security/business guarantee.

## 7. Restart/stateless contract

После Restore сохраняются текущая auth cookie и antiforgery request token, затем выполняется `docker compose restart api`.

После restart проверяются:

- `/auth/me` всё ещё `200` с прежней cookie;
- Project всё ещё существует;
- browser page reload остаётся authenticated;
- pre-restart antiforgery token принимается новым API process.

Это подтверждает, что обязательное state хранится в PostgreSQL/shared Data Protection, а не памяти replica.

## 8. Health semantics

Frontend intentionally не публикует `/health/*`. Поэтому финальная проверка идёт внутри API container:

- `/health/live` anonymous -> `200`;
- `/health/ready` anonymous -> `401` из-за default-deny;
- `/health/ready` с реальной auth cookie -> `200` при доступном PostgreSQL.

## 9. Least privilege

Stage 16 не ограничивается source SQL policy. Runtime probe подключается к PostgreSQL как `taskflow_app` и пытается `CREATE TABLE`; команда обязана завершиться ошибкой.

Затем `taskflow_migrator` читает `__EFMigrationsHistory`, подтверждая, что clean-volume compose deployment действительно применил migration privileged role.

Exhaustive privilege/advisory-lock cases остаются в `DbMigratorTests`.

## 10. Log redaction реальными значениями

Formatter unit/integration tests используют synthetic secrets. Финальный container E2E дополнительно получает реальные значения текущего запуска:

- browser password;
- auth cookie value;
- antiforgery request token;
- app/migrator/bootstrap PostgreSQL passwords.

После flow `docker compose logs api migrator frontend` проверяется на точные значения. Их наличие делает E2E красным.

## 11. Multi-replica и concurrency

Некоторые свойства лучше проверяются deterministic integration tests, а не случайным routing в Docker Compose:

- cookie/token replica A -> replica B — `CookieAndAntiforgeryTokens_WorkAcrossApiReplicasSharingPostgresKeyRing`;
- same-Version race — `ConcurrentUpdatesWithSameVersion_ProduceOneSuccessAndOneConflict`;
- ArchiveProject race matrix — `ArchiveRace_SerializesProjectDependentMutations`;
- deadlock regression — `CanonicalLockOrder_CompletesWithoutDeadlock`.

Stage 16 verifier требует физическое наличие этих tests, а Stage 15/16 CI запускает весь IntegrationTests project.

## 12. Финальная architecture verification

`verify_final_stage16.py` повторно фиксирует самые важные границы:

- Domain без EF/ASP.NET/Npgsql;
- Application без Infrastructure/API;
- Infrastructure без API;
- Client без server project references;
- migrations только в `TaskFlow.DbMigrator/MigrationRunner.cs`;
- API startup без `Migrate/EnsureCreated`;
- browser Client без bearer/refresh token persistence, `MarkupString` и `innerHTML`.

Он также требует исторические tags `stage-0-complete` ... `stage-15-complete` и финальные report artifacts.

## 13. CI integration

Stage 16 не создаёт отдельный необязательный workflow. Существующий required pipeline усилен:

- quality job запускает `verify-static-stage16.sh`;
- container job устанавливает pinned Playwright;
- после Stage 14 smoke запускается `final_browser_e2e.py`;
- final E2E не имеет `continue-on-error`.

Таким образом Definition of Done является merge/release gate, а не ручным чеклистом.

## 14. Локальный полный запуск

```bash
python3 -m pip install playwright==1.63.0
python3 -m playwright install --with-deps chromium
./scripts/verify-stage16.sh
```

Скрипт выполняет static architecture checks, locked restore, Release build, все tests, затем clean-volume production-like stack и browser E2E.

## 15. Итог

После Stage 16 v1 codebase не получает новых architectural shortcuts. Финальный слой тестирования связывает Domain, PostgreSQL, API security, concurrency, observability, migrator, Blazor UI, containers и CI в один проверяемый Definition of Done.
