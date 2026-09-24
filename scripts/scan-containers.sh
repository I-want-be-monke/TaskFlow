#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for container scanning." >&2
  exit 1
fi

mkdir -p artifacts/trivy
docker volume create taskflow-trivy-cache >/dev/null
scanner="aquasec/trivy:0.70.0"
tag="$(git rev-parse HEAD)"
images=(
  "taskflow-api:${tag}"
  "taskflow-migrator:${tag}"
  "taskflow-frontend:${tag}"
)

for image in "${images[@]}"; do
  safe_name="${image//[:\/]/-}"
  echo "Scanning ${image} (report HIGH/CRITICAL; gate fixable CRITICAL)..."
  docker run --rm \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -v taskflow-trivy-cache:/root/.cache/ \
    -v "$PWD/artifacts/trivy:/reports" \
    "$scanner" image \
    --scanners vuln \
    --severity HIGH,CRITICAL \
    --format json \
    --output "/reports/${safe_name}.json" \
    "$image"

  docker run --rm \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -v taskflow-trivy-cache:/root/.cache/ \
    "$scanner" image \
    --scanners vuln \
    --severity CRITICAL \
    --ignore-unfixed \
    --exit-code 1 \
    "$image"
done

echo "Container vulnerability gate passed for all TaskFlow images."
