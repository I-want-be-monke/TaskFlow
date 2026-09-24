#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

./scripts/verify-static-stage15.sh
python3 scripts/verify_final_stage16.py
