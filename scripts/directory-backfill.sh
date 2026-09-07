#!/usr/bin/env bash
# Betriebsschritte nach dem Deploy von 0.428.0 — alle vier Nachtraege fuer den BESTEHENDEN
# Turnierbestand, mit EINER Anmeldung.
#
# Warum das nicht von selbst passiert: der naechtliche Sweep leitet Merkmale nur beim Schreiben
# einer Zeile ab und rotiert die Foederationen ueber eine Woche. Der Bestand (rund 4900 offene
# Eintraege) haengt also bis zu seinem naechsten Durchlauf hinterher — und die drei rein
# rechnenden Schritte brauchen dafuer gar kein Netz.
#
# Die Schritte, in dieser Reihenfolge:
#   1. classify         Publikum + Format aus den Turniernamen (Jugendklasse, Geschlecht, Liga).
#                       Auch der Weg, eine nachgeruestete Wortliste anzuwenden ("Schachrallye").
#   2. backfill-sources Herkunftsvermerk nachtragen (jeder Altbestand stammt aus chess-results).
#   3. round-plans      SPIELTERMINE langlaufender Turniere holen — ein Seitenabruf je Turnier,
#                       deshalb gedeckelt (LIMIT). Mehrfach aufrufbar, der Rest bleibt liegen.
#   4. fide             Den FIDE-Kalender lesen (laufendes Jahr + 2, ein Abruf je Jahr).
#
# Aufruf:  bash scripts/directory-backfill.sh [API-Basis-URL] [ROUND_PLAN_LIMIT]
#          Default-URL ist der Dev-Stack (http://127.0.0.1:5002), Default-Limit 200.
set -euo pipefail

API="${1:-http://127.0.0.1:5002}"
LIMIT="${2:-200}"

read -rp "Admin-Benutzername [admin]: " ADMIN_USER
ADMIN_USER="${ADMIN_USER:-admin}"
read -rsp "Passwort: " PASS; echo

# Benutzername und Passwort gehen ueber die UMGEBUNG an python, nicht als Argumente:
# Argumente stehen in /proc/<pid>/cmdline und sind damit fuer jeden Nutzer des Rechners
# lesbar, solange der Aufruf laeuft. /proc/<pid>/environ gehoert dagegen nur dem Besitzer.
TOKEN=$(ADMIN_USER="$ADMIN_USER" PASS="$PASS" python3 -c '
import json, os
print(json.dumps({"username": os.environ["ADMIN_USER"], "password": os.environ["PASS"]}))' \
  | curl -sS -X POST "$API/api/auth/login" -H 'Content-Type: application/json' --data-binary @- \
  | python3 -c 'import sys,json;
try: print(json.load(sys.stdin).get("token",""))
except Exception: print("")' || true)

[ -n "$TOKEN" ] || { echo "Login fehlgeschlagen — Benutzername/Passwort pruefen."; exit 1; }
echo "Login ok."

# Ein fehlgeschlagener Schritt beendet den Lauf NICHT: die vier sind voneinander unabhaengig,
# und die drei rechnenden sollen laufen, auch wenn der eine mit Netz gerade scheitert.
run() {
  local title="$1"; shift
  echo
  echo "== $title =="
  if ! curl -fsS -H "Authorization: Bearer $TOKEN" -X POST "$@" | python3 -m json.tool; then
    echo "FEHLGESCHLAGEN: $title"
    FAILED=1
  fi
}

FAILED=0
run "Publikum und Format aus den Turniernamen ableiten" \
    "$API/api/admin/tournament-directory/classify"
run "Herkunftsvermerk fuer den Altbestand nachtragen" \
    "$API/api/admin/tournament-directory/backfill-sources"
run "Spieltermine langlaufender Turniere holen (max. $LIMIT)" \
    "$API/api/admin/tournament-directory/round-plans?limit=$LIMIT"
run "FIDE-Kalender lesen" \
    "$API/api/admin/tournament-directory/fide"

echo
[ "$FAILED" -eq 0 ] && echo "Alle vier Schritte durch." \
  || { echo "Mindestens ein Schritt ist gescheitert (siehe oben)."; exit 1; }
