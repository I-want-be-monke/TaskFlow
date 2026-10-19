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

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI не найден.' }
if (-not (Test-Path -LiteralPath $Minikube)) { throw 'minikube не найден.' }

docker info | Out-Null
& $Minikube -p $MinikubeProfile status | Out-Host

Push-Location $RepositoryRoot
try {
    docker build --tag "taskflow-api:$Tag" --file src/TaskFlow.Api/Dockerfile .
    docker build --tag "taskflow-migrator:$Tag" --file src/TaskFlow.DbMigrator/Dockerfile .
    docker build --tag "taskflow-frontend:$Tag" --build-arg "TASKFLOW_RELEASE_ID=$Tag" --file src/TaskFlow.Client/Dockerfile .

    & $Minikube -p $MinikubeProfile image load "taskflow-api:$Tag"
    & $Minikube -p $MinikubeProfile image load "taskflow-migrator:$Tag"
    & $Minikube -p $MinikubeProfile image load "taskflow-frontend:$Tag"
    & $Minikube -p $MinikubeProfile image ls | Select-String 'taskflow-(api|migrator|frontend)'
}
finally {
    Pop-Location
}
