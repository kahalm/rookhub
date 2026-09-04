#!/usr/bin/env bash
###############################################################################
# Test fuer scripts/backup-db.sh — laeuft komplett gegen ein Fake-`docker`
# (kein Container noetig). Prueft:
#   1. alle DREI Datenbanken (rookhub, chessresults, piratechess) werden gedumpt
#   2. das Passwort geht per --defaults-extra-file in den Container — NICHT als
#      Umgebungsvariable (MYSQL_PWD) und NICHT in der Kommandozeile
#   3. Sonderzeichen im Passwort (Backslash, ") werden my.cnf-konform escaped
#   4. trap-Aufraeumen: Host-Tempdatei weg, Container-Kopie per `rm -f` entfernt
#   5. FEHLERFALL (Dump zu klein): Exit != 0, KEIN Archiv, KEINE Rotation (alte Backups
#      bleiben!) und keine liegengebliebene .part.err — genau die drei Schutzmechanismen,
#      die das Skript selbst als „FALLE" kommentiert und die vorher ungetestet waren
#   6. ERFOLGSFALL: die Rotation raeumt einen wirklich alten Dump weg
#
#   ./scripts/tests/test_backup_db.sh                # testet scripts/backup-db.sh
#   ./scripts/tests/test_backup_db.sh /pfad/zu/alt.sh  # beliebige Version testen
###############################################################################
set -u

here="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="${1:-$here/../backup-db.sh}"
[ -r "$SCRIPT" ] || { echo "FAIL: $SCRIPT nicht lesbar"; exit 1; }

sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
export FAKE_DOCKER_LOG="$sandbox/docker.log"
export FAKE_CONTAINER_FS="$sandbox/containerfs"
export FAKE_CNF_CAPTURE="$sandbox/captured.cnf"
mkdir -p "$sandbox/bin" "$FAKE_CONTAINER_FS"
: > "$FAKE_DOCKER_LOG"

# ---------------------------------------------------------------- Fake docker
cat > "$sandbox/bin/docker" <<'FAKE'
#!/usr/bin/env bash
# Fake-docker: zeichnet Argumente + Passwort-Umgebung auf und spielt die vom
# Backup-Skript genutzten Subkommandos (inspect/cp/exec) nach.
echo "ARGS: $*" >> "$FAKE_DOCKER_LOG"
[ -n "${MYSQL_PWD:-}" ] && echo "ENV: MYSQL_PWD gesetzt" >> "$FAKE_DOCKER_LOG"
cmd="$1"; shift
case "$cmd" in
  inspect)
    echo "true" ;;
  cp)
    src="$1"; dest="${2#*:}"
    mkdir -p "$FAKE_CONTAINER_FS$(dirname "$dest")"
    cp "$src" "$FAKE_CONTAINER_FS$dest"
    cp "$src" "$FAKE_CNF_CAPTURE"           # Kopie fuer die Assertions sichern
    echo "CP_SRC: $src" >> "$FAKE_DOCKER_LOG" ;;
  exec)
    # Flags (-i, -e VAR) und Container-Namen ueberspringen, dann dispatchen.
    args=("$@")
    if printf '%s\n' "${args[@]}" | grep -q '^mariadb-dump$'; then
      # gzip-schlecht komprimierbare Daten -> Archiv > 1 KiB (Mindestgroessen-Check).
      # FAKE_DUMP_BYTES steuert die Groesse: klein => das Skript MUSS verwerfen.
      head -c "${FAKE_DUMP_BYTES:-8192}" /dev/urandom
      [ -n "${FAKE_DUMP_STDERR:-}" ] && echo "$FAKE_DUMP_STDERR" >&2
    elif printf '%s\n' "${args[@]}" | grep -q '^rm$'; then
      target="${args[${#args[@]}-1]}"
      rm -f "$FAKE_CONTAINER_FS$target"
      echo "RM: $target" >> "$FAKE_DOCKER_LOG"
    fi ;;
esac
exit 0
FAKE
chmod +x "$sandbox/bin/docker"

# ------------------------------------------------------------------- Testlauf
pw='Ge"heim\pw'
PATH="$sandbox/bin:$PATH" \
  MARIADB_ROOT_PASSWORD="$pw" \
  DB_CONTAINER=fake-mariadb \
  BACKUP_DIR="$sandbox/backups" \
  bash "$SCRIPT" > "$sandbox/run.log" 2>&1
rc=$?

fails=0
fail() { echo "FAIL: $*"; fails=$((fails+1)); }
ok()   { echo "ok:   $*"; }

[ "$rc" -eq 0 ] && ok "Exit-Code 0" || fail "Exit-Code $rc (Log: $(cat "$sandbox/run.log"))"

# 1) Alle drei Datenbanken gedumpt
for db in rookhub chessresults piratechess; do
  if ls "$sandbox/backups/$db-"*.sql.gz >/dev/null 2>&1; then
    ok "Dump fuer '$db' vorhanden"
  else
    fail "Dump fuer '$db' fehlt"
  fi
done

# 2) Passwort nie als ENV oder in der Kommandozeile
if grep -q "ENV: MYSQL_PWD gesetzt" "$FAKE_DOCKER_LOG"; then
  fail "MYSQL_PWD stand in der Umgebung des docker-Prozesses (Host-/proc-Leck)"
else
  ok "kein MYSQL_PWD in der docker-Umgebung"
fi
if grep -F -- "$pw" "$FAKE_DOCKER_LOG" | grep -q "ARGS:"; then
  fail "Passwort stand in einer docker-Kommandozeile"
else
  ok "Passwort in keiner Kommandozeile"
fi
if grep -q -- "--defaults-extra-file=" "$FAKE_DOCKER_LOG"; then
  ok "mariadb-dump nutzt --defaults-extra-file"
else
  fail "kein --defaults-extra-file im mariadb-dump-Aufruf"
fi

# 3) my.cnf-Escaping: \ -> \\ und " -> \"
if [ -r "$FAKE_CNF_CAPTURE" ] && grep -qF 'password="Ge\"heim\\pw"' "$FAKE_CNF_CAPTURE"; then
  ok "Sonderzeichen-Passwort korrekt escaped in der defaults-Datei"
else
  fail "defaults-Datei fehlt oder Passwort-Escaping falsch: $(cat "$FAKE_CNF_CAPTURE" 2>/dev/null)"
fi

# 4) Aufraeumen: Host-Tempdatei weg + Container-Kopie entfernt
src="$(sed -n 's/^CP_SRC: //p' "$FAKE_DOCKER_LOG" | head -n1)"
if [ -n "$src" ] && [ ! -e "$src" ]; then
  ok "Host-Tempdatei nach dem Lauf entfernt (trap)"
else
  fail "Host-Tempdatei existiert noch oder wurde nie kopiert: '$src'"
fi
if grep -q "^RM: " "$FAKE_DOCKER_LOG" && [ -z "$(find "$FAKE_CONTAINER_FS" -name '*.cnf' -print -quit)" ]; then
  ok "Container-Kopie der defaults-Datei entfernt"
else
  fail "Container-Kopie der defaults-Datei nicht entfernt"
fi

# ---------------------------------------------------- Lauf 2: Dump zu klein (Fehlerfall)
# Vorher einen ALTEN Dump hinlegen: er darf NICHT wegrotiert werden, wenn der Lauf scheitert
# (sonst nimmt der erste kaputte Lauf die letzte funktionierende Historie mit).
faildir="$sandbox/backups-fail"
mkdir -p "$faildir"
: > "$faildir/rookhub-alt.sql.gz"
touch -d '30 days ago' "$faildir/rookhub-alt.sql.gz"

PATH="$sandbox/bin:$PATH" \
  MARIADB_ROOT_PASSWORD="$pw" \
  DB_CONTAINER=fake-mariadb \
  BACKUP_DIR="$faildir" \
  FAKE_DUMP_BYTES=10 \
  FAKE_DUMP_STDERR="mariadb-dump: Got error 1045" \
  bash "$SCRIPT" > "$sandbox/run-fail.log" 2>&1
rc_fail=$?

[ "$rc_fail" -ne 0 ] && ok "Fehlerfall: Exit-Code != 0 ($rc_fail)" \
  || fail "Fehlerfall: Exit-Code 0 trotz zu kleinem Dump"
if ls "$faildir"/rookhub-2*.sql.gz >/dev/null 2>&1; then
  fail "Fehlerfall: unbrauchbarer Dump wurde behalten"
else
  ok "Fehlerfall: kein Archiv angelegt"
fi
[ -e "$faildir/rookhub-alt.sql.gz" ] && ok "Fehlerfall: alter Dump NICHT wegrotiert" \
  || fail "Fehlerfall: Rotation lief trotz Fehler und loeschte den alten Dump"
if [ -n "$(find "$faildir" -name '*.part.err' -print -quit)" ]; then
  fail "Fehlerfall: .part.err liegengeblieben (Rotation fasst sie nie an)"
else
  ok "Fehlerfall: keine .part.err zurueckgelassen"
fi
grep -q "Rotation uebersprungen" "$sandbox/run-fail.log" \
  && ok "Fehlerfall: Rotation ausdruecklich uebersprungen" \
  || fail "Fehlerfall: Log nennt die uebersprungene Rotation nicht"

# ------------------------------------------- Lauf 3: Erfolgsfall raeumt alte Dumps wirklich weg
rotdir="$sandbox/backups-rot"
mkdir -p "$rotdir"
: > "$rotdir/rookhub-alt.sql.gz"
touch -d '30 days ago' "$rotdir/rookhub-alt.sql.gz"

PATH="$sandbox/bin:$PATH" \
  MARIADB_ROOT_PASSWORD="$pw" \
  DB_CONTAINER=fake-mariadb \
  BACKUP_DIR="$rotdir" \
  bash "$SCRIPT" > "$sandbox/run-rot.log" 2>&1
rc_rot=$?

[ "$rc_rot" -eq 0 ] && ok "Erfolgsfall: Exit-Code 0" || fail "Erfolgsfall: Exit-Code $rc_rot"
[ ! -e "$rotdir/rookhub-alt.sql.gz" ] && ok "Erfolgsfall: alter Dump wegrotiert" \
  || fail "Erfolgsfall: Rotation entfernte den 30 Tage alten Dump nicht"
ls "$rotdir"/rookhub-2*.sql.gz >/dev/null 2>&1 && ok "Erfolgsfall: neuer Dump liegt da" \
  || fail "Erfolgsfall: kein neuer Dump"

echo
if [ "$fails" -eq 0 ]; then
  echo "PASS: alle Checks gruen."
  exit 0
else
  echo "FAIL: $fails Check(s) rot."
  exit 1
fi
