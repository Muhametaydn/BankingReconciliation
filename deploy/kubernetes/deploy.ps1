[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('staging', 'production')] [string] $Environment,
    [Parameter(Mandatory)] [string] $KubeContext,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidatePattern('@sha256:[a-fA-F0-9]{64}$')] [string] $Image,
    [Parameter(Mandatory)] [ValidatePattern('@sha256:[a-fA-F0-9]{64}$')] [string] $BackupImage,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9.-]+$')] [string] $HostName,
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [string] $AuthenticationAuthority,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9._:/-]+$')] [string] $AuthenticationAudience,
    [Parameter(Mandatory)] [string] $UploadBucket,
    [Parameter(Mandatory)] [ValidatePattern('^\d{12}$')] [string] $ExpectedBucketOwner,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z]{2}-[a-z]+-\d$')] [string] $AwsRegion,
    [Parameter(Mandatory)] [string] $BackupBucket,
    [Parameter(Mandatory)] [string] $BackupKmsKeyId,
    [Parameter(Mandatory)] [ValidatePattern('^https?://')] [string] $OtlpEndpoint,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9a-fA-F:.]+/\d{1,3}$')] [string] $KnownProxyNetwork,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9._-]+$')] [string] $ExternalSecretStore,
    [Parameter(Mandatory)] [string] $ApplicationSecretId,
    [Parameter(Mandatory)] [string] $BackupSecretId,
    [Parameter(Mandatory)] [string] $RestoreSecretId,
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [switch] $ApproveProductionDeployment
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [Version]'7.3') {
    throw 'PowerShell 7.3 or later is required for fail-closed native command handling.'
}
$PSNativeCommandUseErrorActionPreference = $true
$namespace = "banking-reconciliation-$Environment"
$safeVersion = $Version.ToLowerInvariant() -replace '[^a-z0-9-]', '-'
if ([string]::IsNullOrWhiteSpace($safeVersion) -or $safeVersion.Length -gt 40) {
    throw 'Version must produce a non-empty Kubernetes-safe value of at most 40 characters.'
}
if ($Environment -eq 'production' -and -not $ApproveProductionDeployment) {
    throw 'Production deployment requires the explicit -ApproveProductionDeployment switch.'
}
$aspNetCoreEnvironment = if ($Environment -eq 'production') { 'Production' } else { 'Staging' }
$resolvedEvidenceDirectory = [System.IO.Path]::GetFullPath($EvidenceDirectory)
[System.IO.Directory]::CreateDirectory($resolvedEvidenceDirectory) | Out-Null
$currentContext = (kubectl config current-context).Trim()
if ($currentContext -ne $KubeContext) {
    throw "Current Kubernetes context '$currentContext' does not match required context '$KubeContext'."
}

$normalizedAuthority = $AuthenticationAuthority.TrimEnd('/')
$oidcMetadata = Invoke-RestMethod -Uri "$normalizedAuthority/.well-known/openid-configuration" -Method Get
if ($oidcMetadata.issuer.TrimEnd('/') -ne $normalizedAuthority) {
    throw "OIDC discovery issuer does not match AuthenticationAuthority."
}
if ($oidcMetadata.jwks_uri -notmatch '^https://') {
    throw "OIDC discovery must publish an HTTPS jwks_uri."
}

$scriptRoot = $PSScriptRoot
$renderRoot = Join-Path ([System.IO.Path]::GetTempPath()) "banking-reconciliation-deploy-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($renderRoot) | Out-Null

function Render-Manifest {
    param([Parameter(Mandatory)] [string] $Name)

    $source = Join-Path $scriptRoot $Name
    $target = Join-Path $renderRoot $Name
    $content = Get-Content -LiteralPath $source -Raw
    $content = $content.Replace('namespace: banking-reconciliation', "namespace: $namespace")
    $content = $content.Replace('__VERSION__', $safeVersion)
    $content = $content.Replace('__ASPNETCORE_ENVIRONMENT__', $aspNetCoreEnvironment)
    $content = $content.Replace('__IMAGE__', $Image)
    $content = $content.Replace('__BACKUP_IMAGE__', $BackupImage)
    $content = $content.Replace('__HOST__', $HostName)
    $content = $content.Replace('__AUTHORITY__', $AuthenticationAuthority)
    $content = $content.Replace('__AUTHENTICATION_AUDIENCE__', $AuthenticationAudience)
    $content = $content.Replace('__UPLOAD_BUCKET__', $UploadBucket)
    $content = $content.Replace('__EXPECTED_OWNER__', $ExpectedBucketOwner)
    $content = $content.Replace('__AWS_REGION__', $AwsRegion)
    $content = $content.Replace('__BACKUP_BUCKET__', $BackupBucket)
    $content = $content.Replace('__BACKUP_KMS_KEY_ID__', $BackupKmsKeyId)
    $content = $content.Replace('__OTLP_ENDPOINT__', $OtlpEndpoint)
    $content = $content.Replace('__KNOWN_PROXY_NETWORK__', $KnownProxyNetwork)
    $content = $content.Replace('__EXTERNAL_SECRET_STORE__', $ExternalSecretStore)
    $content = $content.Replace('__APPLICATION_SECRET_ID__', $ApplicationSecretId)
    $content = $content.Replace('__BACKUP_SECRET_ID__', $BackupSecretId)
    $content = $content.Replace('__RESTORE_SECRET_ID__', $RestoreSecretId)
    if ($content -match '__[A-Z0-9_]+__') {
        throw "Manifest '$Name' contains an unresolved deployment placeholder."
    }
    Set-Content -LiteralPath $target -Value $content -Encoding UTF8
    return $target
}

function Assert-SecretKey {
    param(
        [Parameter(Mandatory)] [string] $SecretName,
        [Parameter(Mandatory)] [string] $Key
    )

    $jsonPath = "{.data['$Key']}"
    $encodedValue = kubectl --context $KubeContext --namespace $namespace get secret $SecretName --output "jsonpath=$jsonPath"
    if ([string]::IsNullOrWhiteSpace($encodedValue)) {
        throw "Required key '$Key' is missing from secret '$SecretName'."
    }
}

try {
    $namespaceManifest = Get-Content -LiteralPath (Join-Path $scriptRoot 'namespace.yaml') -Raw
    $namespaceManifest = $namespaceManifest.Replace('name: banking-reconciliation', "name: $namespace")
    $namespaceManifest = $namespaceManifest.Replace('namespace: banking-reconciliation', "namespace: $namespace")
    $namespacePath = Join-Path $renderRoot 'namespace.yaml'
    Set-Content -LiteralPath $namespacePath -Value $namespaceManifest -Encoding UTF8
    kubectl --context $KubeContext apply -f $namespacePath

    $externalSecretsPath = Render-Manifest 'external-secrets.yaml'
    kubectl --context $KubeContext apply -f $externalSecretsPath
    foreach ($externalSecret in @(
        'banking-reconciliation',
        'banking-reconciliation-backup',
        'banking-reconciliation-restore-verify'
    )) {
        kubectl --context $KubeContext --namespace $namespace wait `
            --for=condition=Ready "externalsecret/$externalSecret" --timeout=2m
    }

    Assert-SecretKey 'banking-reconciliation-secrets' 'reconciliation-database'
    Assert-SecretKey 'banking-reconciliation-secrets' 'reconciliation-migration-database'
    Assert-SecretKey 'banking-reconciliation-backup-secrets' 'host'
    Assert-SecretKey 'banking-reconciliation-backup-secrets' 'username'
    Assert-SecretKey 'banking-reconciliation-backup-secrets' 'pgpass'
    Assert-SecretKey 'banking-reconciliation-restore-verify-secrets' 'host'
    Assert-SecretKey 'banking-reconciliation-restore-verify-secrets' 'username'
    Assert-SecretKey 'banking-reconciliation-restore-verify-secrets' 'pgpass'
    kubectl --context $KubeContext --namespace $namespace get secret banking-reconciliation-tls | Out-Null

    $configPath = Render-Manifest 'configmap.yaml'
    $migrationPath = Render-Manifest 'migration-job.yaml'
    kubectl --context $KubeContext apply -f $configPath
    kubectl --context $KubeContext delete job "banking-reconciliation-migrate-$safeVersion" --namespace $namespace --ignore-not-found
    kubectl --context $KubeContext apply -f $migrationPath
    kubectl --context $KubeContext wait --for=condition=complete "job/banking-reconciliation-migrate-$safeVersion" --namespace $namespace --timeout=10m

    foreach ($manifest in @(
        'deployment.yaml',
        'availability.yaml',
        'network-policy.yaml',
        'backup-cronjob.yaml',
        'ingress.yaml'
    )) {
        kubectl --context $KubeContext apply -f (Render-Manifest $manifest)
    }

    kubectl --context $KubeContext rollout status deployment/banking-reconciliation --namespace $namespace --timeout=10m
    kubectl --context $KubeContext get pods --namespace $namespace --selector app.kubernetes.io/name=banking-reconciliation

    $health = Invoke-RestMethod -Uri "https://$HostName/api/health" -Method Get
    if ($health.Status -ne 'Running') {
        throw 'Public liveness smoke check did not return Running.'
    }
    $readiness = Invoke-RestMethod -Uri "https://$HostName/api/health/ready" -Method Get
    if ($readiness.Status -ne 'Ready') {
        throw 'Public readiness smoke check did not return Ready.'
    }
    $auditHealth = Invoke-RestMethod -Uri "https://$HostName/api/health/audit-retention" -Method Get
    if ($null -eq $auditHealth) {
        throw 'Audit retention health check returned an empty response.'
    }

    $deployedImage = (kubectl --context $KubeContext --namespace $namespace get `
        deployment banking-reconciliation `
        --output=jsonpath='{.spec.template.spec.containers[0].image}').Trim()
    if ($deployedImage -ne $Image) {
        throw 'Rolled-out application image does not match the requested digest.'
    }
    [ordered]@{
        DeployedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Environment = $aspNetCoreEnvironment
        KubernetesContext = $KubeContext
        Namespace = $namespace
        HostName = $HostName
        Version = $safeVersion
        ApplicationImage = $deployedImage
        BackupImage = $BackupImage
        OidcIssuer = $oidcMetadata.issuer
        Health = $health.Status
        Readiness = $readiness.Status
        MigrationJob = "banking-reconciliation-migrate-$safeVersion"
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $resolvedEvidenceDirectory 'deployment-acceptance.json') `
        -Encoding UTF8
    Write-Host "Deployment $Version completed in namespace $namespace."
}
finally {
    $resolvedRenderRoot = [System.IO.Path]::GetFullPath($renderRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedRenderRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRenderRoot)) {
        Remove-Item -LiteralPath $resolvedRenderRoot -Recurse -Force
    }
}
