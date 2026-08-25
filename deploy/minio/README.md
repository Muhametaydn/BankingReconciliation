# MinIO deployment

Create the bucket separately, then replace `${BUCKET}` and `${PREFIX}` in
`reconciliation-policy.template.json`. The prefix must match
`ReconciliationUpload:S3Prefix` and must not have a leading or trailing slash.
Apply and attach the rendered policy with an administrative `mc` alias:

```text
mc admin policy create ALIAS banking-reconciliation reconciliation-policy.json
mc admin policy attach ALIAS banking-reconciliation --user APP_USER
```

The application user receives only prefix-scoped `List`, `Get`, `Put`, and
`Delete`; it does not receive lifecycle, encryption, policy-management, or
bucket-management permissions.

Enable versioning and add lifecycle cleanup as an administrator:

```text
mc version enable ALIAS/BUCKET
mc ilm rule add --prefix "PREFIX/" --expire-days 30 --noncurrent-expire-days 7 ALIAS/BUCKET
```

The 30-day value is a conservative safety net. MinIO lifecycle expiry does not
query PostgreSQL, so it must be longer than the longest planned outage and job
recovery window.

MinIO SSE-S3 requires a configured external key-management service. After the
tenant's KMS integration has been verified, an administrator can enable
automatic bucket encryption:

```text
mc encrypt set sse-s3 ALIAS/BUCKET
```

Use `S3ServerSideEncryption=BucketDefault` when automatic bucket encryption is
enabled. Use `AES256` only when the MinIO tenant accepts per-request SSE-S3.
Leave `S3ExpectedBucketOwner` empty for MinIO unless the deployed compatible
service explicitly implements that AWS header.

## Immutable audit archive

Use a separate bucket created with object locking enabled; do not reuse the temporary upload bucket:

```text
mc mb --with-lock ALIAS/AUDIT_BUCKET
mc retention set --default COMPLIANCE 3650d ALIAS/AUDIT_BUCKET
```

Render `audit-archive-policy.template.json`, then create and attach it to a dedicated application user. The policy grants only prefix-scoped get, put, and retention headers. It intentionally omits delete, retention bypass, lifecycle, policy, and bucket administration permissions. Confirm that the deployed MinIO release supports S3 Object Lock `COMPLIANCE` mode before enabling the application setting.
