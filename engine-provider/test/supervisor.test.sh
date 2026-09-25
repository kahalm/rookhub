#!/bin/bash
# Aufruf: bash test/supervisor.test.sh  — Exit 0 = alles gut (ergaenzt entrypoint.test.sh,
# der nur den Dry-Run/Argumentaufbau prueft und den Fehlerpfad deshalb nicht sehen kann).
# Prueft den ECHTEN Fehlerpfad des Multi-Engine-Supervisors (nicht den Dry-Run): stirbt ein
# Provider mit Exit != 0, muss der Entrypoint die Meldung MIT dem richtigen Code ausgeben, die
# Geschwister beenden und selbst != 0 zurueckgeben — sonst startet `restart: unless-stopped` nicht neu.
set -u
# ROOT zuerst und ABSOLUT bestimmen — nach einem `cd` zeigt $0 ins Leere.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
fails=0

# Stub-„python": provider.py = langlebiger Prozess, der auf Wunsch mit einem Code stirbt.
cat > "$work/python" << 'PY'
#!/bin/bash
# $1 = Skriptpfad (preflight.py oder provider.py), Rest = Argumente
case "$1" in
  */preflight.py) exit 0 ;;
  */provider.py)
      # Startzeit festhalten — daran haengt die Pruefung der gestaffelten Starts.
      date +%s.%N >> __STARTS__
      # Die Engine, deren Name auf "1" endet, stirbt nach 2 s mit 3; die andere laeuft weiter.
      # Nicht sofort: der Supervisor staffelt die Starts, und stuerbe Engine 1 waehrend der Pause,
      # kaeme `wait -n` gleich nach dem Start von Engine 2 zurueck und killte sie, bevor ihr Stub
      # die Startzeit geschrieben hat.
      for a in "$@"; do case "$a" in *" 1"|*1) die=1 ;; esac; done
      if [ "${die:-0}" = 1 ]; then sleep 2; exit 3; fi
      # `exec`: der Stub IST dann der sleep. Als Kindprozess ueberlebte der sleep das kill des
      # Supervisors, hielt die Ausgabe-Pipe offen und der Test wartete 30 s auf ihn.
      exec sleep 30 ;;
esac
PY
sed -i "s#__STARTS__#$work/starts#" "$work/python"
chmod +x "$work/python"
fake_engine="$work/stockfish"; printf '#!/bin/sh\n' > "$fake_engine"; chmod +x "$fake_engine"
cp "$ROOT/entrypoint.sh" "$work/entrypoint.sh"
mkdir -p "$work/opt"; cp "$ROOT/preflight.py" "$work/opt/" 2>/dev/null || true
sed -i "s#/opt/preflight.py#$work/opt/preflight.py#; s#/opt/provider.py#$work/opt/provider.py#" "$work/entrypoint.sh"
: > "$work/opt/provider.py"

out=$(cd "$work" && PATH="$work:$PATH" env -i PATH="$work:$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" \
      ENGINE_COUNT=2 ENGINE_1_NAME="Engine 1" ENGINE_2_NAME="Engine 2" PROVIDER_START_DELAY=1 bash "$work/entrypoint.sh" 2>&1)
code=$?

if [ "$code" -ne 0 ]; then echo "ok   Exit-Code != 0 ($code)"; else echo "FAIL Exit 0 — Container startet nicht neu"; fails=$((fails+1)); fi
if grep -q "Exit 3" <<< "$out"; then echo "ok   Meldung nennt den echten Code (3)"; else echo "FAIL Meldung fehlt/falscher Code:"; echo "$out" | tail -3; fails=$((fails+1)); fi
# GESTAFFELTE STARTS: der zweite Provider darf erst PROVIDER_START_DELAY (hier 1 s) nach dem ersten
# starten — 13 gleichzeitige Registrierungen loesten am 2026-09-11 die IP-Sperre durch den
# DDoS-Schutz von Lichess aus. Engine 1 stirbt sofort; der Supervisor muss trotzdem erst die
# Pause abwarten, Engine 2 starten und DANN mit dem Code von Engine 1 enden.
if [ "$(wc -l < "$work/starts")" -eq 2 ]; then echo "ok   beide Provider gestartet"; else echo "FAIL Startliste: $(cat "$work/starts")"; fails=$((fails+1)); fi
gap=$(awk 'NR==1{a=$1} NR==2{printf "%.2f", $1-a}' "$work/starts")
if awk "BEGIN { exit !(${gap:-0} >= 0.9) }"; then echo "ok   Starts gestaffelt (${gap} s)"; else echo "FAIL Starts nicht gestaffelt (${gap:-?} s)"; fails=$((fails+1)); fi
exit $fails
