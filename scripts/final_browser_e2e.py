#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import secrets
import subprocess
import sys
import time
from typing import Any

from playwright.sync_api import BrowserContext, Page, sync_playwright

ROOT = Path(__file__).resolve().parent.parent


def parse_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip()
    return values


def compose(env_file: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["docker", "compose", "--env-file", str(env_file), *args],
        cwd=ROOT,
        check=check,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )


def expect_status(response: Any, expected: int, description: str) -> Any:
    if response.status != expected:
        body = response.text()[:700]
        raise RuntimeError(f"{description} returned HTTP {response.status}; body={body!r}")
    return response


def json_body(response: Any, description: str) -> dict[str, Any]:
    try:
        body = response.json()
    except Exception as error:  # noqa: BLE001 - E2E assertion reports response safely
        raise RuntimeError(f"{description} did not return JSON: {response.text()[:500]!r}") from error
    if not isinstance(body, dict):
        raise RuntimeError(f"{description} returned non-object JSON: {type(body).__name__}")
    return body


def antiforgery(context: BrowserContext, base_url: str) -> str:
    response = context.request.get(f"{base_url}/api/v1/auth/antiforgery")
    expect_status(response, 200, "antiforgery token request")
    payload = json_body(response, "antiforgery token request")
    token = payload.get("requestToken")
    if not isinstance(token, str) or not token:
        raise RuntimeError("Antiforgery response did not contain requestToken.")
    return token


def api_request(
    context: BrowserContext,
    base_url: str,
    method: str,
    path: str,
    *,
    payload: dict[str, Any] | None = None,
    csrf: str | None = None,
    expected: int,
) -> Any:
    headers = {"Accept": "application/json"}
    if csrf is not None:
        headers["X-XSRF-TOKEN"] = csrf
    response = context.request.fetch(
        f"{base_url}{path}",
        method=method,
        data=payload,
        headers=headers,
    )
    return expect_status(response, expected, f"{method} {path}")


def wait_for_url(page: Page, pattern: str) -> None:
    page.wait_for_url(pattern, wait_until="networkidle", timeout=20_000)


def fill_project_form(page: Page, name: str, description: str, testid: str) -> None:
    panel = page.get_by_test_id(testid)
    panel.get_by_label("Name").fill(name)
    panel.get_by_label("Description").fill(description)


def row_with_text(page: Page, testid: str, text: str) -> Any:
    container = page.get_by_test_id(testid)
    row = container.locator("tbody tr", has_text=text)
    row.wait_for(state="visible", timeout=15_000)
    return row


def current_auth_cookie(context: BrowserContext) -> str:
    for cookie in context.cookies():
        if cookie["name"] == "__Host-TaskFlow.Auth":
            value = cookie.get("value")
            if isinstance(value, str) and value:
                return value
    raise RuntimeError("Authentication cookie was not available to the E2E harness.")


def assert_internal_health(env_file: Path, auth_cookie: str) -> None:
    live = compose(
        env_file,
        "exec",
        "-T",
        "api",
        "curl",
        "--silent",
        "--output",
        "/dev/null",
        "--write-out",
        "%{http_code}",
        "http://127.0.0.1:8080/health/live",
        check=False,
    )
    if live.returncode != 0 or live.stdout.strip() != "200":
        raise RuntimeError(f"Internal liveness must be anonymous 200; got {live.stdout!r}.")

    ready_anon = compose(
        env_file,
        "exec",
        "-T",
        "api",
        "curl",
        "--silent",
        "--output",
        "/dev/null",
        "--write-out",
        "%{http_code}",
        "http://127.0.0.1:8080/health/ready",
        check=False,
    )
    if ready_anon.stdout.strip() != "401":
        raise RuntimeError(f"Anonymous readiness must remain protected (401); got {ready_anon.stdout!r}.")

    ready_auth = compose(
        env_file,
        "exec",
        "-T",
        "api",
        "curl",
        "--silent",
        "--output",
        "/dev/null",
        "--write-out",
        "%{http_code}",
        "--header",
        f"Cookie: __Host-TaskFlow.Auth={auth_cookie}",
        "http://127.0.0.1:8080/health/ready",
        check=False,
    )
    if ready_auth.returncode != 0 or ready_auth.stdout.strip() != "200":
        raise RuntimeError(f"Authenticated readiness must be 200; got {ready_auth.stdout!r}.")


def assert_database_least_privilege(env_file: Path, values: dict[str, str]) -> None:
    app_password = values.get("TASKFLOW_APP_DB_PASSWORD", "")
    migrator_password = values.get("TASKFLOW_MIGRATOR_DB_PASSWORD", "")
    if not app_password or not migrator_password:
        raise RuntimeError("Compose environment is missing database role passwords.")

    forbidden_table = f"stage16_api_forbidden_{secrets.token_hex(4)}"
    api_ddl = compose(
        env_file,
        "exec",
        "-T",
        "-e",
        f"PGPASSWORD={app_password}",
        "postgres",
        "psql",
        "--host",
        "127.0.0.1",
        "--username",
        "taskflow_app",
        "--dbname",
        "taskflow",
        "--set",
        "ON_ERROR_STOP=1",
        "--command",
        f"CREATE TABLE {forbidden_table}(id integer);",
        check=False,
    )
    if api_ddl.returncode == 0:
        compose(
            env_file,
            "exec",
            "-T",
            "-e",
            f"PGPASSWORD={migrator_password}",
            "postgres",
            "psql",
            "--host",
            "127.0.0.1",
            "--username",
            "taskflow_migrator",
            "--dbname",
            "taskflow",
            "--command",
            f"DROP TABLE IF EXISTS {forbidden_table};",
            check=False,
        )
        raise RuntimeError("taskflow_app unexpectedly has schema DDL privilege.")

    history = compose(
        env_file,
        "exec",
        "-T",
        "-e",
        f"PGPASSWORD={migrator_password}",
        "postgres",
        "psql",
        "--host",
        "127.0.0.1",
        "--username",
        "taskflow_migrator",
        "--dbname",
        "taskflow",
        "--tuples-only",
        "--no-align",
        "--command",
        'SELECT count(*) FROM "__EFMigrationsHistory";',
        check=False,
    )
    if history.returncode != 0:
        raise RuntimeError(f"Migrator role could not read migration history: {history.stdout}")
    try:
        applied_count = int(history.stdout.strip())
    except ValueError as error:
        raise RuntimeError(f"Migration history count was not numeric: {history.stdout!r}") from error
    if applied_count < 1:
        raise RuntimeError("Empty-volume deployment did not apply any EF migration.")


def assert_logs_redacted(
    env_file: Path,
    values: dict[str, str],
    *,
    password: str,
    auth_cookie: str,
    antiforgery_token: str,
) -> None:
    logs = compose(env_file, "logs", "--no-color", "api", "migrator", "frontend", check=False).stdout
    forbidden_values = {
        "browser password": password,
        "auth cookie": auth_cookie,
        "antiforgery token": antiforgery_token,
        "app DB password": values.get("TASKFLOW_APP_DB_PASSWORD", ""),
        "migrator DB password": values.get("TASKFLOW_MIGRATOR_DB_PASSWORD", ""),
        "bootstrap DB password": values.get("POSTGRES_SUPERUSER_PASSWORD", ""),
    }
    leaked = [name for name, value in forbidden_values.items() if value and value in logs]
    if leaked:
        raise RuntimeError("Sensitive values were found in container logs: " + ", ".join(leaked))


def register_api_user(context: BrowserContext, base_url: str, username: str, password: str) -> None:
    token = antiforgery(context, base_url)
    api_request(
        context,
        base_url,
        "POST",
        "/api/v1/auth/register",
        payload={"userName": username, "password": password},
        csrf=token,
        expected=201,
    )


def run_browser_flow(base_url: str, env_file: Path, values: dict[str, str]) -> None:
    run_id = f"{int(time.time())}-{secrets.token_hex(3)}"
    username = f"stage16-{run_id}"[:64]
    password = f"TaskFlow!Aa1{secrets.token_urlsafe(18)}"
    project_name = f"Stage16 project {run_id}"
    project_edited = f"Stage16 edited {run_id}"
    task_name = f"Stage16 task {run_id}"
    task_edited = f"Stage16 task edited {run_id}"
    tag_name = f"stage16-{secrets.token_hex(4)}"

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=True)
        context = browser.new_context(ignore_https_errors=True)
        page = context.new_page()

        # Register through the real Blazor form, then deliberately logout/login to cover both paths.
        page.goto(f"{base_url}/auth/register", wait_until="networkidle")
        page.get_by_label("User name").fill(username)
        page.get_by_label("Password", exact=True).fill(password)
        page.get_by_label("Confirm password").fill(password)
        page.get_by_role("button", name="Create account").click()
        wait_for_url(page, "**/projects")
        page.get_by_role("heading", name="Projects").wait_for()

        page.get_by_role("button", name="Logout").click()
        wait_for_url(page, "**/auth/login")
        page.get_by_label("User name").fill(username)
        page.get_by_label("Password").fill(password)
        page.get_by_role("button", name="Sign in").click()
        wait_for_url(page, "**/projects")

        # Project create/edit through UI.
        page.get_by_role("button", name="New project").click()
        fill_project_form(page, project_name, "Final browser E2E project", "project-create-form")
        page.get_by_test_id("project-create-form").get_by_role("button", name="Create project").click()
        wait_for_url(page, re.compile(r".*/projects/[0-9a-fA-F-]{36}$"))
        project_id = page.url.rstrip("/").split("/")[-1]
        page.get_by_role("heading", name=project_name).wait_for()

        page.get_by_role("link", name="Edit project").click()
        wait_for_url(page, f"**/projects/{project_id}/edit")
        fill_project_form(page, project_edited, "Edited through browser E2E", "project-edit-form")
        page.get_by_test_id("project-edit-form").get_by_role("button", name="Save changes").click()
        wait_for_url(page, f"**/projects/{project_id}")
        page.get_by_role("heading", name=project_edited).wait_for()

        # Task create/edit through UI.
        page.get_by_role("link", name="New task").click()
        wait_for_url(page, f"**/projects/{project_id}/tasks/new")
        task_form = page.get_by_test_id("task-create-form")
        task_form.get_by_label("Title").fill(task_name)
        task_form.get_by_label("Description").fill("Created by final browser E2E")
        task_form.get_by_label("Status").select_option("Todo")
        task_form.get_by_label("Priority").select_option("High")
        task_form.get_by_role("button", name="Create task").click()
        wait_for_url(page, f"**/projects/{project_id}")
        task_row = row_with_text(page, "task-list", task_name)
        task_row.get_by_role("link", name="Edit").click()
        wait_for_url(page, re.compile(r".*/tasks/[0-9a-fA-F-]{36}/edit$"))
        task_id = page.url.rstrip("/").split("/")[-2]
        edit_task = page.get_by_test_id("task-edit-form")
        edit_task.get_by_label("Title").fill(task_edited)
        edit_task.get_by_label("Description").fill("Edited through browser E2E")
        edit_task.get_by_label("Status").select_option("InProgress")
        edit_task.get_by_label("Priority").select_option("Medium")
        edit_task.get_by_role("button", name="Save changes").click()
        wait_for_url(page, f"**/projects/{project_id}")
        row_with_text(page, "task-list", task_edited)

        # Tag CRUD create path, then attach through task UI.
        page.get_by_role("link", name="Tags").click()
        wait_for_url(page, "**/tags")
        page.get_by_role("button", name="New tag").click()
        tag_create = page.get_by_test_id("tag-create-form")
        tag_create.get_by_label("Name").fill(tag_name)
        tag_create.get_by_role("button", name="Create tag").click()
        row_with_text(page, "tags-list", tag_name)

        page.get_by_role("link", name="Projects").click()
        wait_for_url(page, "**/projects")
        page.get_by_role("link", name=project_edited, exact=True).click()
        wait_for_url(page, f"**/projects/{project_id}")
        task_row = row_with_text(page, "task-list", task_edited)
        task_row.locator("select.input-compact").select_option(label=tag_name)
        task_row.get_by_role("button", name="Attach").click()
        page.get_by_text("Tag attached.", exact=False).wait_for()

        # Filter/list path.
        filters = page.get_by_test_id("task-filters")
        filters.get_by_label("Search").fill(task_edited)
        filters.get_by_label("Status").select_option("InProgress")
        filters.get_by_label("Priority").select_option("Medium")
        filters.get_by_label("Tag").select_option(label=tag_name)
        filters.get_by_label("Sort").select_option("title:asc")
        filters.get_by_role("button", name="Apply filters").click()
        row_with_text(page, "task-list", task_edited)

        # Page refresh must restore auth through /auth/me and keep current route usable.
        page.reload(wait_until="networkidle")
        page.get_by_role("heading", name=project_edited).wait_for()
        page.get_by_role("button", name="Logout").wait_for()

        # BOLA: a second user creates a Project; the first session must see 404 for its UUID.
        foreign_context = browser.new_context(ignore_https_errors=True)
        foreign_username = f"foreign-{run_id}"[:64]
        foreign_password = f"TaskFlow!Bb2{secrets.token_urlsafe(18)}"
        register_api_user(foreign_context, base_url, foreign_username, foreign_password)
        foreign_csrf = antiforgery(foreign_context, base_url)
        foreign_response = api_request(
            foreign_context,
            base_url,
            "POST",
            "/api/v1/projects",
            payload={"name": "Foreign project", "description": "BOLA probe"},
            csrf=foreign_csrf,
            expected=201,
        )
        foreign_project = json_body(foreign_response, "foreign project create")
        foreign_project_id = str(foreign_project["id"])
        foreign_project_version = int(foreign_project["version"])
        api_request(context, base_url, "GET", f"/api/v1/projects/{foreign_project_id}", expected=404)

        # CSRF: authenticated unsafe request without request token must fail.
        api_request(
            context,
            base_url,
            "POST",
            "/api/v1/projects",
            payload={"name": "Must not be created", "description": None},
            expected=400,
        )

        # Version conflict UX: mutate the Project out-of-band, then submit the stale browser form.
        page.get_by_role("link", name="Edit project").click()
        wait_for_url(page, f"**/projects/{project_id}/edit")
        current = json_body(
            api_request(context, base_url, "GET", f"/api/v1/projects/{project_id}", expected=200),
            "project read before conflict",
        )
        conflict_csrf = antiforgery(context, base_url)
        server_name = f"{project_edited} server"
        api_request(
            context,
            base_url,
            "PUT",
            f"/api/v1/projects/{project_id}",
            payload={
                "name": server_name,
                "description": "Server-side concurrent update",
                "version": int(current["version"]),
            },
            csrf=conflict_csrf,
            expected=200,
        )
        page.get_by_test_id("project-edit-form").get_by_label("Description").fill("Stale browser write")
        page.get_by_test_id("project-edit-form").get_by_role("button", name="Save changes").click()
        error_panel = page.get_by_test_id("api-error")
        error_panel.get_by_text("This record changed on the server.").wait_for()
        error_panel.get_by_role("button", name="Reload latest").click()
        page.get_by_test_id("project-edit-form").get_by_label("Name").wait_for()
        if page.get_by_test_id("project-edit-form").get_by_label("Name").input_value() != server_name:
            raise RuntimeError("Reload latest did not refresh the Project after a version conflict.")
        page.get_by_role("link", name="Back").click()
        wait_for_url(page, f"**/projects/{project_id}")

        # Archive through UI and prove the server atomically blocks Task mutation while archived.
        page.get_by_role("button", name="Archive").click()
        page.get_by_text("This project is archived.", exact=False).wait_for()
        task = json_body(
            api_request(context, base_url, "GET", f"/api/v1/tasks/{task_id}", expected=200),
            "task read while archived",
        )
        archived_csrf = antiforgery(context, base_url)
        blocked = api_request(
            context,
            base_url,
            "PUT",
            f"/api/v1/tasks/{task_id}",
            payload={
                "title": str(task["title"]),
                "description": task.get("description"),
                "status": str(task["status"]),
                "priority": str(task["priority"]),
                "dueAt": task.get("dueAt"),
                "version": int(task["version"]),
            },
            csrf=archived_csrf,
            expected=409,
        )
        blocked_problem = json_body(blocked, "archived task mutation")
        if blocked_problem.get("code") != "tasks.project_archived":
            raise RuntimeError(f"Archived mutation returned unexpected code: {blocked_problem.get('code')!r}")

        page.get_by_role("button", name="Restore").click()
        page.get_by_role("link", name="New task").wait_for()

        # Restart API and verify shared Data Protection/business state with the same browser session.
        auth_cookie = current_auth_cookie(context)
        persisted_token = antiforgery(context, base_url)
        compose(env_file, "restart", "api")
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            try:
                probe = context.request.get(f"{base_url}/api/v1/auth/me", timeout=5_000)
                if probe.status == 200:
                    break
            except Exception:  # noqa: BLE001 - restart polling retries transient connection errors
                pass
            time.sleep(2)
        else:
            raise RuntimeError("API session did not recover after container restart.")

        page.reload(wait_until="networkidle")
        page.get_by_role("heading", name=server_name).wait_for()
        api_request(context, base_url, "GET", f"/api/v1/projects/{project_id}", expected=200)

        # A request token minted before restart remains valid because Data Protection keys are shared in PostgreSQL.
        post_restart_project = json_body(
            api_request(context, base_url, "GET", f"/api/v1/projects/{project_id}", expected=200),
            "post-restart project read",
        )
        api_request(
            context,
            base_url,
            "PUT",
            f"/api/v1/projects/{project_id}",
            payload={
                "name": server_name,
                "description": "Still authenticated after restart",
                "version": int(post_restart_project["version"]),
            },
            csrf=persisted_token,
            expected=200,
        )

        assert_internal_health(env_file, auth_cookie)
        assert_database_least_privilege(env_file, values)

        # Cleanup through browser UI: Task -> Tag -> Project -> Logout.
        page.reload(wait_until="networkidle")
        task_row = row_with_text(page, "task-list", task_edited)
        task_row.get_by_role("button", name="Delete").click()
        task_row.get_by_role("button", name="Confirm").click()
        page.get_by_text("No matching tasks").wait_for()

        page.get_by_role("link", name="Tags").click()
        wait_for_url(page, "**/tags")
        tag_row = row_with_text(page, "tags-list", tag_name)
        tag_row.get_by_role("button", name="Delete").click()
        tag_row.get_by_role("button", name="Confirm delete").click()

        page.get_by_role("link", name="Projects").click()
        wait_for_url(page, "**/projects")
        page.get_by_role("link", name=server_name, exact=True).click()
        wait_for_url(page, f"**/projects/{project_id}")
        page.get_by_role("button", name="Delete").click()
        page.get_by_test_id("project-delete-confirmation").get_by_role("button", name="Confirm delete").click()
        wait_for_url(page, "**/projects")

        # Clean foreign user data via its own owner-scoped session.
        foreign_delete_csrf = antiforgery(foreign_context, base_url)
        api_request(
            foreign_context,
            base_url,
            "DELETE",
            f"/api/v1/projects/{foreign_project_id}?version={foreign_project_version}",
            csrf=foreign_delete_csrf,
            expected=204,
        )
        foreign_context.close()

        # Capture real secrets before logout, then assert they never appeared in logs.
        final_log_token = antiforgery(context, base_url)
        assert_logs_redacted(
            env_file,
            values,
            password=password,
            auth_cookie=auth_cookie,
            antiforgery_token=final_log_token,
        )

        page.get_by_role("button", name="Logout").click()
        wait_for_url(page, "**/auth/login")
        expect_status(context.request.get(f"{base_url}/api/v1/auth/me"), 401, "auth/me after logout")

        context.close()
        browser.close()


def main() -> int:
    parser = argparse.ArgumentParser(description="TaskFlow Stage 16 final browser E2E / Definition of Done runtime flow")
    parser.add_argument("--env-file", default=".env.compose.local")
    args = parser.parse_args()

    env_file = Path(args.env_file).resolve()
    if not env_file.exists():
        raise RuntimeError(f"Compose environment file does not exist: {env_file}")
    values = parse_env(env_file)
    port = values.get("TASKFLOW_HTTPS_PORT", "8443")
    base_url = f"https://localhost:{port}"

    run_browser_flow(base_url, env_file, values)
    print("Stage 16 final browser E2E passed: CRUD, auth, CSRF, BOLA, version conflict, archive invariant, restart, health, least privilege and log redaction.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:  # noqa: BLE001 - CLI test emits one concise failure
        print(f"Stage 16 browser E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
