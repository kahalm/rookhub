#!/usr/bin/env bash
# Betriebsschritt nach dem Deploy von 0.420.0: Publikum und Format der SCHON GESPEICHERTEN
# Turniere aus ihren Namen ableiten (Jugendklasse, Geschlechtsklasse, Liga).
#
# Warum das nicht von selbst passiert: der naechtliche Sweep wertet diese Merkmale nur beim
# Schreiben einer Zeile aus. Die rund 4000 bestehenden Eintraege waeren also bis zu ihrem
# naechsten Durchlauf unklassifiziert, und der Filter "nur Erwachsene" liesse eine halb leere
# Liste zurueck. Derselbe Lauf ist auch der Weg, eine nachgeruestete Wortliste im
# TournamentClassifier auf den Bestand anzuwenden ("Schachrallye" = Nachwuchs).
#
# Die Turnier-ART (Einzel/Mannschaft) bleibt unberuehrt: die kommt aus einer zweiten
# chess-results-Abfrage und fuellt der naechste Sweep.
#
# Aufruf:  bash scripts/classify-directory.sh [API-Basis-URL]
#          Default-URL ist der Dev-Stack (http://127.0.0.1:5002).
set -euo pipefail

API="${1:-http://127.0.0.1:5002}"

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

echo
echo "== Publikum und Format aus den Turniernamen ableiten =="
curl -fsS -H "Authorization: Bearer $TOKEN" \
  -X POST "$API/api/admin/tournament-directory/classify" | python3 -m json.tool
