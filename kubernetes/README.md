# TaskFlow в minikube

Это минимальный Kubernetes-контур для ДЗ 2: PostgreSQL, отдельная одноразовая
миграция, API и frontend. Исходный код приложения остаётся в `src/`, а все
манифесты собраны в этой папке и применяются одной командой `kubectl apply -k`.

## Что нужно один раз

Нужны Docker, minikube и kubectl. Создать локальный кластер можно так:

```text
minikube start --driver=docker
```

## Сборка образов

Основной вариант собирает образы в Docker и загружает их в minikube.

| ОС | Команда из корня репозитория |
|---|---|
| Windows PowerShell | `powershell -ExecutionPolicy Bypass -File .\kubernetes\scripts\Build-And-Load.ps1` |
| macOS / Linux | `sh ./kubernetes/scripts/build-and-load.sh` |

Скрипт принимает необязательные аргументы: профиль minikube и тег образа. По
умолчанию используются `minikube` и `dz2`. После завершения проверка выглядит
так: `minikube image ls | grep taskflow` (в PowerShell вместо `grep` используйте
`Select-String taskflow`).

Есть второй вариант — собрать напрямую в Docker-демоне minikube. Он не требует
`minikube image load`:

| ОС | Команда |
|---|---|
| Windows PowerShell | `powershell -ExecutionPolicy Bypass -File .\kubernetes\scripts\Build-In-Minikube.ps1` |
| macOS / Linux | `sh ./kubernetes/scripts/build-in-minikube.sh` |

Выберите один из вариантов сборки, не оба сразу.

## Развёртывание

| ОС | Команда |
|---|---|
| Windows PowerShell | `powershell -ExecutionPolicy Bypass -File .\kubernetes\scripts\Deploy.ps1` |
| macOS / Linux | `sh ./kubernetes/scripts/deploy.sh` |

Скрипт заново создаёт idempotent Job миграций, ждёт PostgreSQL, миграцию и оба
Deployment. Затем он выводит объекты кластера. Приложение открывается по URL из
следующей команды:

```text
minikube service taskflow-frontend -n taskflow --url
```

Frontend использует локальный self-signed TLS-сертификат. Если при Docker driver
minikube напечатает `http://127.0.0.1:...`, откройте тот же адрес с `https://` и
подтвердите предупреждение браузера.

## Что проверить на защите

```text
kubectl get all,pvc -n taskflow
kubectl scale deployment taskflow-api -n taskflow --replicas=3
kubectl get pods -n taskflow
```

Должны быть три Pod API, один Pod frontend, один Pod StatefulSet PostgreSQL,
завершившийся `taskflow-migrator` и PVC вида `data-taskflow-postgres-0`.

## Состав и обоснование

| Компонент | Kubernetes-объект | Зачем |
|---|---|---|
| PostgreSQL | `StatefulSet` + headless `Service` + `volumeClaimTemplates` | Стабильное имя Pod и отдельный постоянный PVC для реплики БД. БД не масштабируется как stateless Deployment. |
| Миграции | `Job` | Отдельно и идемпотентно применяет EF Core migrations до проверки API. |
| API | `Deployment` + `Service` | Stateless-реплики за Service; масштабируются командой `kubectl scale`. |
| Frontend | `Deployment` + `NodePort Service` | NGINX отдаёт Blazor WASM и проксирует `/api` во внутренний Service API. |

У каждого прикладного и init-контейнера прописаны `requests` и `limits`: это
делает планирование предсказуемым и защищает учебный minikube от одного
потребителя. API и migrator ограничены также read-only файловой системой и
снятыми Linux capabilities; PostgreSQL не получает такой флаг, потому что
корректно пишет свои данные в PVC.

## Проверка манифестов и очистка

Перед применением можно проверить локальную сборку Kustomize без изменения
кластера:

```text
kubectl kustomize ./kubernetes
kubectl apply --dry-run=client -k ./kubernetes
```

Удаление учебного окружения вместе с PVC и данными:

```text
kubectl delete namespace taskflow
```

Подробный сценарий записи находится в [Демонстрация.md](Демонстрация.md), краткий
отчёт — в [Отчёт.md](Отчёт.md).
