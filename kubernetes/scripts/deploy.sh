#!/usr/bin/env sh
# Apply the manifests, re-run the idempotent migration Job, and wait for readiness.
# Usage: ./kubernetes/scripts/deploy.sh [minikube-profile]
set -eu

profile=${1:-minikube}
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
manifest_root=$(CDPATH= cd -- "$script_dir/.." && pwd)

require_command() {
    command -v "$1" >/dev/null 2>&1 || {
        printf '%s\n' "Required command was not found: $1" >&2
        exit 1
    }
}

require_command kubectl
require_command minikube
minikube -p "$profile" status

cd "$manifest_root"
kubectl delete job taskflow-migrator --namespace taskflow --ignore-not-found
kubectl apply -k .
kubectl wait --namespace taskflow --for=condition=ready pod -l app.kubernetes.io/name=taskflow-postgres --timeout=180s
kubectl wait --namespace taskflow --for=condition=complete job/taskflow-migrator --timeout=180s
kubectl rollout status --namespace taskflow deployment/taskflow-api --timeout=180s
kubectl rollout status --namespace taskflow deployment/taskflow-frontend --timeout=180s
kubectl get all,pvc --namespace taskflow
printf '%s\n' 'Open the URL printed by this command:'
printf '%s\n' "minikube -p $profile service taskflow-frontend --namespace taskflow --url"
