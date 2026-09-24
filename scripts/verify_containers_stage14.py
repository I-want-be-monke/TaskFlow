#!/usr/bin/env python3
from __future__ import annotations

import ipaddress
from pathlib import Path
import re
import sys
import yaml

ROOT = Path(__file__).resolve().parent.parent
errors: list[str] = []


def require(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


def read(path: str) -> str:
    file = ROOT / path
    require(file.exists(), f"Missing required Stage 14 file: {path}")
    return file.read_text(encoding="utf-8") if file.exists() else ""


def dockerfile_contract(path: str, expected_entrypoint: str) -> None:
    text = read(path)
    from_lines = [line.strip() for line in text.splitlines() if line.strip().upper().startswith("FROM ")]
    require(len(from_lines) >= 2, f"{path} must use a multi-stage build")
    if from_lines:
        require("sdk" not in from_lines[-1].lower(), f"{path} final image must not contain the full SDK")
    require("10.0.401" in text, f"{path} must pin the Stage 0 SDK 10.0.401")
    require("USER " in text, f"{path} must switch to an explicit non-root runtime user")
    require(expected_entrypoint in text, f"{path} must start {expected_entrypoint}")
    require("latest" not in text.lower(), f"{path} must not use mutable latest tags")


dockerfile_contract("src/TaskFlow.Api/Dockerfile", "TaskFlow.Api.dll")
dockerfile_contract("src/TaskFlow.DbMigrator/Dockerfile", "TaskFlow.DbMigrator.dll")
dockerfile_contract("src/TaskFlow.Client/Dockerfile", "taskflow-nginx-entrypoint.sh")

api_dockerfile = read("src/TaskFlow.Api/Dockerfile")
migrator_dockerfile = read("src/TaskFlow.DbMigrator/Dockerfile")
client_dockerfile = read("src/TaskFlow.Client/Dockerfile")
require("mcr.microsoft.com/dotnet/aspnet:10.0.12" in api_dockerfile, "API runtime must pin .NET 10.0.12")
require("mcr.microsoft.com/dotnet/aspnet:10.0.12" in migrator_dockerfile, "Migrator runtime must pin .NET 10.0.12")
require("nginxinc/nginx-unprivileged:1.31.6-alpine3.24" in client_dockerfile, "Frontend must use the pinned unprivileged NGINX image")

compose_text = read("docker-compose.yml")
try:
    compose = yaml.safe_load(compose_text) or {}
except yaml.YAMLError as exc:
    errors.append(f"docker-compose.yml is invalid YAML: {exc}")
    compose = {}

services = compose.get("services", {}) if isinstance(compose, dict) else {}
required_services = {"postgres", "migrator", "api", "frontend"}
require(required_services.issubset(set(services)), "Compose must define postgres, migrator, api and frontend services")

postgres = services.get("postgres", {})
migrator = services.get("migrator", {})
api = services.get("api", {})
frontend = services.get("frontend", {})

require(postgres.get("image") == "postgres:18.6-alpine3.24", "PostgreSQL image must be pinned to 18.6-alpine3.24")
require("ports" not in postgres, "PostgreSQL must not publish a host port")
require("ports" not in api, "API must stay internal; browser traffic must enter through the frontend reverse proxy")
require(bool(frontend.get("ports")), "Frontend must publish the only browser-facing HTTPS port")
require("container_name" not in api, "API must not set container_name so replicas remain possible")

subnet_match = re.search(r"TASKFLOW_BACKEND_SUBNET:-([^}]+)", compose_text)
proxy_matches = re.findall(r"TASKFLOW_PROXY_IP:-([^}]+)", compose_text)
require(subnet_match is not None, "Compose must expose an overridable isolated backend subnet")
require(len(proxy_matches) >= 2 and len(set(proxy_matches)) == 1, "API trusted proxy and frontend static IP must share the same default address")
if subnet_match and proxy_matches:
    network = ipaddress.ip_network(subnet_match.group(1), strict=False)
    require(ipaddress.ip_address(proxy_matches[0]) in network, "Default frontend proxy IP must belong to the default backend subnet")

postgres_dep = migrator.get("depends_on", {}).get("postgres", {})
api_dep = api.get("depends_on", {}).get("migrator", {})
frontend_dep = frontend.get("depends_on", {}).get("api", {})
require(postgres_dep.get("condition") == "service_healthy", "Migrator must wait for PostgreSQL readiness")
require(api_dep.get("condition") == "service_completed_successfully", "API must wait for successful one-shot migration")
require(frontend_dep.get("condition") == "service_healthy", "Frontend must wait for a healthy API")

for name, service in (("migrator", migrator), ("api", api), ("frontend", frontend)):
    require(service.get("read_only") is True, f"{name} root filesystem must be read-only")
    require("ALL" in (service.get("cap_drop") or []), f"{name} must drop Linux capabilities")
    require("no-new-privileges:true" in (service.get("security_opt") or []), f"{name} must enable no-new-privileges")
    require(bool(service.get("tmpfs")), f"{name} needs explicit ephemeral tmpfs for writable temp state")

api_env = api.get("environment", {})
migrator_env = migrator.get("environment", {})
require("Username=taskflow_app" in str(api_env.get("ConnectionStrings__Postgres", "")), "API must use taskflow_app runtime credentials")
require("Username=taskflow_migrator" in str(migrator_env.get("ConnectionStrings__Postgres", "")), "Migrator must use taskflow_migrator credentials")
require("Proxy__KnownProxies__0" in api_env, "API must trust only the explicit frontend proxy address")
require(str(api_env.get("ASPNETCORE_ENVIRONMENT")) == "Production", "Compose API must run the production pipeline")
require("${TASKFLOW_IMAGE_TAG" in str(api.get("image", "")), "API image tag must come from immutable release id")
require("${TASKFLOW_IMAGE_TAG" in str(migrator.get("image", "")), "Migrator image tag must come from immutable release id")
require("${TASKFLOW_IMAGE_TAG" in str(frontend.get("image", "")), "Frontend image tag must come from immutable release id")
require("latest" not in compose_text.lower(), "Compose must not use latest image tags")
require("/var/lib/postgresql" in compose_text, "PostgreSQL 18 data volume must target /var/lib/postgresql")

nginx = read("deploy/nginx/default.conf.template")
require("location /api/" in nginx and "api:8080" in nginx, "NGINX must reverse-proxy relative /api requests to the internal API")
require("try_files $uri $uri/ /index.html" in nginx, "NGINX must provide SPA fallback for Blazor routes")
require("listen 8443 ssl" in nginx, "Frontend must terminate local HTTPS")
require("wasm-unsafe-eval" in nginx, "Blazor WASM CSP must explicitly allow wasm-unsafe-eval")
require('"http_path":"$uri"' in nginx, "Frontend access log must use path without query string")
require('"service_version":"__TASKFLOW_SERVICE_VERSION__"' in nginx, "Frontend log service_version must be the immutable release id")
require("location ^~ /health/" in nginx and "return 404" in nginx, "API health endpoints must not be exposed through the public frontend")
for forbidden in ("$http_cookie", "$sent_http_set_cookie", "$request_body", "$args"):
    require(forbidden not in nginx, f"Frontend logging config must not include sensitive field {forbidden}")

entrypoint = read("deploy/nginx/taskflow-nginx-entrypoint.sh")
require("openssl req" in entrypoint and "/tmp/taskflow-tls" in entrypoint, "Local TLS key/certificate must be generated at runtime in ephemeral /tmp")

init_roles = read("deploy/postgres/docker-entrypoint-initdb.d/10-create-roles.sh")
least_privilege = read("deploy/postgres/least-privilege.sql")
require("TASKFLOW_APP_DB_PASSWORD" in init_roles and "TASKFLOW_MIGRATOR_DB_PASSWORD" in init_roles, "PostgreSQL bootstrap must source role passwords from runtime environment")
require("CREATE ROLE taskflow_app" in init_roles and "CREATE ROLE taskflow_migrator" in init_roles, "PostgreSQL bootstrap must create separate app/migrator roles")
require("REVOKE CREATE ON SCHEMA public FROM PUBLIC" in least_privilege, "Least-privilege SQL must revoke public schema CREATE")
require("GRANT USAGE, CREATE ON SCHEMA public TO taskflow_migrator" in least_privilege, "Migrator role must receive schema migration privileges")
require("GRANT SELECT, INSERT, UPDATE, DELETE" in least_privilege, "API role must receive DML privileges")

ignore = read(".dockerignore")
for required in (".git", "**/bin", "**/obj", ".env.*", ".local"):
    require(required in ignore, f".dockerignore must exclude {required}")

up_script = read("scripts/compose-up.sh")
require("git rev-parse HEAD" in up_script, "Compose launcher must tag local images with the exact Git commit SHA")
require("git status --porcelain" in up_script, "Compose launcher must reject dirty source when using commit-SHA image tags")
require("openssl rand" in up_script or "secrets.token_hex" in up_script, "Compose launcher must generate local passwords instead of committing secrets")
require("docker compose" in up_script and "--build" in up_script, "Compose launcher must build/start the production-like stack")

smoke = read("scripts/compose_smoke.py")
for phrase in (
    "/api/v1/auth/register",
    "/api/v1/projects",
    '"restart", "api"',
    "pre_restart_antiforgery",
    "/api/v1/auth/me",
    "/health/ready",
    "/api/v1/auth/logout",
):
    require(phrase in smoke, f"Stage 14 smoke test is missing required flow: {phrase}")

client_tree = "\n".join(
    path.read_text(encoding="utf-8", errors="ignore")
    for path in (ROOT / "src/TaskFlow.Client").rglob("*.cs")
)
require("new HttpClient { BaseAddress = baseAddress }" in client_tree, "Client must continue using same-origin HostEnvironment.BaseAddress")
require("http://api" not in client_tree and "https://api" not in client_tree, "Client must not bake an environment-specific API hostname")

api_program = read("src/TaskFlow.Api/Program.cs")
for forbidden in ("Database.Migrate(", "Database.MigrateAsync(", "EnsureCreated(", "EnsureCreatedAsync("):
    require(forbidden not in api_program, f"API startup must not run migrations: {forbidden}")
require("PersistKeysToDbContext<TaskFlowDbContext>()" in api_program, "API replicas must share Data Protection keys through PostgreSQL")

for path in (
    "docker-compose.yml",
    "src/TaskFlow.Api/Dockerfile",
    "src/TaskFlow.DbMigrator/Dockerfile",
    "src/TaskFlow.Client/Dockerfile",
    ".env.compose.example",
):
    text = read(path)
    require("CHANGE_ME" not in text, f"{path} must not carry a fake committed runtime password")
    require(not re.search(r"Password=(?!\$\{)[^;\s]+", text), f"{path} appears to contain a literal connection-string password")

if errors:
    for error in errors:
        print(f"ERROR: {error}")
    sys.exit(1)

print("Stage 14 Docker/production-like deployment verification passed.")
