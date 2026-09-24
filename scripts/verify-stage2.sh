#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

python3 scripts/verify_project_references.py
python3 scripts/verify_application_contracts.py

if grep -R -n -E 'Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql|TaskFlow\.Infrastructure|TaskFlow\.Api|HttpContext' \
  src/TaskFlow.Application --include='*.cs' --include='*.csproj'; then
  echo "Forbidden Application dependency/reference detected." >&2
  exit 1
fi

if grep -R -n -E '\bIQueryable([<[:space:]]|$)' src/TaskFlow.Application --include='*.cs'; then
  echo "IQueryable must not escape Infrastructure." >&2
  exit 1
fi

echo "Using SDK selected by global.json:"
dotnet --version

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release

echo "Stage 2 verification completed successfully."
