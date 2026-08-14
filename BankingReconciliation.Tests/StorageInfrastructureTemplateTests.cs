using System.Text.Json;

namespace BankingReconciliation.Tests;

public class StorageInfrastructureTemplateTests
{
    [Fact]
    public void AwsTemplate_EnforcesPrivateEncryptedVersionedPrefixScopedStorage()
    {
        using var document = LoadJson(
            "deploy",
            "aws",
            "s3-reconciliation-storage.template.json");
        var resources = document.RootElement.GetProperty("Resources");
        var bucket = resources
            .GetProperty("ReconciliationBucket")
            .GetProperty("Properties");

        var encryptionAlgorithm = bucket
            .GetProperty("BucketEncryption")
            .GetProperty("ServerSideEncryptionConfiguration")[0]
            .GetProperty("ServerSideEncryptionByDefault")
            .GetProperty("SSEAlgorithm")
            .GetString();
        Assert.Equal("AES256", encryptionAlgorithm);
        Assert.Equal(
            "BucketOwnerEnforced",
            bucket.GetProperty("OwnershipControls")
                .GetProperty("Rules")[0]
                .GetProperty("ObjectOwnership")
                .GetString());
        Assert.Equal(
            "Enabled",
            bucket.GetProperty("VersioningConfiguration")
                .GetProperty("Status")
                .GetString());

        var publicAccess = bucket.GetProperty("PublicAccessBlockConfiguration");
        Assert.True(publicAccess.GetProperty("BlockPublicAcls").GetBoolean());
        Assert.True(publicAccess.GetProperty("BlockPublicPolicy").GetBoolean());
        Assert.True(publicAccess.GetProperty("IgnorePublicAcls").GetBoolean());
        Assert.True(publicAccess.GetProperty("RestrictPublicBuckets").GetBoolean());

        var lifecycleRule = bucket
            .GetProperty("LifecycleConfiguration")
            .GetProperty("Rules")[0];
        Assert.Equal("Enabled", lifecycleRule.GetProperty("Status").GetString());
        Assert.Equal(
            "${ObjectPrefix}/",
            lifecycleRule.GetProperty("Prefix").GetProperty("Fn::Sub").GetString());
        Assert.Equal(
            "CurrentVersionExpirationDays",
            lifecycleRule.GetProperty("ExpirationInDays").GetProperty("Ref").GetString());
        Assert.Equal(
            "NoncurrentVersionExpirationDays",
            lifecycleRule.GetProperty("NoncurrentVersionExpiration")
                .GetProperty("NoncurrentDays")
                .GetProperty("Ref")
                .GetString());
        Assert.Equal(
            1,
            lifecycleRule.GetProperty("AbortIncompleteMultipartUpload")
                .GetProperty("DaysAfterInitiation")
                .GetInt32());

        var statements = resources
            .GetProperty("ReconciliationBucketPolicy")
            .GetProperty("Properties")
            .GetProperty("PolicyDocument")
            .GetProperty("Statement")
            .EnumerateArray()
            .ToDictionary(
                statement => statement.GetProperty("Sid").GetString()!,
                statement => statement);

        var transportDeny = statements["DenyInsecureTransport"];
        Assert.Equal("Deny", transportDeny.GetProperty("Effect").GetString());
        Assert.Equal("s3:*", transportDeny.GetProperty("Action").GetString());
        Assert.Equal(
            "false",
            transportDeny.GetProperty("Condition")
                .GetProperty("Bool")
                .GetProperty("aws:SecureTransport")
                .GetString());

        var prefixList = statements["AllowPrefixList"];
        Assert.Equal(
            ["s3:ListBucket"],
            ReadActions(prefixList));
        Assert.Equal(
            ["ObjectPrefix", "${ObjectPrefix}/*"],
            [
                prefixList.GetProperty("Condition")
                    .GetProperty("StringLike")
                    .GetProperty("s3:prefix")[0]
                    .GetProperty("Ref")
                    .GetString()!,
                prefixList.GetProperty("Condition")
                    .GetProperty("StringLike")
                    .GetProperty("s3:prefix")[1]
                    .GetProperty("Fn::Sub")
                    .GetString()!
            ]);

        var prefixObjects = statements["AllowPrefixObjects"];
        Assert.Equal(
            ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"],
            ReadActions(prefixObjects));
        Assert.Equal(
            "${ReconciliationBucket.Arn}/${ObjectPrefix}/*",
            prefixObjects.GetProperty("Resource").GetProperty("Fn::Sub").GetString());
    }

    [Fact]
    public void MinioPolicy_GrantsOnlyRequiredPrefixScopedDataPlaneActions()
    {
        using var document = LoadJson(
            "deploy",
            "minio",
            "reconciliation-policy.template.json");
        var statements = document.RootElement
            .GetProperty("Statement")
            .EnumerateArray()
            .ToDictionary(
                statement => statement.GetProperty("Sid").GetString()!,
                statement => statement);

        var prefixList = statements["AllowPrefixList"];
        Assert.Equal(["s3:ListBucket"], ReadActions(prefixList));
        Assert.Equal(
            ["arn:aws:s3:::${BUCKET}"],
            ReadStringArray(prefixList.GetProperty("Resource")));
        Assert.Equal(
            ["${PREFIX}", "${PREFIX}/*"],
            ReadStringArray(
                prefixList.GetProperty("Condition")
                    .GetProperty("StringLike")
                    .GetProperty("s3:prefix")));

        var prefixObjects = statements["AllowPrefixObjects"];
        Assert.Equal(
            ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"],
            ReadActions(prefixObjects));
        Assert.Equal(
            ["arn:aws:s3:::${BUCKET}/${PREFIX}/*"],
            ReadStringArray(prefixObjects.GetProperty("Resource")));

        var policyText = document.RootElement.GetRawText();
        Assert.DoesNotContain("\"s3:*\"", policyText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"arn:aws:s3:::${BUCKET}/*\"",
            policyText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AwsAuditArchiveTemplate_RequiresComplianceLockAndForbidsApplicationDelete()
    {
        using var document = LoadJson("deploy", "aws", "audit-archive.template.json");
        var resources = document.RootElement.GetProperty("Resources");
        var bucket = resources.GetProperty("AuditArchiveBucket").GetProperty("Properties");
        Assert.True(bucket.GetProperty("ObjectLockEnabled").GetBoolean());
        Assert.Equal(
            "COMPLIANCE",
            bucket.GetProperty("ObjectLockConfiguration")
                .GetProperty("Rule")
                .GetProperty("DefaultRetention")
                .GetProperty("Mode")
                .GetString());
        Assert.Equal(
            "Enabled",
            bucket.GetProperty("VersioningConfiguration").GetProperty("Status").GetString());

        var statements = resources
            .GetProperty("AuditArchiveBucketPolicy")
            .GetProperty("Properties")
            .GetProperty("PolicyDocument")
            .GetProperty("Statement")
            .EnumerateArray()
            .ToDictionary(statement => statement.GetProperty("Sid").GetString()!);
        Assert.Equal(
            ["s3:GetObject", "s3:GetObjectRetention", "s3:PutObject", "s3:PutObjectRetention"],
            ReadActions(statements["AllowApplicationArchiveWrites"]));
        Assert.Equal(
            "COMPLIANCE",
            statements["DenyWritesWithoutComplianceMode"]
                .GetProperty("Condition")
                .GetProperty("StringNotEquals")
                .GetProperty("s3:object-lock-mode")
                .GetString());
        var policy = resources
            .GetProperty("AuditArchiveBucketPolicy")
            .GetRawText();
        Assert.DoesNotContain("s3:DeleteObject", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:BypassGovernanceRetention", policy, StringComparison.Ordinal);

        var signingKey = resources
            .GetProperty("AuditSigningKey")
            .GetProperty("Properties");
        Assert.Equal("RSA_3072", signingKey.GetProperty("KeySpec").GetString());
        Assert.Equal("SIGN_VERIFY", signingKey.GetProperty("KeyUsage").GetString());
        var keyStatements = signingKey
            .GetProperty("KeyPolicy")
            .GetProperty("Statement")
            .EnumerateArray()
            .ToDictionary(statement => statement.GetProperty("Sid").GetString()!);
        Assert.Equal(
            ["kms:Sign", "kms:Verify"],
            ReadActions(keyStatements["AllowApplicationSignAndVerify"]));
        var applicationStatement = keyStatements["AllowApplicationSignAndVerify"].GetRawText();
        Assert.DoesNotContain("kms:Decrypt", applicationStatement, StringComparison.Ordinal);
        Assert.DoesNotContain("kms:ScheduleKeyDeletion", applicationStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void MinioAuditArchivePolicy_GrantsRetentionWithoutDeleteOrBypass()
    {
        using var document = LoadJson(
            "deploy",
            "minio",
            "audit-archive-policy.template.json");
        var statement = document.RootElement.GetProperty("Statement")[0];

        Assert.Equal(
            ["s3:GetObject", "s3:GetObjectRetention", "s3:PutObject", "s3:PutObjectRetention"],
            ReadActions(statement));
        var policy = document.RootElement.GetRawText();
        Assert.DoesNotContain("s3:DeleteObject", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:BypassGovernanceRetention", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void GithubAwsIntegrationRole_UsesRepoScopedOidcAndNoDeletePermissions()
    {
        using var document = LoadJson(
            "deploy",
            "aws",
            "github-worm-integration-role.template.json");
        var role = document.RootElement
            .GetProperty("Resources")
            .GetProperty("GitHubWormIntegrationRole")
            .GetProperty("Properties");
        var trust = role
            .GetProperty("AssumeRolePolicyDocument")
            .GetProperty("Statement")[0];
        Assert.Equal("sts:AssumeRoleWithWebIdentity", trust.GetProperty("Action").GetString());
        Assert.Equal(
            "sts.amazonaws.com",
            trust.GetProperty("Condition")
                .GetProperty("StringEquals")
                .GetProperty("token.actions.githubusercontent.com:aud")
                .GetString());
        Assert.Equal(
            "repo:${GitHubRepository}:*",
            trust.GetProperty("Condition")
                .GetProperty("StringLike")
                .GetProperty("token.actions.githubusercontent.com:sub")
                .GetProperty("Fn::Sub")
                .GetString());

        var statements = role.GetProperty("Policies")[0]
            .GetProperty("PolicyDocument")
            .GetProperty("Statement")
            .EnumerateArray()
            .ToDictionary(statement => statement.GetProperty("Sid").GetString()!);
        Assert.Equal(
            ["kms:Sign", "kms:Verify"],
            ReadActions(statements["SignAndVerifyAuditPayloads"]));
        var policy = role.GetProperty("Policies")[0].GetRawText();
        Assert.DoesNotContain("s3:DeleteObject", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("kms:Decrypt", policy, StringComparison.Ordinal);
    }

    private static JsonDocument LoadJson(params string[] relativePath)
    {
        var root = FindRepositoryRoot();
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine([root, .. relativePath])));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
            !File.Exists(Path.Combine(current.FullName, "BankingReconciliation.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new DirectoryNotFoundException("Solution root could not be found.");
    }

    private static string[] ReadActions(JsonElement statement)
    {
        var action = statement.GetProperty("Action");
        return action.ValueKind == JsonValueKind.Array
            ? ReadStringArray(action)
            : [action.GetString()!];
    }

    private static string[] ReadStringArray(JsonElement value) =>
        value.EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
}
