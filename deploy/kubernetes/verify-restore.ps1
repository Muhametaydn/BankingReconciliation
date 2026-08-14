[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $KubeContext,
    [Parameter(Mandatory)] [ValidateSet('staging', 'production')] [string] $Environment,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidatePattern('@sha256:[a-fA-F0-9]{64}$')] [string] $BackupImage,
    [Parameter(Mandatory)] [ValidatePattern('^s3://.+\.dump$')] [string] $BackupObjectUri,
    [Parameter(Mandatory)] [string] $EvidenceDirectory
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
$currentContext = (kubectl config current-context).Trim()
if ($currentContext -ne $KubeContext) {
    throw "Current Kubernetes context '$currentContext' does not match required context '$KubeContext'."
}

$resolvedEvidenceDirectory = [System.IO.Path]::GetFullPath($EvidenceDirectory)
[System.IO.Directory]::CreateDirectory($resolvedEvidenceDirectory) | Out-Null
$renderRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    "banking-reconciliation-restore-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($renderRoot) | Out-Null
$jobName = "banking-reconciliation-restore-verify-$safeVersion"

try {
    kubectl --context $KubeContext --namespace $namespace wait `
        --for=condition=Ready externalsecret/banking-reconciliation-restore-verify `
        --timeout=2m

    $sourcePath = Join-Path $PSScriptRoot 'restore-verify-job.yaml'
    $renderedPath = Join-Path $renderRoot 'restore-verify-job.yaml'
    $content = Get-Content -LiteralPath $sourcePath -Raw
    $content = $content.Replace('namespace: banking-reconciliation', "namespace: $namespace")
    $content = $content.Replace('__VERSION__', $safeVersion)
    $content = $content.Replace('__BACKUP_IMAGE__', $BackupImage)
    $content = $content.Replace('__BACKUP_OBJECT_URI__', $BackupObjectUri)
    if ($content -match '__[A-Z0-9_]+__') {
        throw 'Restore manifest contains an unresolved deployment placeholder.'
    }
    Set-Content -LiteralPath $renderedPath -Value $content -Encoding UTF8

    kubectl --context $KubeContext --namespace $namespace delete job $jobName --ignore-not-found
    kubectl --context $KubeContext apply -f $renderedPath
    try {
        kubectl --context $KubeContext --namespace $namespace wait `
            --for=condition=complete "job/$jobName" --timeout=60m
    }
    finally {
        try {
            kubectl --context $KubeContext --namespace $namespace logs "job/$jobName" `
                | Set-Content -LiteralPath `
                    (Join-Path $resolvedEvidenceDirectory 'restore.log') -Encoding UTF8
        }
        catch {
            $_ | Out-String | Set-Content -LiteralPath `
                (Join-Path $resolvedEvidenceDirectory 'restore-log-capture-error.txt') `
                -Encoding UTF8
        }
        try {
            kubectl --context $KubeContext --namespace $namespace get "job/$jobName" `
                --output yaml | Set-Content -LiteralPath `
                    (Join-Path $resolvedEvidenceDirectory 'restore-job.yaml') -Encoding UTF8
        }
        catch {
            $_ | Out-String | Set-Content -LiteralPath `
                (Join-Path $resolvedEvidenceDirectory 'restore-job-capture-error.txt') `
                -Encoding UTF8
        }
    }

    [ordered]@{
        VerifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        KubernetesContext = $KubeContext
        Namespace = $namespace
        Version = $safeVersion
        BackupImage = $BackupImage
        BackupObjectUri = $BackupObjectUri
        Result = 'Passed'
        Log = 'restore.log'
        JobManifest = 'restore-job.yaml'
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $resolvedEvidenceDirectory 'restore-acceptance.json') `
        -Encoding UTF8

    Write-Host "Restore verification passed. Evidence: $resolvedEvidenceDirectory"
}
finally {
    $resolvedRenderRoot = [System.IO.Path]::GetFullPath($renderRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedRenderRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRenderRoot)) {
        Remove-Item -LiteralPath $resolvedRenderRoot -Recurse -Force
    }
}
