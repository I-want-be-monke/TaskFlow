#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

env_file="${TASKFLOW_COMPOSE_ENV_FILE:-.env.compose.local}"
if [[ ! -f "$env_file" ]]; then
  echo "$env_file does not exist. Run ./scripts/compose-up.sh first." >&2
  exit 1
fi

python3 scripts/compose_smoke.py --env-file "$env_file"
