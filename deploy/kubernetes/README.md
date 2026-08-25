# Production deployment runbook

These manifests deploy the API as a non-root, read-only, rolling Kubernetes
workload. They contain references only; secret values must be synchronized by
the platform secret manager before deployment.

The operator scripts require PowerShell 7.3 or later. This is intentional: native
`kubectl` failures must terminate the operation instead of allowing a partially
failed deployment to continue.

## Local Docker Desktop smoke profile

`deploy-local.ps1` is a development-only profile for the `docker-desktop`
context. It builds the local image, deploys one isolated Development pod with a
local persistent volume but without External Secrets or ingress, verifies
`/api/health`, and then closes its temporary port-forward. It never substitutes
for staging or production acceptance.

```powershell
./deploy/kubernetes/deploy-local.ps1
```

If `banking-reconciliation:local-release` is already built, use `-SkipBuild`.
For a kind-based Docker Desktop cluster, enable **Kubernetes > Edit cluster >
Show system containers (advanced)** so the script can load the local image into
the node's containerd image store.
To keep the UI reachable after deployment, run:

```powershell
kubectl --context docker-desktop --namespace banking-reconciliation-local port-forward `
  service/banking-reconciliation-local 18080:8080
```

Then open `http://127.0.0.1:18080`. Local users and queued upload files are kept
in the `banking-reconciliation-local-data` persistent volume claim, so they
survive pod restarts. Deleting that claim or resetting the Docker Desktop
Kubernetes cluster removes them. Reconciliation history remains in memory in
this local profile and is reset with the pod.

Run `preflight.ps1` before deployment. It is read-only and verifies the exact
Kubernetes context, cluster access, Docker daemon, External Secrets APIs and
store readiness, nginx ingress class, minimum deployment permissions, and OIDC
discovery. Local AWS CLI and k6 installations are not required; staging k6 and
ZAP checks run from digest-pinned Docker images.

## Required external prerequisites

- Separate `staging` and `production` clusters or contexts.
- An OIDC authority and registered API audience.
- PostgreSQL with TLS, a migration-capable deployment identity, and a separate
  least-privilege runtime identity. Every application connection string must
  use `SSL Mode=VerifyFull`; backup and restore
  jobs use `PGSSLMODE=verify-full`.
- S3 upload and backup buckets with encryption, versioning, lifecycle rules,
  blocked public access, and workload identity permissions.
- An OTLP collector and an nginx ingress controller.
- External Secrets Operator and an environment-scoped `ClusterSecretStore`.
- TLS secret `banking-reconciliation-tls` managed by the platform certificate
  controller.
- Secrets `banking-reconciliation-secrets` and
  `banking-reconciliation-backup-secrets`, populated from the external secret
  manager. Never create them from a checked-in literal manifest.

The deployment applies `external-secrets.yaml`, waits for all three
`ExternalSecret` resources to become Ready, and only then verifies the resulting
Kubernetes Secret keys. Required application secret properties:

- `reconciliation-database`
- `reconciliation-migration-database` (a separate migration-capable identity)
- optional `branch-source-database`
- optional `bank-source-database`

Required backup secret keys:

- `host`
- `username`
- `pgpass` in PostgreSQL password-file format and mode `0400`

## Deployment order

1. Build, scan, sign, and push immutable application and postgres-ops images.
2. Synchronize the required secrets and TLS certificate.
3. Run the read-only `preflight.ps1` check against the exact staging context.
4. Run `deploy.ps1` against staging. The script refuses a context mismatch,
   verifies OIDC discovery and required secrets, applies the environment-specific
   ConfigMap, runs the one-shot migration job, then performs a zero-unavailable
   rolling deployment and HTTPS health checks through the public ingress.
5. Run `verify-staging.ps1` against the exact staging image digest. It requires
   valid public TLS and stores health responses, the deployed manifest, k6
   summary, and ZAP JSON/HTML reports in the supplied evidence directory.
6. Record approval and run the same immutable image digests against production.
7. Confirm `/api/health`, `/api/health/ready`, and
   `/api/health/audit-retention` before closing the change.

Preflight example:

```powershell
./deploy/kubernetes/preflight.ps1 `
  -Environment staging `
  -KubeContext company-staging `
  -AuthenticationAuthority https://identity.example.com `
  -ExternalSecretStore company-aws-secrets-manager
```

Example:

```powershell
./deploy/kubernetes/deploy.ps1 `
  -Environment staging `
  -KubeContext company-staging `
  -Version 2026.08.10.1 `
  -Image registry.example.com/banking-reconciliation@sha256:REPLACE `
  -BackupImage registry.example.com/banking-reconciliation-postgres-ops@sha256:REPLACE `
  -HostName reconciliation-staging.example.com `
  -AuthenticationAuthority https://identity.example.com `
  -AuthenticationAudience banking-reconciliation-api `
  -UploadBucket company-reconciliation-staging `
  -ExpectedBucketOwner 123456789012 `
  -AwsRegion eu-central-1 `
  -BackupBucket company-reconciliation-backup-staging `
  -BackupKmsKeyId alias/reconciliation-backup-staging `
  -OtlpEndpoint http://otel-collector.observability.svc.cluster.local:4317 `
  -KnownProxyNetwork 10.20.0.0/16 `
  -ExternalSecretStore company-aws-secrets-manager `
  -ApplicationSecretId banking-reconciliation/staging/application `
  -BackupSecretId banking-reconciliation/staging/backup `
  -RestoreSecretId banking-reconciliation/staging/restore-verify `
  -EvidenceDirectory C:\release-evidence\banking-reconciliation\2026.08.10.1\deployment
```

For production, use the production context and add the explicit
`-ApproveProductionDeployment` switch. This prevents an accidental production
deployment when a staging command is copied.

Staging acceptance example:

```powershell
./deploy/kubernetes/verify-staging.ps1 `
  -KubeContext company-staging `
  -HostName reconciliation-staging.example.com `
  -Version 2026.08.10.1 `
  -ExpectedImage registry.example.com/banking-reconciliation@sha256:REPLACE `
  -EvidenceDirectory C:\release-evidence\banking-reconciliation\2026.08.10.1
```

## Backup and restore acceptance

The CronJob creates a compressed custom-format dump every six hours, validates
its catalog, calculates SHA-256, and uploads both files with KMS encryption.
The backup role should have write-only access to its prefix and no delete
permission. S3 lifecycle/versioning/Object Lock are infrastructure controls.

At least monthly, run `verify-restore.ps1` with a selected `.dump` S3 object,
the immutable postgres-ops image digest, and an evidence directory. It renders
and runs `restore-verify-job.yaml` against an isolated PostgreSQL instance and
stores the completed Job manifest plus logs. The restore script
refuses any target database whose name does not end in `_restore_verify`, checks
the digest, restores with `--exit-on-error`, and queries a required table.
Record duration and evidence. Initial targets are RPO <= 6 hours and verified
RTO <= 2 hours; the service owner must approve tighter business requirements.
The restore job has a separate service account so its read/restore permissions
do not have to be granted to the write-only backup identity.

```powershell
./deploy/kubernetes/verify-restore.ps1 `
  -Environment staging `
  -KubeContext company-staging `
  -Version 2026.08.10.1 `
  -BackupImage registry.example.com/banking-reconciliation-postgres-ops@sha256:REPLACE `
  -BackupObjectUri s3://company-reconciliation-backup-staging/banking-reconciliation/postgres/SELECTED.dump `
  -EvidenceDirectory C:\release-evidence\banking-reconciliation\2026.08.10.1\restore
```

## Rollback

Use `rollback.ps1` to restore a previous Deployment revision. Production
requires both an explicit positive `-ToRevision` and the
`-ApproveProductionRollback` switch. The command also requires the exact
expected image digest, verifies public HTTPS readiness, and writes
`rollback-acceptance.json` to the supplied evidence directory. Database
migrations are never automatically reversed. Every production migration must
remain backward compatible with the previous application revision; destructive
schema cleanup requires a later, separately approved deployment after the
rollback window closes.

```powershell
./deploy/kubernetes/rollback.ps1 `
  -Environment staging `
  -KubeContext company-staging `
  -HostName reconciliation-staging.example.com `
  -ToRevision 3 `
  -ExpectedImage registry.example.com/banking-reconciliation@sha256:PREVIOUS `
  -EvidenceDirectory C:\release-evidence\banking-reconciliation\rollback-rehearsal
```
