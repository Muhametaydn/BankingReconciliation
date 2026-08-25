# Production Readiness

This is the release gate for the Banking Reconciliation service. A production
release is approved only when every repository-controlled check is green and
every environment-owned evidence item has been recorded for the exact immutable
image digest being released.

## Repository-controlled controls

| Control | Status | Evidence |
|---|---|---|
| Production configuration fails closed | Complete | `ProductionReadinessTests` |
| HTTPS OIDC authority, audience, signed/lifetime JWT validation | Complete | startup validator and authentication options |
| Explicit host allowlist, trusted proxy CIDRs, HSTS and security headers | Complete | startup validator and endpoint tests |
| Global bounded fixed-window rate limiting | Complete | production options and middleware |
| Runtime connection string removed from committed settings; full PostgreSQL TLS verification required | Complete | settings and validator regression tests |
| External Secrets synchronization; separate runtime/migration identities; no literal Kubernetes Secret | Complete | ExternalSecret manifests and deployment contract tests |
| Digest-pinned, non-root, read-only container and pod security context | Complete | Docker/Kubernetes contract tests |
| Migration runs as a one-shot job before rollout | Complete | `--migrate-only` and deployment script |
| Zero-unavailable rolling deployment, probes, PDB and HPA | Complete | Kubernetes manifests |
| TLS ingress and default-deny network policy | Complete | Kubernetes manifests |
| Six-hour encrypted PostgreSQL dump and SHA-256 sidecar | Complete | backup image/script/CronJob |
| Isolated fail-closed restore verification | Complete | restore script/job |
| Selected-backup restore evidence collection | Complete | `verify-restore.ps1` and contract test |
| Exact-digest application rollback, HTTPS verification and evidence without unsafe schema reversal | Complete | rollback script/runbook |
| NuGet vulnerability gate | Complete | CI and local `dotnet list package --vulnerable` |
| Passive OWASP ZAP baseline | Complete in CI contract | `production-verification` job |
| k6 reconciliation load acceptance | Complete in CI contract | `tests/load/reconciliation.js` |
| Exact-digest staging health/ZAP/k6 evidence collection | Complete | `verify-staging.ps1` and contract test |
| Read-only staging dependency and access preflight | Complete | `preflight.ps1` and contract test |

Latest local application verification (2026-08-20): the Release test suite
completed with `272/272` tests passing. This includes automatic synchronization
of schema columns with comparison settings and the read-only staging preflight
contract. The separate Development-only Docker Desktop Kubernetes profile also
completed its rollout and `/api/health` smoke check successfully; this is local
development evidence, not staging acceptance evidence.

Latest comprehensive repository verification (2026-08-14): Release build completed with zero
warnings and zero errors, and all `271/271` tests passed with the PostgreSQL,
MinIO S3, least-privilege, and immutable Object Lock profiles required. All
PowerShell deployment/acceptance scripts and AWS JSON templates parsed
successfully. The API and test projects reported no known vulnerable NuGet
packages from the configured sources. A local authenticated smoke test passed,
and the corrected k6 gate completed 101 requests with 100% successful checks,
zero HTTP failures, 3.52 ms p95, and 17.45 ms p99 latency. The OWASP ZAP passive
baseline completed with zero FAIL findings.

Both production images were rebuilt from digest-pinned .NET 8.0.30/Alpine 3.24
and PostgreSQL 16.15/Alpine 3.24 bases. Trivy 0.72.0 reported zero HIGH or
CRITICAL vulnerabilities in both images and zero HIGH or CRITICAL
misconfigurations in the Kubernetes and Docker configuration.

Current workstation status updated on 2026-08-20: PowerShell 7.6.4 and a local
Docker Desktop Kubernetes 1.36.1 cluster are available. The local node is Ready,
but External Secrets Operator, an nginx ingress class, a real HTTPS OIDC issuer,
cloud storage/KMS resources, and a production-like PostgreSQL service are not
configured. The local cluster is suitable for development smoke tests, but it
cannot produce real staging, backup restore, rollback, or environment-owned
acceptance evidence by itself.

## Environment-owned acceptance evidence

These items cannot be fabricated or completed inside the repository. The
release owner records links, timestamps, and approvers in the change ticket.

- [ ] OIDC discovery succeeds for the real authority; issuer matches exactly;
  approver and administrator claims are issued to dedicated test users.
- [ ] Runtime and migration database identities are separate and least
  privilege; PostgreSQL requires TLS.
- [ ] External secret synchronization has populated all required keys without
  plaintext values appearing in GitHub Actions, manifests, or logs.
- [ ] Application, backup, and restore workload identities have been reviewed;
  backup identity cannot delete backup objects.
- [ ] Staging migration and zero-downtime rollout complete for the immutable
  application and postgres-ops image digests.
- [ ] Staging `/api/health`, `/api/health/ready`, and
  `/api/health/audit-retention` are healthy.
- [ ] Staging ZAP and k6 gates pass with stored artifacts.
- [ ] A selected encrypted backup restores into an isolated database and the
  measured RPO/RTO meet the approved business targets.
- [ ] Alert delivery reaches the on-call channel and includes a tested
  acknowledgement/escalation path.
- [ ] Production rollback is rehearsed against staging and the previous image
  digest remains available.
- [ ] Business owner, security owner, database owner, and operations owner have
  approved the change.

## Required production inputs

The deployment command needs:

- Kubernetes context and target environment.
- Immutable application and postgres-ops image digests.
- Public host name and real HTTPS OIDC authority.
- Upload and backup bucket names, expected AWS account id, and backup KMS key.
- Pre-synchronized application, backup, restore-verification, and TLS secrets.

See [the Kubernetes runbook](deploy/kubernetes/README.md) for exact commands,
deployment order, backup acceptance, and rollback behavior.

## Release decision

Repository completion means the controls and executable contracts exist and
pass locally/CI. It does not mean a real identity provider, cloud account,
cluster, secret manager, backup bucket, or on-call system has been validated.
Production approval remains closed until every environment-owned checkbox above
has objective evidence for the release digest.

The repository cannot manufacture that evidence: it requires access to the real
OIDC tenant, Kubernetes context, image registry, PostgreSQL service, AWS
bucket/KMS resources, external secret synchronization, and on-call channel.
