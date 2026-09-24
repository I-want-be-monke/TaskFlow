#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
python3 scripts/verify_task_tag_features.py
python3 scripts/verify_infrastructure_stage5.py
python3 scripts/verify_infrastructure_stage6.py
python3 scripts/verify_api_stage7.py
python3 scripts/verify_security_stage8.py

echo "Using SDK selected by global.json:"
dotnet --version

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release

# Persistence and Stage 8 HTTP security integration tests use PostgreSQL Testcontainers.
docker info >/dev/null
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release

echo "Stage 8 verification completed successfully."
