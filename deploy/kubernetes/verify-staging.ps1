[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $KubeContext,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9.-]+$')] [string] $HostName,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidatePattern('@sha256:[a-fA-F0-9]{64}$')] [string] $ExpectedImage,
    [Parameter(Mandatory)] [string] $EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [Version]'7.3') {
    throw 'PowerShell 7.3 or later is required for fail-closed native command handling.'
}
$PSNativeCommandUseErrorActionPreference = $true

$namespace = 'banking-reconciliation-staging'
$currentContext = (kubectl config current-context).Trim()
if ($currentContext -ne $KubeContext) {
    throw "Current Kubernetes context '$currentContext' does not match required context '$KubeContext'."
}

$resolvedEvidenceDirectory = [System.IO.Path]::GetFullPath($EvidenceDirectory)
[System.IO.Directory]::CreateDirectory($resolvedEvidenceDirectory) | Out-Null
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$loadTestDirectory = Join-Path $repositoryRoot 'tests\load'
$securityTestDirectory = Join-Path $repositoryRoot 'tests\security'

$deployedImage = (kubectl --context $KubeContext --namespace $namespace get `
    deployment banking-reconciliation `
    --output=jsonpath='{.spec.template.spec.containers[0].image}').Trim()
if ($deployedImage -ne $ExpectedImage) {
    throw "Deployed image digest does not match the expected release image."
}

$deployedVersion = (kubectl --context $KubeContext --namespace $namespace get `
    configmap banking-reconciliation-config `
    --output=jsonpath='{.data.ReconciliationProduction__DeploymentVersion}').Trim()
if ($deployedVersion -ne ($Version.ToLowerInvariant() -replace '[^a-z0-9-]', '-')) {
    throw 'Deployed version does not match the requested release version.'
}

$environmentName = (kubectl --context $KubeContext --namespace $namespace get `
    configmap banking-reconciliation-config `
    --output=jsonpath='{.data.ASPNETCORE_ENVIRONMENT}').Trim()
if ($environmentName -ne 'Staging') {
    throw "Staging acceptance cannot run against ASPNETCORE_ENVIRONMENT '$environmentName'."
}

$baseUrl = "https://$HostName"
$health = Invoke-RestMethod -Uri "$baseUrl/api/health" -Method Get
$readiness = Invoke-RestMethod -Uri "$baseUrl/api/health/ready" -Method Get
$auditHealth = Invoke-RestMethod -Uri "$baseUrl/api/health/audit-retention" -Method Get
if ($health.Status -ne 'Running' -or $readiness.Status -ne 'Ready') {
    throw 'Staging health acceptance failed.'
}

$health | ConvertTo-Json -Depth 10 | Set-Content `
    -LiteralPath (Join-Path $resolvedEvidenceDirectory 'health.json') -Encoding UTF8
$readiness | ConvertTo-Json -Depth 10 | Set-Content `
    -LiteralPath (Join-Path $resolvedEvidenceDirectory 'readiness.json') -Encoding UTF8
$auditHealth | ConvertTo-Json -Depth 10 | Set-Content `
    -LiteralPath (Join-Path $resolvedEvidenceDirectory 'audit-health.json') -Encoding UTF8
kubectl --context $KubeContext --namespace $namespace get deployment `
    banking-reconciliation --output yaml | Set-Content `
        -LiteralPath (Join-Path $resolvedEvidenceDirectory 'deployment.yaml') -Encoding UTF8

$k6Image = 'grafana/k6:1.7.1@sha256:4fd3a694926b064d3491d9b02b01cde886583c4931f1223816e3d9a7bdfa7e0f'
docker run --rm `
    --volume "${loadTestDirectory}:/scripts:ro" `
    --volume "${resolvedEvidenceDirectory}:/evidence" `
    --env "BASE_URL=$baseUrl" `
    $k6Image run --summary-export /evidence/k6-summary.json /scripts/reconciliation.js

$zapImage = 'ghcr.io/zaproxy/zaproxy:stable@sha256:781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef'
docker run --rm `
    --volume "${securityTestDirectory}:/zap/rules:ro" `
    --volume "${resolvedEvidenceDirectory}:/zap/wrk:rw" `
    $zapImage zap-baseline.py `
        -t $baseUrl `
        -c /zap/rules/zap.conf `
        -J zap-report.json `
        -r zap-report.html `
        -I -s -T 10

[ordered]@{
    VerifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    KubernetesContext = $KubeContext
    Namespace = $namespace
    HostName = $HostName
    Version = $deployedVersion
    Image = $deployedImage
    Health = $health.Status
    Readiness = $readiness.Status
    K6Summary = 'k6-summary.json'
    ZapJsonReport = 'zap-report.json'
    ZapHtmlReport = 'zap-report.html'
} | ConvertTo-Json | Set-Content `
    -LiteralPath (Join-Path $resolvedEvidenceDirectory 'acceptance.json') -Encoding UTF8

Write-Host "Staging acceptance passed. Evidence: $resolvedEvidenceDirectory"
