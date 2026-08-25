# AWS S3 deployment

Two separate storage contracts are provided:

- `s3-reconciliation-storage.template.json` is temporary upload storage and permits cleanup.
- `audit-archive.template.json` is a dedicated audit bucket created with Object Lock, versioning, and default `COMPLIANCE` retention. Its application role cannot delete objects or bypass governance retention.

The audit template also creates a retained RSA-3072 KMS key with `SIGN_VERIFY` usage. The application role receives only `kms:Sign` and `kms:Verify`; account administrators retain key administration. Configure the application with `SigningAlgorithm=AwsKmsRsaPssSha256`, the `SigningKeyArn` or `SigningKeyAlias` output, and the deployment region.

For GitHub Actions integration, deploy `github-worm-integration-role.template.json` using the existing GitHub OIDC provider ARN, the repository in `owner/name` form, the dedicated test bucket ARN/prefix, and the signing-key ARN. Store only the resulting role ARN, bucket name, region, 12-digit expected owner, and KMS key ARN/alias as repository secrets. The role trust is repository-scoped and its session policy has no static credentials, S3 delete, KMS decrypt, or key administration permissions.

Do not reuse the temporary upload bucket for immutable audit archives. Object Lock must be enabled when the archive bucket is created. Keep `ArchiveExpirationDays` greater than `ComplianceRetentionDays`.

`s3-reconciliation-storage.template.json` creates a private, versioned S3
bucket with:

- default SSE-S3 (`AES256`) encryption;
- public access blocked and bucket-owner-enforced object ownership;
- TLS-only access;
- an application policy limited to the configured object prefix and the
  exact `List`, `Get`, `Put`, and `Delete` operations used by the service;
- current, noncurrent, and incomplete-upload lifecycle cleanup.

Deploy the stack with the workload IAM role ARN, a globally unique bucket
name, and the application prefix. The role is referenced by the bucket policy;
do not provide long-lived access keys to the application.

Configure the application with the stack outputs:

```json
{
  "ReconciliationUpload": {
    "TemporaryStorageMode": "S3Compatible",
    "S3BucketName": "<BucketName output>",
    "S3Prefix": "<ObjectPrefix output>",
    "S3Region": "<deployment region>",
    "S3ServiceUrl": "",
    "S3ForcePathStyle": false,
    "S3ServerSideEncryption": "BucketDefault",
    "S3KmsKeyId": "",
    "S3ExpectedBucketOwner": "<BucketOwnerAccountId output>"
  }
}
```

The default 30-day current-version expiry is deliberately longer than the
application's default 24-hour orphan-retention window. Native lifecycle expiry
does not query PostgreSQL and therefore cannot protect an active or
retry-waiting job. Set it above the longest planned outage and recovery window.

To require a customer-managed KMS key, change the bucket encryption resource
to `aws:kms`, grant the workload role the corresponding KMS data-plane
permissions, and set `S3ServerSideEncryption` to `AwsKms` with
`S3KmsKeyId`. Keep `BucketDefault` when the bucket policy and default encryption
own that decision centrally.
