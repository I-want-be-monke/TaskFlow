# Stage 16 — Definition of Done v1

Этот файл связывает финальные критерии архитектуры с физическими implementation/test artifacts репозитория.

| Definition of Done | Реализация / проверка |
|---|---|
| Solution dependency rules соблюдены | `verify_project_references.py`, `verify_final_stage16.py` |
| Domain не зависит от persistence/framework | Domain `.csproj` + architecture guards |
| PostgreSQL schema создаётся с нуля | `DbMigratorTests.EmptyDatabase_MigratesSuccessfullyWithMigratorRole`, Compose one-shot migrator |
| Projects/Tasks/Tags CRUD работает через API/UI | Application/API tests + `final_browser_e2e.py` |
| Ownership нельзя обойти UUID | owner-scoped repositories + `BolaMatrix_ForeignProjectTaskTagAndRelationReturnNotFound` + final E2E foreign Project `404` |
| Archived Project блокирует Task/TaskTag mutations | Project-first row lock + `ArchiveRace_SerializesProjectDependentMutations` + final E2E `tasks.project_archived` |
| Optimistic concurrency возвращает `409` | EF Version token + concurrency tests + browser `Reload latest` conflict flow |
| Cookie auth + CSRF работают | Identity cookie + antiforgery filter/tests + browser flow |
| Session работает между replicas | shared PostgreSQL Data Protection + `CookieAndAntiforgeryTokens_WorkAcrossApiReplicasSharingPostgresKeyRing` |
| API stateless / no sticky session | shared DB/key ring + API restart smoke/final E2E |
| API runtime не имеет DDL | `ApiRole_CannotPerformSchemaDdl` + final container DDL probe |
| Migrations отдельным DbMigrator | `TaskFlow.DbMigrator`, advisory lock tests, production migration-call guard |
| Errors RFC7807 | API ProblemDetails tests + typed client parser |
| Structured logs stdout/stderr без secrets | JSON formatter/tests + final real-secret container-log scan |
| live/ready semantics различаются | health tests + final container internal probes |
| Config fail-fast | Stage 9 typed Options/startup tests |
| Integration tests используют PostgreSQL | Testcontainers.PostgreSql; SQLite отсутствует |
| P0 security/concurrency/architecture tests | Stage 15 CI + Stage 16 final verifier |
| Immutable/non-root images | Stage 14 Dockerfiles/Compose + container verifier |
| Полный browser CRUD flow | Playwright `scripts/final_browser_e2e.py` |

## Финальный browser flow

```text
Register
-> Logout
-> Login
-> Create Project
-> Edit Project
-> Create Task
-> Edit Task
-> Create Tag
-> Attach Tag to Task
-> Filter/List Tasks
-> page refresh restores session
-> foreign UUID returns 404
-> unsafe request without CSRF returns 400
-> concurrent Project update -> UI 409 + Reload latest
-> Archive Project
-> Task update blocked with tasks.project_archived
-> Restore Project
-> restart API
-> same cookie/session + data + pre-restart antiforgery remain valid
-> health/least-privilege/log-redaction checks
-> Delete Task
-> Delete Tag
-> Delete Project
-> Logout
```

## Что запускает полный DoD

```bash
./scripts/verify-stage16.sh
```

Требования к машине: .NET SDK `10.0.401`, Docker Compose v2, Python 3, Playwright `1.63.0` + Chromium.

CI устанавливает Playwright автоматически. Локально:

```bash
python3 -m pip install playwright==1.63.0
python3 -m playwright install --with-deps chromium
```
