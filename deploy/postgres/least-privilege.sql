-- TaskFlow PostgreSQL least-privilege template.
--
-- Run as a database owner/provisioning role after creating LOGIN roles
-- `taskflow_app` and `taskflow_migrator`. Passwords/credentials are supplied by
-- deployment secret management and are intentionally NOT stored in this file.
--
-- The API and DbMigrator both read ConnectionStrings__Postgres, but receive
-- different values at runtime:
--   API        -> Username=taskflow_app
--   DbMigrator -> Username=taskflow_migrator

REVOKE CREATE ON SCHEMA public FROM PUBLIC;

GRANT USAGE ON SCHEMA public TO taskflow_app;
GRANT USAGE, CREATE ON SCHEMA public TO taskflow_migrator;

-- Existing objects (safe to run after an initial migration as well).
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO taskflow_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO taskflow_app;

-- Future objects are created by taskflow_migrator; automatically grant the
-- runtime DML permissions required by taskflow_app.
ALTER DEFAULT PRIVILEGES FOR ROLE taskflow_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO taskflow_app;
ALTER DEFAULT PRIVILEGES FOR ROLE taskflow_migrator IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO taskflow_app;
