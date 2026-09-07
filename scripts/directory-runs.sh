#!/usr/bin/env bash
# Die drei Nachtrags-Laeufe des Turnierverzeichnisses, mit EINER Anmeldung.
#
# Reihenfolge ist Absicht — kurz vor lang, damit man frueh sieht, ob etwas grundsaetzlich klemmt:
#   1. sweep         Turnierart + System der genannten Foederationen (Vorgabe AUT).
#                    Vier Crawler-Abrufe je Foederation, rund zehn Sekunden.
#   2. fide-details  Bedenkzeit, Turniersystem, Runden-/Teilnehmerzahl und vor allem die
#                    ANSCHRIFT der FIDE-Eintraege. Ein Abruf je Ereignis (~144), rund 9 Minuten.
#   3. round-plans   Spieltermine langlaufender Turniere, mit `retryEmpty`. Ein Abruf je Turnier
#                    (~615 Kandidaten), rund 50 Minuten.
#
# Die beiden langen Schritte laufen SO LANGE, BIS NICHTS MEHR KOMMT (hoechstens `MAX_ROUNDS`
# Durchgaenge). Das geht erst, seit die Auswahl nach dem ALTER des Vermerks sortiert: vorher nahm
# jeder Durchgang wieder dieselben vordersten Turniere, eine Wiederholung war also nutzlos.
# `checked` zaehlt die VERSUCHTEN, nicht die erfolgreichen — genau deshalb ist „bis 0" die
# richtige Abbruchbedingung und nicht „bis nichts mehr gefunden wird".
#
# Aufruf:  bash scripts/directory-runs.sh [API-Basis-URL] [FOEDERATIONEN] [LIMIT]
#   API-Basis-URL   Vorgabe http://127.0.0.1:5002 (Dev-Stack)
#   FOEDERATIONEN   Komma-getrennt, Vorgabe AUT. Leer ("") laesst den Sweep aus.
#   LIMIT           Turniere je Durchgang, Vorgabe 200
#
# Beispiele:
#   bash scripts/directory-runs.sh                          # Dev, AUT, 200
#   bash scripts/directory-runs.sh http://127.0.0.1:5002 "AUT,GER,SUI"
#   bash scripts/directory-runs.sh http://127.0.0.1:5002 "" 50   # ohne Sweep, kleine Portionen
set -euo pipefail

API="${1:-http://127.0.0.1:5002}"
FEDS="${2-AUT}"
LIMIT="${3:-200}"

# Deckel gegen eine Endlosschleife, falls ein Durchgang dauerhaft dieselbe Zahl meldet (etwa weil
# der Crawler jeden Abruf abweist): 20 x 200 = 4000 Turniere, weit ueber jedem echten Rueckstand.
MAX_ROUNDS="${MAX_ROUNDS:-20}"

read -rp "Admin-Benutzername [admin]: " ADMIN_USER
ADMIN_USER="${ADMIN_USER:-admin}"
read -rsp "Passwort: " PASS; echo

# Benutzername und Passwort gehen ueber die UMGEBUNG an python, nicht als Argumente: Argumente
# stehen in /proc/<pid>/cmdline und sind fuer jeden Nutzer des Rechners lesbar, solange der Aufruf
# laeuft. /proc/<pid>/environ gehoert dagegen nur dem Besitzer.
TOKEN=$(ADMIN_USER="$ADMIN_USER" PASS="$PASS" python3 -c '
import json, os
print(json.dumps({"username": os.environ["ADMIN_USER"], "password": os.environ["PASS"]}))' \
  | curl -sS -X POST "$API/api/auth/login" -H 'Content-Type: application/json' --data-binary @- \
  | python3 -c 'import sys,json;
try: print(json.load(sys.stdin).get("token",""))
except Exception: print("")' || true)

[ -n "$TOKEN" ] || { echo "Login fehlgeschlagen — Benutzername/Passwort pruefen."; exit 1; }
unset PASS
echo "Login ok."

FAILED=0

post() { curl -fsS -H "Authorization: Bearer $TOKEN" -X POST "$@"; }

# Liest EIN Feld aus einer JSON-Antwort. Fehlt es, kommt 0 — der Aufrufer bricht dann ab, statt
# auf einer leeren Zeichenkette zu rechnen.
field() { python3 -c 'import sys,json
try: print(int(json.load(sys.stdin).get(sys.argv[1], 0)))
except Exception: print(0)' "$1"; }

# ---------------------------------------------------------------------------
# 1. Sweep: Turnierart (Einzel/Mannschaft) UND System (Schweizer/Rundenturnier)
# ---------------------------------------------------------------------------
if [ -n "$FEDS" ]; then
  echo
  echo "== Sweep $FEDS =="
  BODY=$(FEDS="$FEDS" python3 -c '
import json, os
print(json.dumps({"federations": [f.strip().upper() for f in os.environ["FEDS"].split(",") if f.strip()]}))')
  if ! post "$API/api/admin/tournament-directory/sweep" \
        -H 'Content-Type: application/json' -d "$BODY" | python3 -m json.tool; then
    echo "FEHLGESCHLAGEN: Sweep"
    FAILED=1
  fi
fi

# ---------------------------------------------------------------------------
# 2. + 3. Die beiden langen Laeufe, je bis nichts mehr kommt
# ---------------------------------------------------------------------------
# $1 Ueberschrift · $2 Pfad mit Parametern · $3 Name des Zaehlerfeldes in der Antwort
run_until_done() {
  local title="$1" path="$2" counter="$3"
  local round=1 total=0

  echo
  echo "== $title =="
  while [ "$round" -le "$MAX_ROUNDS" ]; do
    local out
    if ! out=$(post "$API$path"); then
      echo "  Durchgang $round: FEHLGESCHLAGEN"
      FAILED=1
      return
    fi

    echo "  Durchgang $round: $out"
    local n
    n=$(printf '%s' "$out" | field "$counter")
    total=$((total + n))

    # 0 heisst „keine Kandidaten mehr" — das ist das Ende, nicht ein Fehlschlag.
    [ "$n" -eq 0 ] && break
    round=$((round + 1))
  done

  if [ "$round" -gt "$MAX_ROUNDS" ]; then
    echo "  Deckel von $MAX_ROUNDS Durchgaengen erreicht — es sind noch Kandidaten offen."
    echo "  Nochmal starten, oder MAX_ROUNDS hochsetzen."
  fi
  echo "  Summe: $total vorgenommen."
}

run_until_done "FIDE-Detailangaben (Bedenkzeit, System, Anschrift)" \
               "/api/admin/tournament-directory/fide-details?limit=$LIMIT" \
               "checked"

run_until_done "Spieltermine langlaufender Turniere (retryEmpty)" \
               "/api/admin/tournament-directory/round-plans?limit=$LIMIT&retryEmpty=true" \
               "checked"

echo
[ "$FAILED" -eq 0 ] && echo "Alle Laeufe durch." \
  || { echo "Mindestens ein Lauf ist gescheitert (siehe oben)."; exit 1; }
