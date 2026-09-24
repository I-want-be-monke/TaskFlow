#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

./scripts/verify-static-stage15.sh

echo "Using SDK selected by global.json:"
dotnet --version

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test tests/TaskFlow.Domain.Tests/TaskFlow.Domain.Tests.csproj --no-build --no-restore --configuration Release
dotnet test tests/TaskFlow.Application.Tests/TaskFlow.Application.Tests.csproj --no-build --no-restore --configuration Release

docker info >/dev/null
dotnet test tests/TaskFlow.IntegrationTests/TaskFlow.IntegrationTests.csproj --no-build --no-restore --configuration Release
python3 scripts/check_nuget_vulnerabilities.py
./scripts/scan-secrets.sh

./scripts/compose-up.sh
trap './scripts/compose-down.sh >/dev/null 2>&1 || true' EXIT
./scripts/scan-containers.sh
./scripts/compose-smoke.sh
python3 scripts/ci_e2e_smoke.py

echo "Stage 15 verification completed successfully."
