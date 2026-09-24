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
python3 scripts/verify_hardening_stage9.py
python3 scripts/verify_observability_stage10.py
python3 scripts/verify_migrator_stage11.py
python3 scripts/verify_client_stage12.py
python3 scripts/verify_client_ui_stage13.py
python3 scripts/verify_containers_stage14.py
python3 scripts/verify_ci_stage15.py
