#!/bin/bash
# Aufruf: bash test/schedule.test.sh  — Exit 0 = alles gut.
#
# Prueft den Zeitplan (ENGINE_SCHEDULE): erst die Regel-Auswertung ohne jeden Prozess
# (ENGINE_SCHEDULE_AT rechnet nur nach), danach die Aufsicht mit echten Stub-Providern —
# laeuft wirklich die vorgesehene ZAHL von Engines, und zwar keine bei 0 %?
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
fails=0
ok()   { echo "ok   $1"; }
fail() { echo "FAIL $1"; fails=$((fails + 1)); }

fake_engine="$work/stockfish"; printf '#!/bin/sh\n' > "$fake_engine"; chmod +x "$fake_engine"

# ---------------------------------------------------------------- Regel-Auswertung
at() {   # at "<Zeitplan>" "<Tag HH:MM>" [ENGINE_BACKGROUND_COUNT]
    env -i PATH="$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" \
        ENGINE_BACKGROUND_COUNT="${3:-15}" ENGINE_SCHEDULE="$1" ENGINE_SCHEDULE_AT="$2" \
        bash "$ROOT/entrypoint.sh" 2>&1 | tail -1
}
expect() {   # expect "<Beschreibung>" "<erwartet>" "<bekommen>"
    if [ "$3" = "$2" ]; then ok "$1"; else fail "$1 — erwartet '$2', bekommen '$3'"; fi
}

PLAN="Mo-Do 08:00-17:00 0%; Fr 08:00-14:00 25%; Sa,So 100%"
expect "Sperrzeit: 0 %, keine Engine"      "ZEITPLAN Mo 09:00: 0% — 0 von 16 Engine(s)"    "$(at "$PLAN" "Mo 09:00")"
expect "abends wieder voll"                "ZEITPLAN Mo 19:00: 100% — 16 von 16 Engine(s)" "$(at "$PLAN" "Mo 19:00")"
expect "Teillast: Live + Anteil"           "ZEITPLAN Fr 09:00: 25% — 5 von 16 Engine(s)"   "$(at "$PLAN" "Fr 09:00")"
expect "Ende der Spanne zaehlt nicht mehr" "ZEITPLAN Do 17:00: 100% — 16 von 16 Engine(s)" "$(at "$PLAN" "Do 17:00")"
expect "Kurzform ohne Uhrzeit"             "ZEITPLAN Sa 03:00: 100% — 16 von 16 Engine(s)" "$(at "$PLAN" "Sa 03:00")"
expect "ueber Mitternacht"                 "ZEITPLAN Mi 02:00: 100% — 16 von 16 Engine(s)" "$(at "* 22:00-06:00 100%; * 06:00-22:00 0%" "Mi 02:00")"
# Spanne ueber Mitternacht MIT Tagesangabe gehoert zum Starttag: Fr 22:00 bis Sa 06:00.
NIGHT="Fr 22:00-06:00 0%"
expect "Mitternacht mit Tag: Sa 02:00 noch in der Spanne" "ZEITPLAN Sa 02:00: 0% — 0 von 16 Engine(s)"     "$(at "$NIGHT" "Sa 02:00")"
expect "Mitternacht mit Tag: Sa 00:00 in der Spanne"      "ZEITPLAN Sa 00:00: 0% — 0 von 16 Engine(s)"     "$(at "$NIGHT" "Sa 00:00")"
expect "Mitternacht mit Tag: Sa 06:00 nicht mehr"         "ZEITPLAN Sa 06:00: 100% — 16 von 16 Engine(s)"  "$(at "$NIGHT" "Sa 06:00")"
expect "Mitternacht mit Tag: Fr 21:59 noch nicht"         "ZEITPLAN Fr 21:59: 100% — 16 von 16 Engine(s)"  "$(at "$NIGHT" "Fr 21:59")"
expect "Mitternacht mit Tag: Fr 22:00 beginnt"            "ZEITPLAN Fr 22:00: 0% — 0 von 16 Engine(s)"     "$(at "$NIGHT" "Fr 22:00")"
expect "Mitternacht mit Tag: Fr 02:00 gehoert zum Do"     "ZEITPLAN Fr 02:00: 100% — 16 von 16 Engine(s)"  "$(at "$NIGHT" "Fr 02:00")"
expect "Mitternacht mit Tag: So->Mo, Mo 03:00"            "ZEITPLAN Mo 03:00: 0% — 0 von 16 Engine(s)"     "$(at "So 22:00-06:00 0%" "Mo 03:00")"
expect "Mitternacht mit Tag: So 03:00 gehoert zum Sa"     "ZEITPLAN So 03:00: 100% — 16 von 16 Engine(s)"  "$(at "So 22:00-06:00 0%" "So 03:00")"
expect "Mitternacht mit Bereich: Fr-So, Mo 01:00"         "ZEITPLAN Mo 01:00: 0% — 0 von 16 Engine(s)"     "$(at "Fr-So 23:00-07:00 0%" "Mo 01:00")"
expect "ohne Ueberlauf unveraendert: Sa 02:00"            "ZEITPLAN Sa 02:00: 0% — 0 von 16 Engine(s)"     "$(at "Sa 00:00-06:00 0%" "Sa 02:00")"
expect "ohne Ueberlauf unveraendert: So 02:00"            "ZEITPLAN So 02:00: 100% — 16 von 16 Engine(s)"  "$(at "Sa 00:00-06:00 0%" "So 02:00")"
expect "Stern ueber Mitternacht unveraendert"             "ZEITPLAN Mo 03:00: 0% — 0 von 16 Engine(s)"     "$(at "* 22:00-06:00 0%" "Mo 03:00")"
expect "englische Tagesnamen"              "ZEITPLAN Mo 09:00: 50% — 9 von 16 Engine(s)"   "$(at "mon-fri 08:00-17:00 50%" "Mo 09:00")"
expect "eine Engine, 50 % laesst sie an"   "ZEITPLAN Mo 09:00: 50% — 1 von 1 Engine(s)"    "$(at "* 08:00-17:00 50%" "Mo 09:00" 0)"
expect "eine Engine, 0 % schaltet sie ab"  "ZEITPLAN Mo 09:00: 0% — 0 von 1 Engine(s)"     "$(at "* 08:00-17:00 0%" "Mo 09:00" 0)"

# Scope: gilt der Anteil nur dem Hintergrund (Live bleibt an) oder allen zusammen?
at_scope() {   # at_scope <scope> "<Zeitplan>" "<Tag HH:MM>"
    env -i PATH="$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" ENGINE_BACKGROUND_COUNT=15 \
        ENGINE_SCHEDULE_SCOPE="$1" ENGINE_SCHEDULE="$2" ENGINE_SCHEDULE_AT="$3" bash "$ROOT/entrypoint.sh" 2>&1 | tail -1
}
expect "Scope background: Live + Anteil"   "ZEITPLAN Mo 09:00: 25% — 5 von 16 Engine(s)"  "$(at_scope background "* 25%" "Mo 09:00")"
expect "Scope all: Anteil von allen"       "ZEITPLAN Mo 09:00: 25% — 4 von 16 Engine(s)"  "$(at_scope all "* 25%" "Mo 09:00")"
expect "Scope all: 1 % laesst eine laufen" "ZEITPLAN Mo 09:00: 1% — 1 von 16 Engine(s)"   "$(at_scope all "* 1%" "Mo 09:00")"
expect "Scope all: 0 % haelt alles an"     "ZEITPLAN Mo 09:00: 0% — 0 von 16 Engine(s)"   "$(at_scope all "* 0%" "Mo 09:00")"
at_scope sonstwas "* 25%" "Mo 09:00" | grep -q '^FEHLER:' && ok "Scope: Unsinn abgelehnt" || fail "Scope: Unsinn durchgelassen"

# Kaputte Regeln muessen BEIM START auffallen, nicht erst, wenn sie nachts greifen wuerden.
bad() { at "$1" "Mo 09:00" | grep -q '^FEHLER:' && ok "abgelehnt: $1" || fail "durchgelassen: $1"; }
bad "Mo-Do 08:00-17:00"
bad "Xy 08:00-17:00 50%"
bad "Mo 8-17 50%"
bad "Mo 08:00-17:00 150%"
bad "Mo 25:00-26:00 50%"

# ---------------------------------------------------------------- Meldung an RookHub (Dry-Run)
dry() {   # dry <Zeitplan> [weitere VAR=Wert …] — gibt die Zeile ZEITPLAN-MELDUNG aus
    local plan="$1"; shift
    env -i PATH="$PATH" ROOKHUB_URL=https://rh.example ROOKHUB_API_TOKEN=rkh_x ENGINE_PATH="$fake_engine" \
        ENTRYPOINT_DRY_RUN=1 ENGINE_PRIMARY_NAME="PC" ENGINE_BACKGROUND_NAME="PC Hintergrund" ENGINE_BACKGROUND_COUNT=2 \
        ENGINE_SCHEDULE="$plan" "$@" bash "$ROOT/entrypoint.sh" 2>&1 | grep '^ZEITPLAN-MELDUNG: ' | sed 's/^ZEITPLAN-MELDUNG: //'
}
json=$(dry "Mo-Do 08:00-17:00 0%" ENGINE_SCHEDULE_SCOPE=all)
check_json() {   # check_json "<Beschreibung>" "<Python-Ausdruck ueber d>"
    if printf '%s' "$json" | python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null
    then ok "$1"; else fail "$1 — Meldung war: $json"; fi
}
check_json "Meldung: Regel, Scope, Zone"        'd["rule"]=="Mo-Do 08:00-17:00 0%" and d["scope"]=="all" and d["timeZone"]=="Europe/Vienna"'
check_json "Meldung: Engines mit Platz"         '[(e["name"],e["slot"]) for e in d["engines"]]==[("PC",1),("PC Hintergrund",2),("PC Hintergrund 2",3)]'
json=$(dry "")
check_json "ohne Zeitplan: leere Regel (zuruecknehmen)" 'd["rule"]==""'
json=$(dry "* 50%" TZ=America/New_York)
check_json "TZ aus der .env geht mit"            'd["timeZone"]=="America/New_York"'
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" ENGINE_SCHEDULE="* 50%" TZ=Mond/Krater \
    ENGINE_SCHEDULE_AT="Mo 09:00" bash "$ROOT/entrypoint.sh" 2>&1 | grep -q '^FEHLER: Zeitzone' \
    && ok "unbekannte Zeitzone abgelehnt" || fail "unbekannte Zeitzone durchgelassen"
lichess_out=$(env -i PATH="$PATH" LICHESS_API_TOKEN=lip_x ENGINE_PATH="$fake_engine" ENTRYPOINT_DRY_RUN=1 \
    ENGINE_SCHEDULE="* 50%" bash "$ROOT/entrypoint.sh" 2>&1)
printf '%s' "$lichess_out" | grep -q '^ZEITPLAN-MELDUNG' && fail "ueber Lichess darf nichts gemeldet werden" || ok "ueber Lichess keine Meldung"

# ---------------------------------------------------------------- Aufsicht mit Stub-Providern
# Stub-„python": provider.py meldet seinen Start und bleibt dann liegen.
cat > "$work/python" << 'PY'
#!/bin/bash
case "$1" in
  */preflight.py) exit 0 ;;
  */provider.py)
      name=""; while [ $# -gt 0 ]; do [ "$1" = "--name" ] && name="$2"; shift; done
      echo "$name" >> __STARTS__
      exec sleep 60 ;;
  # Alles andere (z. B. `python -c` fuer die Zeitplan-Meldung) an das echte Python weiterreichen.
  *) exec python3 "$@" ;;
esac
PY
sed -i "s#__STARTS__#$work/starts#" "$work/python"
chmod +x "$work/python"
cp "$ROOT/entrypoint.sh" "$work/entrypoint.sh"
mkdir -p "$work/opt"; : > "$work/opt/provider.py"; : > "$work/opt/preflight.py"
sed -i "s#/opt/preflight.py#$work/opt/preflight.py#; s#/opt/provider.py#$work/opt/provider.py#" "$work/entrypoint.sh"

# Stub-„date": die Aufsicht fragt die Uhr (`date +%u/+%H/+%M`). Mit SCHEDULE_NOW antwortet sie fuer
# diesen festen Zeitpunkt (GNU `date -d`, in der Zeitzone, die das Skript setzt), ohne die echte.
# Frueher rechnete der Test den Wochentag mit der Uhr des Runners (UTC) aus, das Skript aber in
# Europe/Vienna — zwischen 22:00/23:00 UTC und Mitternacht lag Wien schon auf dem naechsten Tag,
# die Regel griff nicht, und 0 %/50 % liefen mit allen vier Engines (CI 24.10.2026, 23:29 UTC).
real_date=$(command -v date)
cat > "$work/date" << DATE
#!/bin/bash
if [ -n "\${SCHEDULE_NOW:-}" ]; then exec "$real_date" -d "\$SCHEDULE_NOW" "\$@"; fi
exec "$real_date" "\$@"
DATE
chmod +x "$work/date"

# Feste Zeitpunkte (Wiener Ortszeit bzw. UTC), damit der Test zu jeder Tages- und Nachtzeit dasselbe prueft.
FR_10="2026-10-23T10:00:00+02:00"    # Freitag 10:00 in Wien
NACHT="2026-10-24T23:29:00Z"         # Samstag 23:29 UTC = Sonntag 01:29 in Wien (der CI-Fehlschlag)
MI_2330="2026-10-21T23:30:00+02:00"  # Mittwoch 23:30 in Wien
DO_0030="2026-10-22T00:30:00+02:00"  # Donnerstag 00:30 in Wien

run_plan() {   # run_plan "<Zeitplan>" "<Sekunden>" "<SCHEDULE_NOW>"; Ausgabe: Zahl der gestarteten Provider
    : > "$work/starts"
    # setsid: die Aufsicht bekommt eine EIGENE Prozessgruppe, und am Ende geht die ganze Gruppe weg.
    # Ein `pkill -f sleep` traefe sonst auch Prozesse ausserhalb des Tests — und den Test selbst.
    ( cd "$work" && exec setsid env -i PATH="$work:$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" \
        ENGINE_NAME=T ENGINE_BACKGROUND_COUNT=3 PROVIDER_START_DELAY=0 SCHEDULE_TICK=1 SCHEDULE_NOW="$3" \
        ENGINE_SCHEDULE="$1" bash "$work/entrypoint.sh" > "$work/log" 2>&1 ) &
    local sup=$!
    sleep "$2"
    kill -TERM -"$sup" 2>/dev/null || kill -TERM "$sup" 2>/dev/null
    wait "$sup" 2>/dev/null
    wc -l < "$work/starts" | tr -d ' '
}

# Stub-„curl": haelt Methode, Adresse und Rumpf fest und antwortet 200.
cat > "$work/curl" << 'CURL'
#!/bin/bash
method=""; url=""; data=""
while [ $# -gt 0 ]; do
  case "$1" in -X) method="$2"; shift ;; --data) data="$2"; shift ;; -o|-w|-m|-H) shift ;; http*) url="$1" ;; esac
  shift
done
printf '%s %s %s\n' "$method" "$url" "$data" >> __REPORTS__
printf '200'
CURL
sed -i "s#__REPORTS__#$work/reports#" "$work/curl"; chmod +x "$work/curl"; : > "$work/reports"
( cd "$work" && exec setsid env -i PATH="$work:$PATH" ROOKHUB_URL=https://rh.example ROOKHUB_API_TOKEN=rkh_x \
    ENGINE_PATH="$fake_engine" ENGINE_NAME=T ENGINE_BACKGROUND_COUNT=1 PROVIDER_START_DELAY=0 SCHEDULE_TICK=1 \
    SCHEDULE_NOW="$FR_10" ENGINE_SCHEDULE="Fr 08:00-17:00 100%" bash "$work/entrypoint.sh" > "$work/log" 2>&1 ) &
sup=$!; sleep 3; kill -TERM -"$sup" 2>/dev/null || kill -TERM "$sup" 2>/dev/null; wait "$sup" 2>/dev/null
if grep -q '^PUT https://rh.example/api/external-engine/schedule {' "$work/reports"; then ok "Zeitplan per PUT an RookHub gemeldet"
else fail "keine Meldung an RookHub — gesehen: $(cat "$work/reports")"; fi
grep -q 'Zeitplan aktiv (Europe/Vienna)' "$work/log" && ok "Container rechnet in Europe/Vienna statt UTC" \
    || fail "Zeitzone nicht gesetzt — Log: $(head -3 "$work/log")"
rm -f "$work/curl"

# Jede Regel hat einen Gegenwert ausserhalb der Spanne — ein Treffer ist also nie Zufall der Vorgabe (100 %).
expect "100 %: alle vier Engines laufen" "4" "$(run_plan "Fr 08:00-17:00 100%; * 0%" 3 "$FR_10")"
expect "0 %: keine einzige laeuft"       "0" "$(run_plan "Fr 08:00-17:00 0%" 3 "$FR_10")"
expect "50 %: Live + zwei Hintergrund"   "3" "$(run_plan "Fr 08:00-17:00 50%; * 0%" 3 "$FR_10")"
# 23:29 UTC am Samstag ist in Wien schon Sonntag: die Sonntagsregel gilt, die Samstagsregel nicht mehr.
expect "Wiener Tag statt UTC-Tag (So-Regel greift)"   "0" "$(run_plan "So 00:00-06:00 0%" 3 "$NACHT")"
expect "Wiener Tag statt UTC-Tag (Sa-Regel vorbei)"   "4" "$(run_plan "Sa 00:00-24:00 0%" 3 "$NACHT")"
# Spanne ueber Mitternacht: vor und nach 00:00 dieselbe Regel.
expect "ueber Mitternacht: 23:30 in der Spanne" "3" "$(run_plan "* 22:00-06:00 50%; * 0%" 3 "$MI_2330")"
expect "ueber Mitternacht: 00:30 in der Spanne" "3" "$(run_plan "* 22:00-06:00 50%; * 0%" 3 "$DO_0030")"

echo
if [ "$fails" -eq 0 ]; then echo "ALLE TESTS OK"; else echo "$fails FEHLER"; fi
exit $((fails > 0))
