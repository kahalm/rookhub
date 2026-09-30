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
# Und (Codereview 2026-09-29, I1-003): die App-Benutzer gelten nur fuer die
# Adressbereiche der Docker-Netze, nie fuer '%' — sonst meldet sich aus LAN und
# VPN an, wer den veroeffentlichten Port erreicht.
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
#    (CREATE USER IF NOT EXISTS allein tut das NICHT). Zwei Benutzer x zwei
#    Adressbereiche der Vorgabe = vier Konten.
[ "$(grep -c '^ALTER USER ' "$out")" -eq 4 ] \
  && ok "ALTER USER fuer alle vier Konten (Rotation wirkt)" \
  || fail "ALTER USER fehlt (Passwort-Rotation waere ein No-op): $(grep -c '^ALTER USER ' "$out")"

# 3b) Kein Konto fuer beliebige Adressen — '%' war der Weg aus LAN und VPN.
grep -qF "@'%'" "$out" \
  && fail "App-Benutzer mit Host '%': $(grep -F "@'%'" "$out" | head -1)" \
  || ok "kein App-Benutzer mit Host '%'"

# 3c) Vorgabe = beide Docker-Adressvorraete, fuer beide Benutzer mit Rechten.
for user in crawler rookhub; do
  for host in 172.16.0.0/255.240.0.0 192.168.0.0/255.255.0.0; do
    grep -qF "TO '$user'@'$host';" "$out" \
      && ok "GRANT fuer '$user'@'$host'" || fail "GRANT fuer '$user'@'$host' fehlt"
  done
done

# 4) Beide Datenbanken + Rechte
for db in chessresults rookhub; do
  grep -qF "CREATE DATABASE IF NOT EXISTS \`$db\`" "$out" \
    && ok "CREATE DATABASE fuer '$db'" || fail "CREATE DATABASE fuer '$db' fehlt"
done

# 5) DB_APP_HOSTS ersetzt die Vorgabe (eigene Adress-Pools im Docker-Daemon).
out2="$(mktemp)"; trap 'rm -f "$out" "$out2"' EXIT
CRAWLER_DB_NAME=chessresults CRAWLER_DB_USER=crawler CRAWLER_DB_PASSWORD=x \
ROOKHUB_DB_NAME=rookhub ROOKHUB_DB_USER=rookhub ROOKHUB_DB_PASSWORD=y \
DB_APP_HOSTS='10.99.0.0/255.255.0.0' \
  bash -c "docker_process_sql() { cat >> '$out2'; }; source '$SCRIPT'"
[ "$?" -eq 0 ] && [ "$(grep -c '^ALTER USER ' "$out2")" -eq 2 ] \
  && grep -qF "TO 'rookhub'@'10.99.0.0/255.255.0.0';" "$out2" \
  && ! grep -qF "172.16.0.0" "$out2" \
  && ok "DB_APP_HOSTS ersetzt die Vorgabe" \
  || fail "DB_APP_HOSTS wirkt nicht: $(grep '^GRANT' "$out2" | head -2)"

# 6) Ein DB_APP_HOSTS ohne Eintrag bricht ab, statt Benutzer ohne Host anzulegen.
CRAWLER_DB_NAME=chessresults CRAWLER_DB_USER=crawler CRAWLER_DB_PASSWORD=x \
ROOKHUB_DB_NAME=rookhub ROOKHUB_DB_USER=rookhub ROOKHUB_DB_PASSWORD=y \
DB_APP_HOSTS='   ' \
  bash -c "docker_process_sql() { cat > /dev/null; }; source '$SCRIPT'" 2>/dev/null \
  && fail "leeres DB_APP_HOSTS lief durch" \
  || ok "DB_APP_HOSTS ohne Eintrag bricht ab"

echo
if [ "$fails" -eq 0 ]; then echo "PASS: alle Checks gruen."; exit 0; fi
echo "FAIL: $fails Check(s) rot."; exit 1
