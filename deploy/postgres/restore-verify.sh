#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?PGHOST is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGPASSFILE:?PGPASSFILE is required}"
: "${BACKUP_OBJECT_URI:?BACKUP_OBJECT_URI is required}"
: "${VERIFY_DATABASE:?VERIFY_DATABASE is required}"

if [[ ! "$VERIFY_DATABASE" =~ _restore_verify$ ]]; then
  echo "VERIFY_DATABASE must end with _restore_verify." >&2
  exit 2
fi

restore_work_dir="$(mktemp -d)"
trap 'rm -rf -- "$restore_work_dir"' EXIT
backup_file="$restore_work_dir/backup.dump"
checksum_file="$restore_work_dir/backup.dump.sha256"

aws s3 cp "$BACKUP_OBJECT_URI" "$backup_file" --only-show-errors
aws s3 cp "$BACKUP_OBJECT_URI.sha256" "$checksum_file" --only-show-errors
sed -i 's# .*/#  backup.dump#' "$checksum_file"
(
  cd "$restore_work_dir"
  sha256sum --check "$(basename "$checksum_file")"
)
pg_restore --list "$backup_file" > /dev/null

dropdb --if-exists "$VERIFY_DATABASE"
createdb "$VERIFY_DATABASE"
pg_restore \
  --exit-on-error \
  --no-owner \
  --no-acl \
  --dbname="$VERIFY_DATABASE" \
  "$backup_file"

table_count="$(psql --dbname="$VERIFY_DATABASE" --tuples-only --no-align \
  --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public';")"
if [[ "$table_count" -lt 1 ]]; then
  echo "Restore verification failed: no public tables were restored." >&2
  exit 1
fi

psql --dbname="$VERIFY_DATABASE" --set=ON_ERROR_STOP=1 \
  --command='SELECT count(*) FROM "ReconciliationBatches";' > /dev/null
echo "Restore verification completed for $VERIFY_DATABASE with $table_count public tables."
