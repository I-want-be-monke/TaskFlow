#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

mkdir -p artifacts
image="zricethezav/gitleaks:v8.30.1"

docker run --rm \
  --user "$(id -u):$(id -g)" \
  -v "$PWD:/repo:ro" \
  -v "$PWD/artifacts:/reports" \
  "$image" \
  git --redact --report-format json --report-path /reports/gitleaks-report.json /repo

echo "Gitleaks secret scan passed using ${image}."
