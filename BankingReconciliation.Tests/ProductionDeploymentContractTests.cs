namespace BankingReconciliation.Tests;

public class ProductionDeploymentContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task Container_RunsAsNonRootFromPinnedMajorRuntime()
    {
        var dockerfile = await ReadAsync("Dockerfile");

        Assert.Contains("mcr.microsoft.com/dotnet/aspnet:8.0-alpine-extra", dockerfile);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            dockerfile,
            "@sha256:[a-f0-9]{64}").Count);
        Assert.Contains("USER 1654", dockerfile);
        Assert.Contains("DOTNET_EnableDiagnostics=0", dockerfile);
        Assert.DoesNotContain("latest", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deployment_UsesExternalSecretsAndHardenedContainer()
    {
        var deployment = await ReadAsync("deploy", "kubernetes", "deployment.yaml");

        Assert.Contains("secretKeyRef:", deployment);
        Assert.Contains("name: banking-reconciliation-secrets", deployment);
        Assert.Contains("readOnlyRootFilesystem: true", deployment);
        Assert.Contains("allowPrivilegeEscalation: false", deployment);
        Assert.Contains("drop: [\"ALL\"]", deployment);
        Assert.Contains("runAsNonRoot: true", deployment);
        Assert.Contains("seccompProfile:", deployment);
        Assert.Contains("readinessProbe:", deployment);
        Assert.Contains("livenessProbe:", deployment);
        Assert.Contains("maxUnavailable: 0", deployment);
        Assert.DoesNotContain("Password=", deployment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_IsAnExplicitOneShotJob()
    {
        var migrationJob = await ReadAsync("deploy", "kubernetes", "migration-job.yaml");
        var config = await ReadAsync("deploy", "kubernetes", "configmap.yaml");

        Assert.Contains("kind: Job", migrationJob);
        Assert.Contains("args: [\"--migrate-only\"]", migrationJob);
        Assert.Contains("key: reconciliation-migration-database", migrationJob);
        Assert.Contains("backoffLimit: 1", migrationJob);
        Assert.Contains("ApplyDatabaseMigrationsOnStartup: \"false\"", config);
        Assert.DoesNotContain("kind: Secret", migrationJob);
        Assert.DoesNotContain("Password=", config, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Secrets_AreSynchronizedExternallyAndDatabaseIdentitiesAreSeparated()
    {
        var externalSecrets = await ReadAsync("deploy", "kubernetes", "external-secrets.yaml");
        var deployment = await ReadAsync("deploy", "kubernetes", "deployment.yaml");
        var migration = await ReadAsync("deploy", "kubernetes", "migration-job.yaml");

        Assert.Contains("apiVersion: external-secrets.io/v1", externalSecrets);
        Assert.Contains("kind: ClusterSecretStore", externalSecrets);
        Assert.Contains("creationPolicy: Owner", externalSecrets);
        Assert.Contains("deletionPolicy: Retain", externalSecrets);
        Assert.Contains("property: reconciliation-database", externalSecrets);
        Assert.Contains("property: reconciliation-migration-database", externalSecrets);
        Assert.Contains("key: reconciliation-database", deployment);
        Assert.DoesNotContain("reconciliation-migration-database", deployment);
        Assert.Contains("key: reconciliation-migration-database", migration);
        Assert.DoesNotContain("kind: Secret\n", externalSecrets.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task AvailabilityContract_HasDisruptionBudgetAndAutoscaling()
    {
        var availability = await ReadAsync("deploy", "kubernetes", "availability.yaml");

        Assert.Contains("kind: PodDisruptionBudget", availability);
        Assert.Contains("minAvailable: 1", availability);
        Assert.Contains("kind: HorizontalPodAutoscaler", availability);
        Assert.Contains("minReplicas: 2", availability);
        Assert.Contains("maxReplicas: 8", availability);
    }

    [Fact]
    public async Task BackupContract_EncryptsOffsiteDumpAndPublishesChecksum()
    {
        var backupScript = await ReadAsync("deploy", "postgres", "backup.sh");
        var backupImage = await ReadAsync("deploy", "postgres", "Dockerfile");
        var cronJob = await ReadAsync("deploy", "kubernetes", "backup-cronjob.yaml");

        Assert.Contains("postgres:16-alpine", backupImage);
        Assert.Matches("postgres:16-alpine@sha256:[a-f0-9]{64}", backupImage);
        Assert.Contains("pg_dump", backupScript);
        Assert.Contains("pg_restore --list", backupScript);
        Assert.Contains("sha256sum", backupScript);
        Assert.Contains("--sse aws:kms", backupScript);
        Assert.Contains("--sse-kms-key-id", backupScript);
        Assert.Contains("schedule: \"0 */6 * * *\"", cronJob);
        Assert.Contains("concurrencyPolicy: Forbid", cronJob);
        Assert.Contains("value: verify-full", cronJob);
        Assert.Contains("app.kubernetes.io/component: database-backup", cronJob);
        Assert.Contains("secretKeyRef:", cronJob);
        Assert.DoesNotContain("PGPASSWORD", backupScript);
        Assert.DoesNotContain("kind: Secret", cronJob);
    }

    [Fact]
    public async Task RestoreVerification_IsFailClosedAndUsesIsolatedDatabase()
    {
        var restoreScript = await ReadAsync("deploy", "postgres", "restore-verify.sh");
        var restoreJob = await ReadAsync("deploy", "kubernetes", "restore-verify-job.yaml");

        Assert.Contains("set -euo pipefail", restoreScript);
        Assert.Contains("_restore_verify", restoreScript);
        Assert.Contains("sha256sum --check", restoreScript);
        Assert.Contains("--exit-on-error", restoreScript);
        Assert.Contains("ReconciliationBatches", restoreScript);
        Assert.Contains("backoffLimit: 0", restoreJob);
        Assert.Contains("name: banking-reconciliation-restore-verify", restoreJob);
        Assert.Contains("app.kubernetes.io/component: database-restore-verification", restoreJob);
        Assert.Contains("value: verify-full", restoreJob);
        Assert.Contains("BACKUP_OBJECT_URI", restoreJob);
        Assert.DoesNotContain("dropdb \"$PGDATABASE\"", restoreScript);
    }

    [Fact]
    public async Task DeploymentScript_EnforcesContextSecretsMigrationAndRolloutOrder()
    {
        var deploymentScript = await ReadAsync("deploy", "kubernetes", "deploy.ps1");

        Assert.Contains("current-context", deploymentScript);
        Assert.Contains("does not match required context", deploymentScript);
        Assert.Contains("Assert-SecretKey 'banking-reconciliation-secrets' 'reconciliation-database'", deploymentScript);
        Assert.Contains("Assert-SecretKey 'banking-reconciliation-secrets' 'reconciliation-migration-database'", deploymentScript);
        Assert.Contains("externalsecret/$externalSecret", deploymentScript);
        Assert.Contains("migration-job.yaml", deploymentScript);
        Assert.Contains("--for=condition=complete", deploymentScript);
        Assert.Contains("rollout status", deploymentScript);
        Assert.Contains("unresolved deployment placeholder", deploymentScript);
        Assert.Contains("GetTempPath", deploymentScript);
        Assert.Contains("PSNativeCommandUseErrorActionPreference", deploymentScript);
        Assert.Contains("ApproveProductionDeployment", deploymentScript);
        Assert.Contains("__ASPNETCORE_ENVIRONMENT__", deploymentScript);
        Assert.Contains("__AUTHENTICATION_AUDIENCE__", deploymentScript);
        Assert.Contains("https://$HostName/api/health/ready", deploymentScript);
        Assert.Contains("does not match the requested digest", deploymentScript);
        Assert.Contains("deployment-acceptance.json", deploymentScript);
    }

    [Fact]
    public async Task DeploymentPreflight_IsReadOnlyAndFailsClosedOnMissingDependencies()
    {
        var preflightScript = await ReadAsync("deploy", "kubernetes", "preflight.ps1");

        Assert.Contains("PSNativeCommandUseErrorActionPreference", preflightScript);
        Assert.Contains("No current Kubernetes context is configured", preflightScript);
        Assert.Contains("Current Kubernetes context", preflightScript);
        Assert.Contains("kubectl --context $KubeContext version", preflightScript);
        Assert.Contains("docker info", preflightScript);
        Assert.Contains("externalsecrets.external-secrets.io", preflightScript);
        Assert.Contains("clustersecretstores.external-secrets.io", preflightScript);
        Assert.Contains("ClusterSecretStore '$ExternalSecretStore' is not Ready", preflightScript);
        Assert.Contains("get ingressclass nginx", preflightScript);
        Assert.Contains("auth', 'can-i'", preflightScript);
        Assert.Contains(".well-known/openid-configuration", preflightScript);
        Assert.Contains("OIDC discovery issuer does not match", preflightScript);
        Assert.DoesNotContain("kubectl --context $KubeContext apply", preflightScript);
        Assert.DoesNotContain("kubectl --context $KubeContext delete", preflightScript);
    }

    [Fact]
    public async Task LocalKubernetesProfile_IsIsolatedFromProductionDeployment()
    {
        var manifest = await ReadAsync("deploy", "kubernetes", "local.yaml");
        var script = await ReadAsync("deploy", "kubernetes", "deploy-local.ps1");

        Assert.Contains("namespace: banking-reconciliation-local", manifest);
        Assert.Contains("value: Development", manifest);
        Assert.Contains("value: __LOCAL_SIGNING_KEY__", manifest);
        Assert.Contains("imagePullPolicy: Never", manifest);
        Assert.Contains("readOnlyRootFilesystem: true", manifest);
        Assert.Contains("runAsNonRoot: true", manifest);
        Assert.Contains("kind: PersistentVolumeClaim", manifest);
        Assert.Contains("claimName: banking-reconciliation-local-data", manifest);
        Assert.Contains("emptyDir: {}", manifest);
        Assert.DoesNotContain("ExternalSecret", manifest);
        Assert.DoesNotContain("kind: Ingress", manifest);
        Assert.Contains("docker-desktop", script);
        Assert.Contains("does not match required context", script);
        Assert.Contains("docker build --tag $Image", script);
        Assert.Contains("RandomNumberGenerator", script);
        Assert.Contains("ctr --namespace k8s.io images import", script);
        Assert.Contains("Show system containers (advanced)", script);
        Assert.Contains("rollout restart", script);
        Assert.Contains("rollout status", script);
        Assert.Contains("TcpListener", script);
        Assert.Contains("127.0.0.1:$smokePort/api/health", script);
        Assert.Contains("GetTempPath", script);
    }

    [Fact]
    public async Task RollbackContract_UndoesOnlyApplicationRevision()
    {
        var rollbackScript = await ReadAsync("deploy", "kubernetes", "rollback.ps1");

        Assert.Contains("rollout', 'undo'", rollbackScript);
        Assert.Contains("--to-revision", rollbackScript);
        Assert.Contains("ApproveProductionRollback", rollbackScript);
        Assert.Contains("explicit positive -ToRevision", rollbackScript);
        Assert.Contains("PSNativeCommandUseErrorActionPreference", rollbackScript);
        Assert.Contains("explicitly expected digest", rollbackScript);
        Assert.Contains("https://$HostName/api/health/ready", rollbackScript);
        Assert.Contains("rollback-acceptance.json", rollbackScript);
        Assert.Contains("Database migrations are intentionally not reversed", rollbackScript);
    }

    [Fact]
    public async Task EdgeContract_RequiresTlsAndDefaultDenyNetworkPolicy()
    {
        var ingress = await ReadAsync("deploy", "kubernetes", "ingress.yaml");
        var networkPolicy = await ReadAsync("deploy", "kubernetes", "network-policy.yaml");

        Assert.Contains("force-ssl-redirect: \"true\"", ingress);
        Assert.Contains("secretName: banking-reconciliation-tls", ingress);
        Assert.Contains("banking-reconciliation-default-deny", networkPolicy);
        Assert.Contains("policyTypes: [\"Ingress\", \"Egress\"]", networkPolicy);
        Assert.Contains("port: 5432", networkPolicy);
        Assert.Contains("port: 4317", networkPolicy);
    }

    [Fact]
    public async Task StagingAcceptance_RequiresExactDigestAndStoresSecurityAndLoadEvidence()
    {
        var script = await ReadAsync("deploy", "kubernetes", "verify-staging.ps1");

        Assert.Contains("PSNativeCommandUseErrorActionPreference", script);
        Assert.Contains("Deployed image digest does not match", script);
        Assert.Contains("ASPNETCORE_ENVIRONMENT '$environmentName'", script);
        Assert.Contains("k6-summary.json", script);
        Assert.Contains("zap-report.json", script);
        Assert.Contains("zap-report.html", script);
        Assert.Contains("acceptance.json", script);
        Assert.Contains("https://$HostName", script);
        Assert.DoesNotContain("--insecure", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreAcceptance_UsesSelectedDumpAndStoresJobEvidence()
    {
        var script = await ReadAsync("deploy", "kubernetes", "verify-restore.ps1");

        Assert.Contains("PSNativeCommandUseErrorActionPreference", script);
        Assert.Contains("^s3://.+\\.dump$", script);
        Assert.Contains("externalsecret/banking-reconciliation-restore-verify", script);
        Assert.Contains("restore-verify-job.yaml", script);
        Assert.Contains("--for=condition=complete", script);
        Assert.Contains("restore.log", script);
        Assert.Contains("restore-job.yaml", script);
        Assert.Contains("restore-acceptance.json", script);
        Assert.Contains("GetTempPath", script);
    }

    private static Task<string> ReadAsync(params string[] parts) =>
        File.ReadAllTextAsync(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "BankingReconciliation.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("Repository root could not be located.");
    }
}
