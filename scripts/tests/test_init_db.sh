#!/usr/bin/env bash
###############################################################################
# Test fuer init-db.sh — laeuft OHNE Container: `docker_process_sql` (im echten
# Lauf vom MariaDB-Entrypoint bereitgestellt) wird hier durch eine Funktion
# ersetzt, die das erzeugte SQL nur einsammelt.
#
# Geprueft wird der Fall, der die halbe Initialisierung ausloeste: ein Passwort
# mit einfachem Anfuehrungszeichen und Backslash. Unmaskiert beendet es das
# SQL-Literal, der Rest der Zeile wird als SQL gelesen.
#
#   ./scripts/tests/test_init_db.sh
###############################################################################
set -u
here="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="${1:-$here/../../init-db.sh}"   # init-db.sh liegt im REPO-ROOT, nicht in scripts/
[ -r "$SCRIPT" ] || { echo "FAIL: $SCRIPT nicht lesbar"; exit 1; }

out="$(mktemp)"; trap 'rm -f "$out"' EXIT
fails=0
fail() { echo "FAIL: $*"; fails=$((fails+1)); }
ok()   { echo "ok:   $*"; }

docker_process_sql() { cat >> "$out"; }
export -f docker_process_sql 2>/dev/null || true

CRAWLER_DB_NAME=chessresults CRAWLER_DB_USER=crawler \
CRAWLER_DB_PASSWORD='pa$$\wo''rd' \
ROOKHUB_DB_NAME=rookhub ROOKHUB_DB_USER=rookhub \
ROOKHUB_DB_PASSWORD="it's \\ fine" \
  bash -c "docker_process_sql() { cat >> '$out'; }; source '$SCRIPT'"
rc=$?

[ "$rc" -eq 0 ] && ok "Exit-Code 0" || fail "Exit-Code $rc"

# 1) Das Anfuehrungszeichen im Passwort ist verdoppelt (MySQL-Maskierung), der
#    Backslash verdoppelt — sonst bricht das Literal auf.
grep -qF "IDENTIFIED BY 'it''s \\\\ fine'" "$out" \
  && ok "Apostroph + Backslash im Passwort maskiert" \
  || fail "Passwort-Maskierung fehlt: $(grep -F 'rookhub' "$out" | head -2)"

# 2) Kein unmaskiertes Anfuehrungszeichen mitten im Literal (Gegenprobe).
grep -qF "IDENTIFIED BY 'it's" "$out" \
  && fail "unmaskiertes Anfuehrungszeichen im SQL" \
  || ok "kein unmaskiertes Anfuehrungszeichen"

# 3) Passwort-Rotation wirkt: ALTER USER zieht ein bestehendes Konto nach
#    (CREATE USER IF NOT EXISTS allein tut das NICHT).
[ "$(grep -c '^ALTER USER ' "$out")" -eq 2 ] \
  && ok "ALTER USER fuer beide Konten (Rotation wirkt)" \
  || fail "ALTER USER fehlt (Passwort-Rotation waere ein No-op)"

# 4) Beide Datenbanken + Rechte
for db in chessresults rookhub; do
  grep -qF "CREATE DATABASE IF NOT EXISTS \`$db\`" "$out" \
    && ok "CREATE DATABASE fuer '$db'" || fail "CREATE DATABASE fuer '$db' fehlt"
done

echo
if [ "$fails" -eq 0 ]; then echo "PASS: alle Checks gruen."; exit 0; fi
echo "FAIL: $fails Check(s) rot."; exit 1
