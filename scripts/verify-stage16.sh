#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

./scripts/verify-static-stage16.sh

command -v dotnet >/dev/null 2>&1 || { echo "dotnet: command not found" >&2; exit 127; }
command -v docker >/dev/null 2>&1 || { echo "docker: command not found" >&2; exit 127; }
command -v python3 >/dev/null 2>&1 || { echo "python3: command not found" >&2; exit 127; }

dotnet --version | grep -Fx "10.0.401"
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test tests/TaskFlow.Domain.Tests/TaskFlow.Domain.Tests.csproj --no-build --no-restore --configuration Release
dotnet test tests/TaskFlow.Application.Tests/TaskFlow.Application.Tests.csproj --no-build --no-restore --configuration Release
dotnet test tests/TaskFlow.IntegrationTests/TaskFlow.IntegrationTests.csproj --no-build --no-restore --configuration Release

if [[ -f .env.compose.local ]]; then
  ./scripts/compose-down.sh --volumes || true
fi
./scripts/compose-up.sh
trap './scripts/compose-down.sh --volumes || true' EXIT
./scripts/compose-smoke.sh
python3 scripts/ci_e2e_smoke.py

python3 - <<'PY'
try:
    import playwright  # noqa: F401
except ImportError as exc:
    raise SystemExit("Python Playwright 1.63.0 is required. Install with: python3 -m pip install playwright==1.63.0 && python3 -m playwright install --with-deps chromium") from exc
PY
python3 scripts/final_browser_e2e.py
