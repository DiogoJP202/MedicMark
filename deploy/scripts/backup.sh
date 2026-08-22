#!/usr/bin/env bash
set -euo pipefail

database="${DATABASE_PATH:-/var/lib/docker/volumes/checklistplantao-dados/_data/checklistplantao.db}"
backup_dir="${BACKUP_DIR:-/opt/medicmark/backups}"
retention_days="${RETENTION_DAYS:-30}"
timestamp="$(date -u +%Y%m%d-%H%M%S)"
temporary="$backup_dir/.checklistplantao-$timestamp.db.tmp"
destination="$backup_dir/checklistplantao-$timestamp.db"

test -f "$database"
install -d -m 0700 "$backup_dir"

cleanup() {
    rm -f -- "$temporary"
}
trap cleanup EXIT

# O comando .backup do SQLite produz uma cópia consistente mesmo com WAL e servidor ativos.
sqlite3 "$database" ".timeout 10000" ".backup '$temporary'"
chmod 0600 "$temporary"
mv "$temporary" "$destination"
trap - EXIT

integrity="$(sqlite3 "$destination" 'PRAGMA integrity_check;')"
if [ "$integrity" != "ok" ]; then
    rm -f -- "$destination"
    printf 'Backup reprovado pelo integrity_check: %s\n' "$integrity" >&2
    exit 1
fi

find "$backup_dir" -maxdepth 1 -type f -name 'checklistplantao-*.db' \
    -mtime "+$retention_days" -delete

printf 'Backup consistente criado em %s\n' "$destination"
