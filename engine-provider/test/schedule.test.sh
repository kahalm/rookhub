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
esac
PY
sed -i "s#__STARTS__#$work/starts#" "$work/python"
chmod +x "$work/python"
cp "$ROOT/entrypoint.sh" "$work/entrypoint.sh"
mkdir -p "$work/opt"; : > "$work/opt/provider.py"; : > "$work/opt/preflight.py"
sed -i "s#/opt/preflight.py#$work/opt/preflight.py#; s#/opt/provider.py#$work/opt/provider.py#" "$work/entrypoint.sh"

# Regel, die GENAU JETZT gilt — der Test soll zu jeder Tages- und Nachtzeit dasselbe pruefen.
today=$(date +%u); case "$today" in 1) d=Mo;; 2) d=Di;; 3) d=Mi;; 4) d=Do;; 5) d=Fr;; 6) d=Sa;; 7) d=So;; esac

run_plan() {   # run_plan "<Prozentregel>" "<Sekunden>"; Ausgabe: Zahl der gestarteten Provider
    : > "$work/starts"
    # setsid: die Aufsicht bekommt eine EIGENE Prozessgruppe, und am Ende geht die ganze Gruppe weg.
    # Ein `pkill -f sleep` traefe sonst auch Prozesse ausserhalb des Tests — und den Test selbst.
    ( cd "$work" && exec setsid env -i PATH="$work:$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" \
        ENGINE_NAME=T ENGINE_BACKGROUND_COUNT=3 PROVIDER_START_DELAY=0 SCHEDULE_TICK=1 \
        ENGINE_SCHEDULE="$d 00:00-24:00 $1" bash "$work/entrypoint.sh" > "$work/log" 2>&1 ) &
    local sup=$!
    sleep "$2"
    kill -TERM -"$sup" 2>/dev/null || kill -TERM "$sup" 2>/dev/null
    wait "$sup" 2>/dev/null
    wc -l < "$work/starts" | tr -d ' '
}

expect "100 %: alle vier Engines laufen" "4" "$(run_plan "100%" 3)"
expect "0 %: keine einzige laeuft"       "0" "$(run_plan "0%" 3)"
expect "50 %: Live + zwei Hintergrund"   "3" "$(run_plan "50%" 3)"

echo
if [ "$fails" -eq 0 ]; then echo "ALLE TESTS OK"; else echo "$fails FEHLER"; fi
exit $((fails > 0))
