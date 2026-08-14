#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?PGHOST is required}"
: "${PGDATABASE:?PGDATABASE is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGPASSFILE:?PGPASSFILE is required}"
: "${BACKUP_S3_URI:?BACKUP_S3_URI is required}"
: "${BACKUP_KMS_KEY_ID:?BACKUP_KMS_KEY_ID is required}"

backup_work_dir="$(mktemp -d)"
trap 'rm -rf -- "$backup_work_dir"' EXIT
backup_id="$(date -u +%Y%m%dT%H%M%SZ)-${HOSTNAME:-job}"
backup_file="$backup_work_dir/$backup_id.dump"
checksum_file="$backup_file.sha256"
destination="${BACKUP_S3_URI%/}/$backup_id.dump"

pg_dump \
  --format=custom \
  --compress=9 \
  --no-owner \
  --no-acl \
  --file="$backup_file"
pg_restore --list "$backup_file" > /dev/null
sha256sum "$backup_file" > "$checksum_file"

aws s3 cp "$backup_file" "$destination" \
  --only-show-errors \
  --sse aws:kms \
  --sse-kms-key-id "$BACKUP_KMS_KEY_ID"
aws s3 cp "$checksum_file" "$destination.sha256" \
  --only-show-errors \
  --sse aws:kms \
  --sse-kms-key-id "$BACKUP_KMS_KEY_ID"

echo "Backup completed: $destination"
