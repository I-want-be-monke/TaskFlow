#!/bin/sh
set -eu

: "${TASKFLOW_APP_DB_PASSWORD:?TASKFLOW_APP_DB_PASSWORD is required}"
: "${TASKFLOW_MIGRATOR_DB_PASSWORD:?TASKFLOW_MIGRATOR_DB_PASSWORD is required}"

psql \
    --set=ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --set=app_password="$TASKFLOW_APP_DB_PASSWORD" \
    --set=migrator_password="$TASKFLOW_MIGRATOR_DB_PASSWORD" <<'SQL'
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'taskflow_app') THEN
        CREATE ROLE taskflow_app LOGIN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'taskflow_migrator') THEN
        CREATE ROLE taskflow_migrator LOGIN;
    END IF;
END
$$;

ALTER ROLE taskflow_app WITH LOGIN PASSWORD :'app_password';
ALTER ROLE taskflow_migrator WITH LOGIN PASSWORD :'migrator_password';
SQL
