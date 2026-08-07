#!/usr/bin/env bash
###############################################################################
# RookHub — Datenbank-Backup (rookhub + chessresults)
#
# Legt je Datenbank einen gzip-komprimierten mariadb-dump ab und raeumt alte
# Dumps nach RETENTION_DAYS weg. Laeuft auf dem Deploy-Host gegen den laufenden
# MariaDB-Container (docker exec) — es muss also kein Client installiert sein.
#
#   ./scripts/backup-db.sh                       # Standardpfade (siehe unten)
#   BACKUP_DIR=/data/backups RETENTION_DAYS=30 ./scripts/backup-db.sh
#
# Konfiguration (alles per ENV ueberschreibbar):
#   ENV_FILE        .env des Stacks, aus der MARIADB_ROOT_PASSWORD gelesen wird
#                   (Default /opt/stacks/rookhub-schach/.env). Alternativ
#                   MARIADB_ROOT_PASSWORD direkt setzen.
#   DB_CONTAINER    Name des MariaDB-Containers (Default rookhub-mariadb)
#   DATABASES       Leerzeichenliste (Default "rookhub chessresults")
#   BACKUP_DIR      Zielverzeichnis (Default /var/backups/rookhub)
#   RETENTION_DAYS  Aufbewahrung in Tagen (Default 14)
#
# ---------------------------------------------------------------------------
# WIEDERHERSTELLEN (Restore)
# ---------------------------------------------------------------------------
# 1. Schreibende Dienste stoppen, damit nichts gegen die halb eingespielte DB
#    laeuft (die API migriert beim Start und wuerde dazwischenfunken):
#       docker stop rookhub-api rookhub-crawler
#
# 2. Dump auswaehlen und einspielen (Beispiel rookhub):
#       gunzip -c /var/backups/rookhub/rookhub-20260807-030000.sql.gz \
#         | docker exec -i -e MYSQL_PWD="$MARIADB_ROOT_PASSWORD" \
#             rookhub-mariadb mariadb -u root --default-character-set=utf8mb4 rookhub
#
#    Der Dump enthaelt CREATE DATABASE IF NOT EXISTS + USE (--databases), die
#    Zieldatenbank muss also nicht vorher existieren. Bestehende Tabellen werden
#    per DROP TABLE ersetzt — ein Restore ueberschreibt den aktuellen Stand
#    dieser Datenbank vollstaendig.
#
# 3. Dienste wieder starten und Migrationsstand pruefen:
#       docker start rookhub-crawler rookhub-api
#       docker logs -f rookhub-api | head -50
#
# 4. Restore VORHER geuebt haben. Ein Backup, dessen Restore-Weg nie getestet
#    wurde, ist kein Backup — mindestens einmal in eine Wegwerf-Datenbank
#    einspielen: ... | docker exec -i ... mariadb -u root restore_test
#
# Einrichtung als cron/systemd-Timer: siehe scripts/systemd/ und docs/backup.md.
###############################################################################
set -euo pipefail

ENV_FILE="${ENV_FILE:-/opt/stacks/rookhub-schach/.env}"
DB_CONTAINER="${DB_CONTAINER:-rookhub-mariadb}"
DATABASES="${DATABASES:-rookhub chessresults}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/rookhub}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }
die() { log "FEHLER: $*" >&2; exit 1; }

# Passwort: ENV schlaegt .env. Aus der .env NUR diese eine Zeile ziehen (kein
# `source`) — die Datei enthaelt weitere Secrets und teils unquotierte Werte.
if [ -z "${MARIADB_ROOT_PASSWORD:-}" ]; then
  [ -r "$ENV_FILE" ] || die "Weder MARIADB_ROOT_PASSWORD gesetzt noch $ENV_FILE lesbar."
  MARIADB_ROOT_PASSWORD="$(sed -n 's/^[[:space:]]*MARIADB_ROOT_PASSWORD=//p' "$ENV_FILE" | head -n1 | sed 's/^["'\'']//;s/["'\'']$//')"
fi
[ -n "${MARIADB_ROOT_PASSWORD:-}" ] || die "MARIADB_ROOT_PASSWORD ist leer."

command -v docker >/dev/null 2>&1 || die "docker nicht gefunden."
docker inspect -f '{{.State.Running}}' "$DB_CONTAINER" 2>/dev/null | grep -q true \
  || die "Container '$DB_CONTAINER' laeuft nicht."

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

stamp="$(date '+%Y%m%d-%H%M%S')"
failed=0

for db in $DATABASES; do
  target="$BACKUP_DIR/$db-$stamp.sql.gz"
  tmp="$target.part"
  log "Dumpe '$db' -> $target"

  # MYSQL_PWD statt -p<pass>: sonst steht das Passwort in der Prozessliste des
  # Containers. --single-transaction haelt InnoDB konsistent ohne Table-Locks.
  if docker exec -i -e MYSQL_PWD="$MARIADB_ROOT_PASSWORD" "$DB_CONTAINER" \
      mariadb-dump -u root \
        --single-transaction --quick --routines --events --triggers \
        --default-character-set=utf8mb4 --databases "$db" 2>"$tmp.err" \
      | gzip -c > "$tmp"
  then
    # FALLE: die Pipe verschluckt Dump-Fehler (gzip liefert 0). Darum zusaetzlich
    # pruefen, ob das Archiv valide ist UND plausibel gross — ein abgebrochener
    # Dump erzeugt sonst ein "erfolgreiches", leeres Backup, das erst beim
    # Restore auffliegt.
    if ! gzip -t "$tmp" 2>/dev/null; then
      log "FEHLER: $db — Archiv ist beschaedigt, verwerfe."
      rm -f "$tmp"; failed=1; continue
    fi
    size=$(wc -c < "$tmp")
    if [ "$size" -lt 1024 ]; then
      log "FEHLER: $db — Dump nur $size Byte gross, verwerfe."
      cat "$tmp.err" >&2 || true
      rm -f "$tmp"; failed=1; continue
    fi
    rm -f "$tmp.err"
    mv "$tmp" "$target"
    chmod 600 "$target"
    log "OK: $db ($(du -h "$target" | cut -f1))"
  else
    log "FEHLER: mariadb-dump fuer '$db' fehlgeschlagen:"
    cat "$tmp.err" >&2 || true
    rm -f "$tmp" "$tmp.err"
    failed=1
  fi
done

# Rotation NUR wenn alle Dumps sauber waren — sonst wuerde ein kaputter Lauf die
# letzten funktionierenden Backups mit wegraeumen.
if [ "$failed" -eq 0 ]; then
  log "Raeume Dumps aelter als $RETENTION_DAYS Tage in $BACKUP_DIR"
  find "$BACKUP_DIR" -maxdepth 1 -name '*.sql.gz' -type f -mtime "+$RETENTION_DAYS" -print -delete
else
  log "Mindestens ein Dump fehlgeschlagen — Rotation uebersprungen."
fi

log "Fertig (Fehler: $failed)"
exit "$failed"
