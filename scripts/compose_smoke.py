#!/usr/bin/env python3
from __future__ import annotations

import argparse
import http.cookiejar
import json
from pathlib import Path
import secrets
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request

REPO_ROOT = Path(__file__).resolve().parent.parent


def parse_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip()
    return values


class TaskFlowHttp:
    def __init__(self, base_url: str) -> None:
        self.base_url = base_url.rstrip("/")
        self.cookies = http.cookiejar.CookieJar()
        context = ssl.create_default_context()
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=context),
            urllib.request.HTTPCookieProcessor(self.cookies),
        )

    def request(
        self,
        method: str,
        path: str,
        *,
        payload: dict[str, object] | None = None,
        antiforgery: str | None = None,
        expected: set[int] | None = None,
    ) -> tuple[int, object | None]:
        data = None
        headers = {"Accept": "application/json"}
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if antiforgery is not None:
            headers["X-XSRF-TOKEN"] = antiforgery

        request = urllib.request.Request(
            f"{self.base_url}{path}",
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with self.opener.open(request, timeout=10) as response:
                status = response.status
                body = response.read()
        except urllib.error.HTTPError as error:
            status = error.code
            body = error.read()

        if expected is not None and status not in expected:
            safe_body = body.decode("utf-8", errors="replace")[:500]
            raise RuntimeError(f"{method} {path} returned {status}; body={safe_body!r}")

        if not body:
            return status, None
        if body[:1] in (b"{", b"["):
            return status, json.loads(body.decode("utf-8"))
        return status, body.decode("utf-8", errors="replace")

    def antiforgery(self) -> str:
        _, body = self.request("GET", "/api/v1/auth/antiforgery", expected={200})
        if not isinstance(body, dict) or not isinstance(body.get("requestToken"), str):
            raise RuntimeError("Antiforgery endpoint did not return requestToken.")
        return body["requestToken"]

    def auth_cookie_header(self) -> str:
        for cookie in self.cookies:
            if cookie.name == "__Host-TaskFlow.Auth":
                return f"{cookie.name}={cookie.value}"
        raise RuntimeError("Authentication cookie was not issued.")


def wait_for_frontend(client: TaskFlowHttp, timeout_seconds: int = 180) -> None:
    deadline = time.monotonic() + timeout_seconds
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            client.request("GET", "/api/v1/auth/antiforgery", expected={200})
            return
        except Exception as error:  # noqa: BLE001 - startup polling intentionally retries transient failures
            last_error = error
            time.sleep(2)
    raise RuntimeError(f"TaskFlow did not become ready within {timeout_seconds}s: {last_error}")


def compose(env_file: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["docker", "compose", "--env-file", str(env_file), *args],
        cwd=REPO_ROOT,
        check=check,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Stage 14 TaskFlow container smoke test")
    parser.add_argument("--env-file", default=".env.compose.local")
    parser.add_argument("--wait-only", action="store_true")
    args = parser.parse_args()

    env_file = Path(args.env_file).resolve()
    if not env_file.exists():
        raise RuntimeError(f"Compose environment file does not exist: {env_file}")

    values = parse_env(env_file)
    port = values.get("TASKFLOW_HTTPS_PORT", "8443")
    client = TaskFlowHttp(f"https://localhost:{port}")
    wait_for_frontend(client)

    if args.wait_only:
        print("Stage 14 frontend/API startup check passed.")
        return 0

    username = f"smoke-{int(time.time())}-{secrets.token_hex(3)}"
    password = f"TaskFlow!Aa1{secrets.token_urlsafe(18)}"

    token = client.antiforgery()
    client.request(
        "POST",
        "/api/v1/auth/register",
        payload={"userName": username, "password": password},
        antiforgery=token,
        expected={201},
    )

    token = client.antiforgery()
    _, project = client.request(
        "POST",
        "/api/v1/projects",
        payload={"name": "Stage 14 smoke project", "description": "container restart persistence"},
        antiforgery=token,
        expected={201},
    )
    if not isinstance(project, dict):
        raise RuntimeError("Create project did not return a JSON object.")
    project_id = project.get("id")
    project_version = project.get("version")
    if not isinstance(project_id, str) or not isinstance(project_version, int):
        raise RuntimeError("Create project response is missing id/version.")

    client.request("GET", f"/api/v1/projects/{project_id}", expected={200})
    pre_restart_antiforgery = client.antiforgery()

    print("Restarting API container to verify stateless session/business persistence...")
    compose(env_file, "restart", "api")
    wait_for_frontend(client)

    client.request("GET", "/api/v1/auth/me", expected={200})
    _, persisted_project = client.request("GET", f"/api/v1/projects/{project_id}", expected={200})
    if not isinstance(persisted_project, dict) or persisted_project.get("id") != project_id:
        raise RuntimeError("Project was not preserved across API restart.")

    _, updated_project = client.request(
        "PUT",
        f"/api/v1/projects/{project_id}",
        payload={
            "name": "Stage 14 smoke project",
            "description": "container restart persistence",
            "version": project_version,
        },
        antiforgery=pre_restart_antiforgery,
        expected={200},
    )
    if not isinstance(updated_project, dict) or not isinstance(updated_project.get("version"), int):
        raise RuntimeError("Pre-restart antiforgery token was not accepted after API restart.")
    project_version = updated_project["version"]

    cookie_header = client.auth_cookie_header()
    ready = compose(
        env_file,
        "exec",
        "-T",
        "api",
        "curl",
        "--fail",
        "--silent",
        "--show-error",
        "--header",
        f"Cookie: {cookie_header}",
        "http://127.0.0.1:8080/health/ready",
        check=False,
    )
    if ready.returncode != 0:
        raise RuntimeError(f"Authenticated readiness probe failed: {ready.stdout}")

    token = client.antiforgery()
    client.request(
        "DELETE",
        f"/api/v1/projects/{project_id}?version={project_version}",
        antiforgery=token,
        expected={204},
    )
    client.request("POST", "/api/v1/auth/logout", antiforgery=token, expected={204})
    client.request("GET", "/api/v1/auth/me", expected={401})

    print("Stage 14 container smoke test passed: HTTPS same-origin auth, CRUD, restart, session, antiforgery, data and health.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:  # noqa: BLE001 - command-line smoke test reports one concise failure
        print(f"Stage 14 smoke test failed: {error}", file=sys.stderr)
        raise SystemExit(1)
