# Banking Reconciliation

[![CI](https://github.com/Muhametaydn/BankingReconciliation/actions/workflows/ci.yml/badge.svg)](https://github.com/Muhametaydn/BankingReconciliation/actions/workflows/ci.yml)

[English](README.md) | [Türkçe](README.tr.md)

A web application for comparing branch and bank transaction records. It helps
operations teams find missing records and value differences, review results, and
record an approval decision.

## What it does

- Compares CSV, delimited TXT, fixed-width TXT, and configured database sources.
- Detects missing transactions, duplicate keys, quantity differences, amount
  differences, and configurable field differences.
- Validates file columns and values before processing.
- Exports reconciliation differences to Excel.
- Stores reconciliation history, settings, approvals, and audit events in
  PostgreSQL when a connection string is configured.
- Supports Administrator, Operator, and Approver roles.
- Runs longer comparisons in a background worker with retry support.

## Technology

- .NET 8 and ASP.NET Core Minimal APIs
- PostgreSQL and Entity Framework Core
- JWT authentication and role-based authorization
- Docker and Kubernetes manifests
- AWS S3-compatible storage and MinIO test support
- xUnit and GitHub Actions

## Run locally

You need the .NET 8 SDK.

```powershell
git clone https://github.com/Muhametaydn/BankingReconciliation.git
cd BankingReconciliation
dotnet run --project .\BankingReconciliation.Api\BankingReconciliation.Api.csproj
```

Open `http://localhost:5230`.

Create the first local account from **Kayıt ol**. The first account is an
Administrator; later accounts are Operators. An Administrator can assign the
Approver role from the user management section.

Sample input files are in
[`BankingReconciliation.Api/Samples`](BankingReconciliation.Api/Samples).

## Run with Docker

```powershell
docker build -t banking-reconciliation:local .
docker run --rm -p 8080:8080 banking-reconciliation:local
```

Open `http://localhost:8080`.

## Verify

```powershell
dotnet test .\BankingReconciliation.sln --configuration Release
dotnet format .\BankingReconciliation.sln --verify-no-changes --no-restore
```

## Project layout

```text
BankingReconciliation.Api/     API, UI, services, data model, migrations
BankingReconciliation.Tests/   Unit and endpoint tests
deploy/                        Docker, Kubernetes, storage and backup resources
tests/                         Load and security test configuration
```

## Local Kubernetes

For Docker Desktop Kubernetes:

```powershell
.\deploy\kubernetes\deploy-local.ps1
```

The local profile is intended for development only. Deployment, backup, and
rollback scripts are documented in
[deploy/kubernetes/README.md](deploy/kubernetes/README.md).

## Workflow

1. An Operator uploads two sources and starts a comparison.
2. The application validates and reconciles the records.
3. Differences can be reviewed in the UI or exported to Excel.
4. An Approver approves or rejects a completed reconciliation.
5. Management changes and approval decisions are recorded in the audit trail.

Swagger is available locally at `http://localhost:5230/swagger`.
