#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

python3 scripts/verify_project_references.py

if grep -R -n -E 'Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql|TaskFlow\.Infrastructure|TaskFlow\.Api' \
  src/TaskFlow.Domain --include='*.cs' --include='*.csproj'; then
  echo "Forbidden Domain dependency/reference detected." >&2
  exit 1
fi

if grep -R -n -E 'DateTime\.UtcNow|DateTimeOffset\.UtcNow' src/TaskFlow.Domain --include='*.cs'; then
  echo "Domain must receive time from the caller instead of reading the system clock." >&2
  exit 1
fi

echo "Using SDK selected by global.json:"
dotnet --version

dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release

echo "Stage 1 verification completed successfully."
