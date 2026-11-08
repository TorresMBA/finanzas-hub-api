#!/bin/sh
# Copia de seguridad de la base de PersonalHub con pg_dump (formato custom, se restaura con pg_restore).
# La usan el cron del servidor (una vez al día) y Jenkins, justo antes de migrar prod.
#
#   tools/backup.sh /srv/mercury/apps/prod/finanzas-hub-backup.env [etiqueta]
#
# El .env (no versionado, legible solo por quien hace backups) define:
#   PGHOST, PGPORT (5432), PGUSER, PGPASSWORD, PGDATABASE   cómo llegar a la base
#   PG_NETWORK        red de Docker donde está la base (la misma que usa el API)
#   BACKUP_DIR        carpeta del host donde quedan los .dump
#   BACKUP_KEEP_DAYS  días que se conservan (14)
#   PG_IMAGE          imagen con pg_dump, de la misma versión mayor que el servidor o más nueva (postgres:18-alpine)
#   BACKUP_COPY       opcional: comando que recibe la ruta del .dump y lo copia fuera del servidor
#                     (p. ej. "rclone copy --no-traverse {} remoto:personalhub"); {} se sustituye por la ruta
# Docs: docs/devops/operations.md, sección Copias de seguridad.
set -eu

env_file="${1:?Uso: tools/backup.sh <archivo .env> [etiqueta]}"
label="${2:-daily}"
[ -r "$env_file" ] || { echo "No se puede leer $env_file" >&2; exit 1; }

set -a
. "$env_file"
set +a

: "${PGHOST:?falta PGHOST}" "${PGUSER:?falta PGUSER}" "${PGPASSWORD:?falta PGPASSWORD}" "${PGDATABASE:?falta PGDATABASE}"
: "${PG_NETWORK:?falta PG_NETWORK}" "${BACKUP_DIR:?falta BACKUP_DIR}"
keep_days="${BACKUP_KEEP_DAYS:-14}"
image="${PG_IMAGE:-postgres:18-alpine}"
file="personalhub-$(date -u +%Y%m%dT%H%M%SZ)-$label.dump"

mkdir -p "$BACKUP_DIR"
docker run --rm --network "$PG_NETWORK" \
  -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD -e PGDATABASE \
  -v "$BACKUP_DIR:/backup" "$image" \
  sh -c "pg_dump --format=custom --no-owner --file=/backup/$file.part && mv /backup/$file.part /backup/$file"

# Una copia vacía o cortada no cuenta: pg_restore --list tiene que poder leerla.
docker run --rm -v "$BACKUP_DIR:/backup:ro" "$image" pg_restore --list "/backup/$file" >/dev/null
echo "Copia lista: $BACKUP_DIR/$file ($(du -h "$BACKUP_DIR/$file" | cut -f1))"

if [ -n "${BACKUP_COPY:-}" ]; then
  sh -c "$(echo "$BACKUP_COPY" | sed "s|{}|$BACKUP_DIR/$file|g")"
  echo "Copiada fuera del servidor"
fi

find "$BACKUP_DIR" -name 'personalhub-*.dump' -mtime +"$keep_days" -print -delete
