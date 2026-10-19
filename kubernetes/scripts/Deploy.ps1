[CmdletBinding()]
param(
    [string]$MinikubeProfile = 'minikube'
)

$ErrorActionPreference = 'Stop'
$ManifestRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Minikube = (Get-Command minikube -ErrorAction SilentlyContinue).Source
if (-not $Minikube) {
    $Minikube = Join-Path $env:ProgramFiles 'Kubernetes\Minikube\minikube.exe'
}

if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) { throw 'kubectl не найден.' }
if (-not (Test-Path -LiteralPath $Minikube)) { throw 'minikube не найден.' }

Push-Location $ManifestRoot
try {
    kubectl delete job taskflow-migrator --namespace taskflow --ignore-not-found
    kubectl apply -k .
    kubectl rollout restart daemonset/taskflow-vector --namespace taskflow
    kubectl wait --namespace taskflow --for=condition=ready pod -l app.kubernetes.io/name=taskflow-postgres --timeout=180s
    kubectl wait --namespace taskflow --for=condition=complete job/taskflow-migrator --timeout=180s
    kubectl rollout status --namespace taskflow deployment/taskflow-api --timeout=180s
    kubectl rollout status --namespace taskflow deployment/taskflow-frontend --timeout=180s
    kubectl rollout status --namespace taskflow deployment/victoria-logs --timeout=180s
    kubectl rollout status --namespace taskflow daemonset/taskflow-vector --timeout=180s
    kubectl rollout status --namespace taskflow deployment/taskflow-alertmanager --timeout=180s
    kubectl rollout status --namespace taskflow deployment/taskflow-vmalert --timeout=180s
    kubectl get all,pvc --namespace taskflow
    Write-Host 'В отдельном PowerShell окне выполните:'
    Write-Host "  & '$Minikube' -p $MinikubeProfile service taskflow-frontend --namespace taskflow --url"
    Write-Host 'Для Docker driver откройте выведенный localhost-адрес, заменив http:// на https://.'
}
finally {
    Pop-Location
}
