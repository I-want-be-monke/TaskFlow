#!/usr/bin/env sh
# Build the three TaskFlow images locally and load them into minikube.
# Usage: ./kubernetes/scripts/build-and-load.sh [minikube-profile] [image-tag]
set -eu

profile=${1:-minikube}
tag=${2:-dz2}
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)

require_command() {
    command -v "$1" >/dev/null 2>&1 || {
        printf '%s\n' "Required command was not found: $1" >&2
        exit 1
    }
}

require_command docker
require_command minikube
minikube -p "$profile" status

cd "$repository_root"
docker build --tag "taskflow-api:$tag" --file src/TaskFlow.Api/Dockerfile .
docker build --tag "taskflow-migrator:$tag" --file src/TaskFlow.DbMigrator/Dockerfile .
docker build --tag "taskflow-frontend:$tag" --build-arg "TASKFLOW_RELEASE_ID=$tag" --file src/TaskFlow.Client/Dockerfile .

minikube -p "$profile" image load "taskflow-api:$tag"
minikube -p "$profile" image load "taskflow-migrator:$tag"
minikube -p "$profile" image load "taskflow-frontend:$tag"
minikube -p "$profile" image ls | grep 'taskflow-\(api\|migrator\|frontend\)'
