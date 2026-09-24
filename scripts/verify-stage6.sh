#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py
python3 scripts/verify_project_features.py
python3 scripts/verify_task_tag_features.py
python3 scripts/verify_infrastructure_stage5.py
python3 scripts/verify_infrastructure_stage6.py

echo "Using SDK selected by global.json:"
dotnet --version

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release

# Stage 5-6 integration tests use a real PostgreSQL Testcontainer and require Docker.
docker info >/dev/null
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release

echo "Stage 6 verification completed successfully."
