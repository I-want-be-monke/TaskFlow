#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
import ssl
import urllib.request

ROOT = Path(__file__).resolve().parent.parent


def env_value(name: str, default: str) -> str:
    path = ROOT / ".env.compose.local"
    if not path.exists():
        return default
    for raw in path.read_text(encoding="utf-8").splitlines():
        if raw.startswith(f"{name}="):
            return raw.split("=", 1)[1].strip()
    return default


def fetch(url: str) -> tuple[int, bytes, str]:
    context = ssl.create_default_context()
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    request = urllib.request.Request(url, headers={"Accept": "text/html,application/json"})
    with urllib.request.urlopen(request, context=context, timeout=10) as response:
        return response.status, response.read(), response.headers.get("Content-Type", "")


def main() -> int:
    port = env_value("TASKFLOW_HTTPS_PORT", "8443")
    base = f"https://localhost:{port}"

    for route in ("/", "/auth/login", "/auth/register", "/projects", "/tags"):
        status, body, content_type = fetch(base + route)
        if status != 200:
            raise RuntimeError(f"Frontend route {route} returned {status}.")
        text = body.decode("utf-8", errors="replace")
        if "text/html" not in content_type.lower() or "blazor.webassembly.js" not in text:
            raise RuntimeError(f"Frontend route {route} did not serve the Blazor SPA shell.")

    status, body, content_type = fetch(base + "/api/v1/auth/antiforgery")
    if status != 200 or "application/json" not in content_type.lower():
        raise RuntimeError("Same-origin API proxy did not return JSON antiforgery response.")
    payload = json.loads(body.decode("utf-8"))
    if not isinstance(payload.get("requestToken"), str) or not payload["requestToken"]:
        raise RuntimeError("Antiforgery response did not contain requestToken.")

    print("Stage 15 E2E smoke passed: SPA routes and same-origin API boundary are reachable through HTTPS frontend.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
