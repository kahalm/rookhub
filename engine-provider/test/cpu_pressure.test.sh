#!/bin/bash
# Aufruf: bash test/cpu_pressure.test.sh  — Exit 0 = alles gut.
#
# Prueft die Aufsicht im entrypoint.sh mit ECHTEN Stub-Providern und dem echten cpu_pressure.py: der CPU-Druck
# kommt aus Fixture-Dateien (CPU_PRESSURE_ROOT), die der Test waehrend des Laufs umschreibt. Gesehen werden soll:
# unter Druck gehen die Hintergrund-Engines von HINTEN weg (hoechste Nummer zuerst) bis zur Untergrenze, die
# Live-Engine bleibt, die Engine unter einem beendeten Provider stirbt mit, und bei Ruhe kommen alle zurueck.
# Die Entscheidungsregeln selbst (Schwellen, Zaehler, Abkuehlzeit) prueft test/cpu_pressure.test.py.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
fails=0
ok()   { echo "ok   $1"; }
fail() { echo "FAIL $1"; fails=$((fails + 1)); }

fake_engine="$work/stockfish"; printf '#!/bin/sh\n' > "$fake_engine"; chmod +x "$fake_engine"

# Stub-„python": provider.py startet eine „Engine" (sleep) als Kind und wartet auf sie — wie der echte Provider
# mit Stockfish. Ein TERM beendet nur den Stub; die Engine bliebe als Waise liegen, wenn die Aufsicht sie nicht
# ausdruecklich mitnimmt. Alles andere (cpu_pressure.py) geht an das echte Python.
cat > "$work/python" << 'PY'
#!/bin/bash
case "$1" in
  */preflight.py) exit 0 ;;
  */provider.py)
      name=""; while [ $# -gt 0 ]; do [ "$1" = "--name" ] && name="$2"; shift; done
      sleep 300 &
      echo "$! $name" >> __STARTS__
      wait ;;
  *) exec python3 "$@" ;;
esac
PY
sed -i "s#__STARTS__#$work/starts#" "$work/python"; chmod +x "$work/python"
cp "$ROOT/entrypoint.sh" "$work/entrypoint.sh"
mkdir -p "$work/opt"; : > "$work/opt/provider.py"; : > "$work/opt/preflight.py"
sed -i "s#/opt/preflight.py#$work/opt/preflight.py#; s#/opt/provider.py#$work/opt/provider.py#; s#/opt/cpu_pressure.py#$ROOT/cpu_pressure.py#g" "$work/entrypoint.sh"

fx="$work/fx"; mkdir -p "$fx/proc/pressure"
echo "8" > "$fx/nproc"
echo "1.00 1.00 1.00 1/100 1" > "$fx/proc/loadavg"
psi() { printf 'some avg10=%s avg60=%s avg300=0.00 total=1\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n' "$1" "$1" > "$fx/proc/pressure/cpu"; }

# Laufende Provider zaehlen: jeder Stub ist ein `bash …/python …/provider.py`-Prozess unserer Aufsicht.
count_providers() { pgrep -f "$work/python $work/opt/provider.py" -s "$sup" 2>/dev/null | wc -l | tr -d ' '; }
wait_for() {   # wait_for <zahl> <sekunden> — bis so viele Provider laufen
    local want="$1" n=0
    for ((n = 0; n < $2 * 5; n++)); do
        [ "$(count_providers)" = "$want" ] && return 0
        sleep 0.2
    done
    return 1
}

psi 60.00
: > "$work/starts"
( cd "$work" && exec setsid env -i PATH="$work:$PATH" CPU_PRESSURE_ROOT="$fx" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" \
    ENGINE_NAME=T ENGINE_BACKGROUND_COUNT=3 PROVIDER_START_DELAY=0 \
    ENGINE_CPU_PRESSURE_INTERVAL=1 ENGINE_CPU_PRESSURE_COOLDOWN=0 ENGINE_CPU_PRESSURE_LOW_COUNT=2 \
    bash "$work/entrypoint.sh" > "$work/log" 2>&1 ) &
sup=$!
sleep 0.5
sup=$(pgrep -f "bash $work/entrypoint.sh" | head -1)

if wait_for 1 15; then ok "Druck: von 4 auf nur die Live-Engine gesenkt (Vorgabe MIN=0)"; else fail "Druck: es laufen $(count_providers) statt 1"; fi
sleep 2
[ "$(count_providers)" = 1 ] && ok "die Live-Engine bleibt" || fail "Live-Engine weg? $(count_providers) Provider"
grep -q "beende 'T'" "$work/log" && fail "Live-Engine beendet" || ok "Live-Engine nie beendet"
order=$(grep -o "CPU-Druck: beende '[^']*'" "$work/log" | sed "s/CPU-Druck: beende //" | tr '\n' '|')
[ "$order" = "'T Hintergrund 3'|'T Hintergrund 2'|'T Hintergrund'|" ] && ok "hoechste Nummer zuerst beendet" || fail "Reihenfolge: $order"
grep -q "^CPU-Druck: PSI some avg60 60.0 % .*Load 1.00 / 8.0 Kerne = 0.12 — senke auf 3 von 4" "$work/log" \
    && ok "Logzeile nennt PSI und Load" || fail "Logzeile fehlt: $(grep CPU-Druck "$work/log" | head -3)"
grep -q "^CPU-Druck aktiv:" "$work/log" && ok "Startzeile 'CPU-Druck aktiv'" || fail "Startzeile fehlt"
# Die „Engines" (sleep-Kinder) der beendeten Provider muessen weg sein.
orphans=0
while read -r pid name; do
    case "$name" in T\ Hintergrund*) kill -0 "$pid" 2>/dev/null && orphans=$((orphans + 1)) ;; esac
done < "$work/starts"
[ "$(wc -l < "$work/starts")" -ge 4 ] && [ "$orphans" -eq 0 ] && ok "Engine unter beendetem Provider mitbeendet" || fail "$orphans Engine(s) rechnen als Waise weiter"

psi 2.00
if wait_for 4 20; then ok "Ruhe: alle vier Engines wieder da"; else fail "Ruhe: es laufen $(count_providers) statt 4"; fi
grep -c " T Hintergrund 3$" "$work/starts" | grep -q '^2$' && ok "derselbe Name kommt zurueck (stabile Registrierung)" \
    || fail "Startliste: $(cut -d' ' -f2- "$work/starts" | tr '\n' '|')"

kill -TERM -"$sup" 2>/dev/null || kill -TERM "$sup" 2>/dev/null
sleep 1
pkill -TERM -s "$sup" 2>/dev/null || true

# ENGINE_CPU_PRESSURE=off: bleibt beim bisherigen Weg (alle starten, kein Messen).
out=$(cd "$work" && env -i PATH="$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" ENGINE_CPU_PRESSURE=an \
      ENTRYPOINT_DRY_RUN=1 ENGINE_BACKGROUND_COUNT=2 bash "$work/entrypoint.sh" 2>&1)
grep -q "^FEHLER: ENGINE_CPU_PRESSURE muss 'auto' oder 'off'" <<< "$out" && ok "Unsinn in ENGINE_CPU_PRESSURE abgelehnt" || fail "Unsinn durchgelassen: $out"
out=$(cd "$work" && env -i PATH="$work:$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" ENGINE_CPU_PRESSURE_MIN=-1 \
      ENGINE_BACKGROUND_COUNT=2 timeout 10 bash "$work/entrypoint.sh" 2>&1); code=$?
[ "$code" -eq 1 ] && grep -q "^FEHLER: ENGINE_CPU_PRESSURE_MIN" <<< "$out" && ok "kaputte Schwelle beim Start abgelehnt" \
    || fail "kaputte Schwelle: Exit $code, $out"

echo
if [ "$fails" -eq 0 ]; then echo "ALLE TESTS OK"; else echo "$fails FEHLER"; fi
exit $((fails > 0))
