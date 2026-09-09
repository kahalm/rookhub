#!/usr/bin/env bash
# Die Nachtrags-Laeufe des Turnierverzeichnisses, mit EINER Anmeldung.
#
# Sie laufen auch jede Nacht von selbst (TournamentDirectoryScheduler, 03:00 UTC). Dieses Skript
# ist der Weg, sie SOFORT anzustossen — nach einem Deploy, der eine neue Quelle mitbringt, oder
# wenn man nicht bis morgen warten will.
#
# Reihenfolge ist Absicht — kurz vor lang, damit man frueh sieht, ob etwas grundsaetzlich klemmt:
#   0. gazetteer     NUR mit GAZETTEER=1: Postleitzahlen fuer GB/IE/FR/NO/RO/NL/SE nachladen.
#                    Der genaueste Weg der Verortung — heute gibt es sie nur fuer AT, DE und SK.
#   1. sweep         Turnierart + System der genannten Foederationen (Vorgabe AUT).
#                    Vier Crawler-Abrufe je Foederation, rund zehn Sekunden.
#   2. quellen       Die sechzehn Zusatzquellen neben der chess-results-Turniersuche: der
#                    Ankuendigungskalender, Italien, Slowenien, Slowakei, Ungarn, Tschechien,
#                    Deutschland, England, Irland, Frankreich, Norwegen, Schottland, Rumaenien,
#                    Wales und die Niederlande — je ein Aufruf. Zusammen rund zehn Minuten, das meiste davon
#                    Ungarn (75 s je Abruf) und Deutschland (zwei Abrufe je Region mit der
#                    Wartezeit aus deren robots.txt).
#   3. polen         Eigener Schritt, weil er WIEDERHOLT wird: die Liste kostet einen Abruf, die
#                    Detailseite einen je noch unbekanntem Turnier — und die sind gedeckelt. Bei
#                    611 Turnieren braucht es ein paar Durchgaenge, bis nichts mehr kommt.
#   4. fide-details  Bedenkzeit, Turniersystem, Runden-/Teilnehmerzahl und vor allem die
#                    ANSCHRIFT der FIDE-Eintraege. Ein Abruf je Ereignis (~144), rund 9 Minuten.
#   5. round-plans   Spieltermine langlaufender Turniere, mit `retryEmpty`. Ein Abruf je Turnier
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
# SKIP_SOURCES=1 laesst die Zusatzquellen und Polen aus (Schritte 2 und 3) — der Weg, wenn nur
# die beiden langen Nachtraege gebraucht werden.
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
# 0. Postleitzahlen der Laender, fuer die es noch keine gibt
# ---------------------------------------------------------------------------
# Der genaueste Weg der Verortung ist die Postleitzahl — und im Lexikon stehen heute nur die von
# AT, DE und SK. Fuer alles andere traegt allein der Ortsname, also die Stadtmitte statt der
# Spielstaette. Der Import laeuft je Land gegen GeoNames und wirkt RUECKWIRKEND auf den ganzen
# Bestand, nicht nur auf die neuen Quellen.
#
# Er steht VOR allem anderen, weil jede Quelle danach besser verortet als davor. Und er laeuft nur
# auf Wunsch: er zieht je Land eine Datei von GeoNames und dauert seine Zeit.
#   GAZETTEER=1 bash scripts/directory-runs.sh …
if [ "${GAZETTEER:-0}" = "1" ]; then
  echo
  echo "== Postleitzahlen importieren =="
  for ISO in GB IE FR NO RO NL SE; do
    printf '  %s: ' "$ISO"
    post "$API/api/admin/tournament-directory/gazetteer/postal/$ISO" \
      | python3 -c 'import sys,json
try:
    d = json.load(sys.stdin)
    print(f"{d.get(\"imported\", 0)} Orte" + (f" — {d[\"error\"]}" if d.get("error") else ""))
except Exception: print("keine Antwort")' || { echo "FEHLGESCHLAGEN"; FAILED=1; }
  done
fi

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

# ---------------------------------------------------------------------------
# 2. Die Zusatzquellen — je ein Aufruf
# ---------------------------------------------------------------------------
# Ein Fehlschlag hier beendet das Skript NICHT: die Quellen sind voneinander unabhaengig, und ein
# Ausfall bei einer soll die uebrigen nicht mitnehmen (dieselbe Regel wie im naechtlichen Lauf).
source_run() {
  local title="$1" path="$2"
  echo
  echo "== $title =="
  if ! post "$API$path"; then
    echo "  FEHLGESCHLAGEN"
    FAILED=1
    return
  fi
  echo
}

if [ "${SKIP_SOURCES:-0}" != "1" ]; then
  source_run "Ankuendigungskalender (chess-results, 16 Foederationen)" \
             "/api/admin/tournament-directory/calendar"
  source_run "Italien (FSI)"            "/api/admin/tournament-directory/fsi"
  source_run "Slowenien (SZS)"          "/api/admin/tournament-directory/szs"
  source_run "Slowakei (chess.sk)"      "/api/admin/tournament-directory/chess-sk"
  source_run "Ungarn (chess.hu)"        "/api/admin/tournament-directory/chess-hu"
  source_run "Tschechien (chess.cz)"    "/api/admin/tournament-directory/chess-cz"
  source_run "Deutschland (schachbund)" "/api/admin/tournament-directory/schachbund"
  source_run "England (ECF)"            "/api/admin/tournament-directory/ecf"
  source_run "Irland (ICU)"             "/api/admin/tournament-directory/icu"
  source_run "Frankreich (FFE)"         "/api/admin/tournament-directory/ffe"
  source_run "Norwegen (sjakk.no)"      "/api/admin/tournament-directory/sjakk"
  source_run "Schottland"               "/api/admin/tournament-directory/chess-scotland"
  source_run "Rumaenien (FRSah)"        "/api/admin/tournament-directory/frsah"
  source_run "Wales (WCU)"              "/api/admin/tournament-directory/wcu"
  source_run "Niederlande (KNSB)"       "/api/admin/tournament-directory/knsb"
fi

# Polen: die Liste ist ein Abruf, die Detailseiten sind gedeckelt — deshalb wiederholt, bis keine
# mehr geholt wird. `Details` zaehlt die gelesenen Detailseiten.
if [ "${SKIP_SOURCES:-0}" != "1" ]; then
  run_until_done "Polen (chessarbiter) — Liste und Detailseiten" \
                 "/api/admin/tournament-directory/chess-arbiter" \
                 "details"
fi

run_until_done "FIDE-Detailangaben (Bedenkzeit, System, Anschrift)" \
               "/api/admin/tournament-directory/fide-details?limit=$LIMIT" \
               "checked"

run_until_done "Spieltermine langlaufender Turniere (retryEmpty)" \
               "/api/admin/tournament-directory/round-plans?limit=$LIMIT&retryEmpty=true" \
               "checked"

echo
[ "$FAILED" -eq 0 ] && echo "Alle Laeufe durch." \
  || { echo "Mindestens ein Lauf ist gescheitert (siehe oben)."; exit 1; }
