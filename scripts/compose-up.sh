#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

env_file="${TASKFLOW_COMPOSE_ENV_FILE:-.env.compose.local}"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command '$1' was not found." >&2
    exit 1
  fi
}

require_command docker
require_command git
require_command python3

if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose v2 is required (docker compose)." >&2
  exit 1
fi

if [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; then
  echo "Stage 14 image tags are Git commit SHAs; commit/stash changes before building containers." >&2
  exit 1
fi

random_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 32
  else
    python3 - <<'PY'
import secrets
print(secrets.token_hex(32))
PY
  fi
}

if [[ ! -f "$env_file" ]]; then
  umask 077
  cat > "$env_file" <<EOF_ENV
TASKFLOW_IMAGE_TAG=$(git rev-parse HEAD)
TASKFLOW_HTTPS_PORT=${TASKFLOW_HTTPS_PORT:-8443}
POSTGRES_SUPERUSER_PASSWORD=$(random_secret)
TASKFLOW_APP_DB_PASSWORD=$(random_secret)
TASKFLOW_MIGRATOR_DB_PASSWORD=$(random_secret)
EOF_ENV
  chmod 600 "$env_file"
  echo "Generated local runtime secrets in $env_file (gitignored)."
fi

current_release_id="$(git rev-parse HEAD)"
python3 - "$env_file" "$current_release_id" <<'PY_UPDATE'
from pathlib import Path
import sys
path = Path(sys.argv[1])
release_id = sys.argv[2]
lines = path.read_text(encoding="utf-8").splitlines()
updated = []
seen = False
for line in lines:
    if line.startswith("TASKFLOW_IMAGE_TAG="):
        updated.append(f"TASKFLOW_IMAGE_TAG={release_id}")
        seen = True
    else:
        updated.append(line)
if not seen:
    updated.insert(0, f"TASKFLOW_IMAGE_TAG={release_id}")
path.write_text("\n".join(updated) + "\n", encoding="utf-8")
PY_UPDATE

docker compose --env-file "$env_file" config --quiet
docker compose --env-file "$env_file" up --build --detach
python3 scripts/compose_smoke.py --wait-only --env-file "$env_file"

port="$(awk -F= '$1=="TASKFLOW_HTTPS_PORT" {print $2}' "$env_file")"
echo "TaskFlow is running at https://localhost:${port:-8443}/"
echo "The local certificate is self-signed; accept the browser warning for this development-only stack."
