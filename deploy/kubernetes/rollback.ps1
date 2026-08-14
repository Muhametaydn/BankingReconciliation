[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('staging', 'production')] [string] $Environment,
    [Parameter(Mandatory)] [string] $KubeContext,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9.-]+$')] [string] $HostName,
    [Parameter(Mandatory)] [ValidatePattern('@sha256:[a-fA-F0-9]{64}$')] [string] $ExpectedImage,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [int] $ToRevision,
    [switch] $ApproveProductionRollback
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [Version]'7.3') {
    throw 'PowerShell 7.3 or later is required for fail-closed native command handling.'
}
$PSNativeCommandUseErrorActionPreference = $true
$namespace = "banking-reconciliation-$Environment"
if ($Environment -eq 'production' -and
    (-not $ApproveProductionRollback -or $ToRevision -le 0)) {
    throw 'Production rollback requires -ApproveProductionRollback and an explicit positive -ToRevision.'
}
$currentContext = (kubectl config current-context).Trim()
if ($currentContext -ne $KubeContext) {
    throw "Current Kubernetes context '$currentContext' does not match required context '$KubeContext'."
}

$resolvedEvidenceDirectory = [System.IO.Path]::GetFullPath($EvidenceDirectory)
[System.IO.Directory]::CreateDirectory($resolvedEvidenceDirectory) | Out-Null
$previousImage = (kubectl --context $KubeContext --namespace $namespace get `
    deployment banking-reconciliation `
    --output=jsonpath='{.spec.template.spec.containers[0].image}').Trim()

$arguments = @(
    '--context', $KubeContext,
    'rollout', 'undo', 'deployment/banking-reconciliation',
    '--namespace', $namespace
)
if ($ToRevision -gt 0) {
    kubectl --context $KubeContext rollout history deployment/banking-reconciliation `
        --namespace $namespace --revision=$ToRevision | Out-Null
    $arguments += "--to-revision=$ToRevision"
}

& kubectl @arguments
kubectl --context $KubeContext rollout status deployment/banking-reconciliation --namespace $namespace --timeout=10m
$rolledBackImage = (kubectl --context $KubeContext get deployment `
    banking-reconciliation --namespace $namespace `
    --output=jsonpath='{.spec.template.spec.containers[0].image}').Trim()
if ($rolledBackImage -ne $ExpectedImage) {
    throw 'Rollback completed to an image other than the explicitly expected digest.'
}
$readiness = Invoke-RestMethod -Uri "https://$HostName/api/health/ready" -Method Get
if ($readiness.Status -ne 'Ready') {
    throw 'Rolled-back application did not become ready through the public HTTPS endpoint.'
}

[ordered]@{
    VerifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    KubernetesContext = $KubeContext
    Namespace = $namespace
    RequestedRevision = $ToRevision
    PreviousImage = $previousImage
    RolledBackImage = $rolledBackImage
    Readiness = $readiness.Status
    DatabaseMigrationReversed = $false
} | ConvertTo-Json | Set-Content `
    -LiteralPath (Join-Path $resolvedEvidenceDirectory 'rollback-acceptance.json') `
    -Encoding UTF8

Write-Host "Application rollback completed and verified. Database migrations are intentionally not reversed."
