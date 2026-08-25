[CmdletBinding()]
param(
    [string] $KubeContext = 'docker-desktop',
    [string] $Image = 'banking-reconciliation:local-release',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [Version]'7.3') {
    throw 'PowerShell 7.3 or later is required for fail-closed native command handling.'
}
$PSNativeCommandUseErrorActionPreference = $true

$currentContext = (kubectl config current-context).Trim()
if ($currentContext -ne $KubeContext) {
    throw "Current Kubernetes context '$currentContext' does not match required context '$KubeContext'."
}
if ($KubeContext -ne 'docker-desktop') {
    throw 'The local profile may run only against the docker-desktop context.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$renderRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    "banking-reconciliation-local-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($renderRoot) | Out-Null
$imageArchive = Join-Path $renderRoot 'local-image.tar'
$nodeArchive = '/var/tmp/banking-reconciliation-local-image.tar'
$nodeContainer = $null
if (-not $SkipBuild) {
    docker build --tag $Image $repositoryRoot
}
$null = docker image inspect $Image

$renderedManifest = Join-Path $renderRoot 'local.yaml'
$portForwardOutput = Join-Path $renderRoot 'port-forward.out.log'
$portForwardError = Join-Path $renderRoot 'port-forward.error.log'
$portForward = $null

try {
    $nodeName = (kubectl --context $KubeContext get nodes `
        --output=jsonpath='{.items[0].metadata.name}').Trim()
    $nodeContainer = [string](docker ps `
        --filter "name=^/$nodeName$" `
        --format '{{.Names}}')
    $nodeContainer = $nodeContainer.Trim()
    if ([string]::IsNullOrWhiteSpace($nodeContainer)) {
        throw "Docker Desktop Kubernetes node container '$nodeName' is hidden. Enable Kubernetes > Edit cluster > Show system containers (advanced), then run this script again."
    }
    docker image save --output $imageArchive $Image
    docker cp $imageArchive "${nodeContainer}:$nodeArchive"
    docker exec $nodeContainer ctr --namespace k8s.io images import $nodeArchive

    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'local.yaml') -Raw
    $manifest = $manifest.Replace('__LOCAL_IMAGE__', $Image)
    $localSigningKey = [Convert]::ToBase64String(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $manifest = $manifest.Replace('__LOCAL_SIGNING_KEY__', $localSigningKey)
    if ($manifest -match '__[A-Z0-9_]+__') {
        throw 'Local manifest contains an unresolved placeholder.'
    }
    Set-Content -LiteralPath $renderedManifest -Value $manifest -Encoding UTF8

    kubectl --context $KubeContext apply -f $renderedManifest
    kubectl --context $KubeContext --namespace banking-reconciliation-local `
        rollout restart deployment/banking-reconciliation-local
    kubectl --context $KubeContext --namespace banking-reconciliation-local `
        rollout status deployment/banking-reconciliation-local --timeout=3m

    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    $smokePort = ([Net.IPEndPoint]$portProbe.LocalEndpoint).Port
    $portProbe.Stop()
    $kubectl = Get-Command kubectl
    $portForward = Start-Process `
        -FilePath $kubectl.Source `
        -ArgumentList @(
            '--context', $KubeContext,
            '--namespace', 'banking-reconciliation-local',
            'port-forward', 'service/banking-reconciliation-local', "${smokePort}:8080"
        ) `
        -RedirectStandardOutput $portForwardOutput `
        -RedirectStandardError $portForwardError `
        -WindowStyle Hidden `
        -PassThru

    $health = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $health; $attempt++) {
        Start-Sleep -Milliseconds 500
        try {
            $health = Invoke-RestMethod `
                -Uri "http://127.0.0.1:$smokePort/api/health" `
                -TimeoutSec 2
        }
        catch {
            if ($portForward.HasExited) {
                $errorText = Get-Content -LiteralPath $portForwardError -Raw -ErrorAction SilentlyContinue
                throw "Local port-forward stopped before the health check succeeded. $errorText"
            }
        }
    }
    if ($health.Status -ne 'Running') {
        throw 'Local Kubernetes health check did not return Running.'
    }

    Write-Host 'Local Kubernetes deployment is healthy.'
    Write-Host 'Run the following command to open it at http://127.0.0.1:18080:'
    Write-Host 'kubectl --context docker-desktop --namespace banking-reconciliation-local port-forward service/banking-reconciliation-local 18080:8080'
}
finally {
    if ($null -ne $portForward -and -not $portForward.HasExited) {
        Stop-Process -Id $portForward.Id -Force
        $portForward.WaitForExit()
    }
    if (-not [string]::IsNullOrWhiteSpace($nodeContainer)) {
        docker exec $nodeContainer rm -f $nodeArchive 2>$null
    }
    $resolvedRenderRoot = [System.IO.Path]::GetFullPath($renderRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedRenderRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRenderRoot)) {
        Remove-Item -LiteralPath $resolvedRenderRoot -Recurse -Force
    }
}
