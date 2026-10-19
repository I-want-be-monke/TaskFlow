#!/usr/bin/env sh
# Build images directly in minikube's Docker daemon.
# Usage: ./kubernetes/scripts/build-in-minikube.sh [minikube-profile] [image-tag]
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
eval "$(minikube -p "$profile" docker-env --shell sh)"

cd "$repository_root"
docker build --tag "taskflow-api:$tag" --file src/TaskFlow.Api/Dockerfile .
docker build --tag "taskflow-migrator:$tag" --file src/TaskFlow.DbMigrator/Dockerfile .
docker build --tag "taskflow-frontend:$tag" --build-arg "TASKFLOW_RELEASE_ID=$tag" --file src/TaskFlow.Client/Dockerfile .
docker image ls 'taskflow-*'
