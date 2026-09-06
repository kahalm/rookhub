#!/usr/bin/env bash
# Einmaliger Betriebsschritt nach dem Deploy des Turnierverzeichnisses:
# Ortslexikon (GeoNames, CC BY 4.0) laden, bereits eingesammelte Turniere verorten,
# ersten Sweep anstossen. Ohne diesen Lauf hat kein Turnier Koordinaten — Karte und
# Umkreissuche bleiben leer, die Liste funktioniert.
#
# Aufruf:  bash scripts/gazetteer-import.sh [API-Basis-URL]
#          Default-URL ist der Dev-Stack (http://127.0.0.1:5002).
# Das Passwort wird interaktiv abgefragt, geht ueber die Umgebung weiter (nicht als
# Argument — das stuende in der Prozessliste) und wird nirgends protokolliert.
set -euo pipefail

API="${1:-http://127.0.0.1:5002}"
LAENDER="${LAENDER:-AT DE CH IT CZ SK HU SI LI}"
SWEEP="${SWEEP:-AUT GER SUI}"

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

auth=(-H "Authorization: Bearer $TOKEN")

echo
echo "== 1/4  Weltweite Ortsliste (cities15000, ~11 MB) =="
curl -fsS "${auth[@]}" -X POST "$API/api/admin/tournament-directory/gazetteer/cities"; echo

echo
echo "== 2/4  Postleitzahlen je Land =="
for c in $LAENDER; do
  printf '  %s ... ' "$c"
  curl -fsS "${auth[@]}" -X POST "$API/api/admin/tournament-directory/gazetteer/postal/$c" \
    | python3 -c 'import sys,json; d=json.load(sys.stdin); print(f"{d[\"imported\"]} Eintraege")'
done

echo
echo "== 3/4  Erster Sweep ($SWEEP) =="
curl -fsS "${auth[@]}" -X POST "$API/api/admin/tournament-directory/sweep" \
  -H 'Content-Type: application/json' \
  --data-binary @<(python3 -c '
import json,sys
print(json.dumps({"federations": sys.argv[1].split()}))' "$SWEEP") \
  | python3 -m json.tool

echo
echo "== 4/4  Nicht verortete Eintraege nachziehen =="
curl -fsS "${auth[@]}" -X POST "$API/api/admin/tournament-directory/geocode-missing" | python3 -m json.tool

echo
echo "== Ergebnis =="
curl -fsS "${auth[@]}" "$API/api/admin/tournament-directory/status" \
  | python3 -c '
import sys, json
d = json.load(sys.stdin)
print(f"Turniere im Verzeichnis : {d[\"totalEntries\"]}")
print(f"davon verortet          : {d[\"geocoded\"]}")
print(f"Ortslexikon-Eintraege   : {d[\"gazetteerPlaces\"]}")
print("Quellen der Koordinaten :", d["byGeoSource"])
fehler = [s for s in d["sweeps"] if s.get("lastError")]
print("Sweeps mit Fehler       :", [s["federation"] for s in fehler] or "keine")'
