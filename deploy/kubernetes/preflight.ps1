[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $KubeContext,
    [Parameter(Mandatory)] [ValidateSet('staging', 'production')] [string] $Environment,
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [string] $AuthenticationAuthority,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9._-]+$')] [string] $ExternalSecretStore
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [Version]'7.3') {
    throw 'PowerShell 7.3 or later is required for fail-closed native command handling.'
}
$PSNativeCommandUseErrorActionPreference = $true

function Assert-CommandAvailable {
    param([Parameter(Mandatory)] [string] $Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available."
    }
}

function Assert-KubernetesPermission {
    param(
        [Parameter(Mandatory)] [string] $Verb,
        [Parameter(Mandatory)] [string] $Resource,
        [string] $Namespace
    )

    $arguments = @('--context', $KubeContext, 'auth', 'can-i', $Verb, $Resource)
    if (-not [string]::IsNullOrWhiteSpace($Namespace)) {
        $arguments += @('--namespace', $Namespace)
    }
    $allowed = (& kubectl @arguments).Trim()
    if ($allowed -ne 'yes') {
        $scope = if ($Namespace) { "namespace '$Namespace'" } else { 'cluster scope' }
        throw "Kubernetes identity cannot $Verb $Resource in $scope."
    }
}

Assert-CommandAvailable 'kubectl'
Assert-CommandAvailable 'docker'

try {
    $currentContext = (kubectl config current-context 2>$null).Trim()
}
catch {
    throw 'No current Kubernetes context is configured.'
}
if ($currentContext -ne $KubeContext) {
    throw "Current Kubernetes context '$currentContext' does not match required context '$KubeContext'."
}

$namespace = "banking-reconciliation-$Environment"
$null = kubectl --context $KubeContext version --request-timeout=10s --output=json
$null = docker info --format '{{json .ServerVersion}}'

$apiResources = kubectl --context $KubeContext api-resources `
    --api-group external-secrets.io --output=name
if ($apiResources -notcontains 'externalsecrets.external-secrets.io' -or
    $apiResources -notcontains 'clustersecretstores.external-secrets.io') {
    throw 'External Secrets Operator API resources are not available in the cluster.'
}

$secretStoreJson = kubectl --context $KubeContext get clustersecretstore `
    $ExternalSecretStore --output=json
$secretStore = $secretStoreJson | ConvertFrom-Json
$secretStoreReady = $secretStore.status.conditions | Where-Object {
    $_.type -eq 'Ready' -and $_.status -eq 'True'
}
if ($null -eq $secretStoreReady) {
    throw "ClusterSecretStore '$ExternalSecretStore' is not Ready."
}

$null = kubectl --context $KubeContext get ingressclass nginx --output=name
Assert-KubernetesPermission 'create' 'namespaces'
Assert-KubernetesPermission 'create' 'externalsecrets.external-secrets.io' $namespace
Assert-KubernetesPermission 'create' 'deployments.apps' $namespace
Assert-KubernetesPermission 'create' 'jobs.batch' $namespace

$normalizedAuthority = $AuthenticationAuthority.TrimEnd('/')
$oidcMetadata = Invoke-RestMethod `
    -Uri "$normalizedAuthority/.well-known/openid-configuration" `
    -Method Get `
    -TimeoutSec 15
if ($oidcMetadata.issuer.TrimEnd('/') -ne $normalizedAuthority) {
    throw 'OIDC discovery issuer does not match AuthenticationAuthority.'
}
if ($oidcMetadata.jwks_uri -notmatch '^https://') {
    throw 'OIDC discovery must publish an HTTPS jwks_uri.'
}

[ordered]@{
    CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Environment = $Environment
    KubernetesContext = $KubeContext
    Namespace = $namespace
    ClusterReachable = $true
    DockerReady = $true
    ExternalSecretsReady = $true
    IngressClass = 'nginx'
    OidcIssuer = $oidcMetadata.issuer
} | ConvertTo-Json
