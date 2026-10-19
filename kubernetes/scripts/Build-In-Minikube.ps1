[CmdletBinding()]
param(
    [string]$MinikubeProfile = 'minikube',
    [string]$Tag = 'dz2'
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Minikube = (Get-Command minikube -ErrorAction SilentlyContinue).Source
if (-not $Minikube) {
    $Minikube = Join-Path $env:ProgramFiles 'Kubernetes\Minikube\minikube.exe'
}

if (-not (Test-Path -LiteralPath $Minikube)) { throw 'minikube не найден.' }

# Эквивалент команды из задания `eval $(minikube docker-env)` для Windows PowerShell.
& $Minikube -p $MinikubeProfile docker-env --shell powershell | Invoke-Expression

Push-Location $RepositoryRoot
try {
    docker build --tag "taskflow-api:$Tag" --file src/TaskFlow.Api/Dockerfile .
    docker build --tag "taskflow-migrator:$Tag" --file src/TaskFlow.DbMigrator/Dockerfile .
    docker build --tag "taskflow-frontend:$Tag" --build-arg "TASKFLOW_RELEASE_ID=$Tag" --file src/TaskFlow.Client/Dockerfile .
    docker image ls 'taskflow-*' --format 'table {{.Repository}}\t{{.Tag}}\t{{.ID}}'
}
finally {
    Pop-Location
}

Write-Host 'Образы собраны непосредственно в Docker-демоне minikube и уже доступны кластеру; minikube image load для этого режима не требуется.'
