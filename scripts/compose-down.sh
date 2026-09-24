#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

env_file="${TASKFLOW_COMPOSE_ENV_FILE:-.env.compose.local}"
if [[ ! -f "$env_file" ]]; then
  echo "$env_file does not exist." >&2
  exit 1
fi

args=(down --remove-orphans)
if [[ "${1:-}" == "--volumes" ]]; then
  args+=(--volumes)
fi

docker compose --env-file "$env_file" "${args[@]}"
