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
      # Die Engine, deren Name auf "1" endet, stirbt sofort mit 3; die andere laeuft weiter.
      for a in "$@"; do case "$a" in *" 1"|*1) die=1 ;; esac; done
      if [ "${die:-0}" = 1 ]; then exit 3; fi
      sleep 30 ;;
esac
PY
chmod +x "$work/python"
fake_engine="$work/stockfish"; printf '#!/bin/sh\n' > "$fake_engine"; chmod +x "$fake_engine"
cp "$ROOT/entrypoint.sh" "$work/entrypoint.sh"
mkdir -p "$work/opt"; cp "$ROOT/preflight.py" "$work/opt/" 2>/dev/null || true
sed -i "s#/opt/preflight.py#$work/opt/preflight.py#; s#/opt/provider.py#$work/opt/provider.py#" "$work/entrypoint.sh"
: > "$work/opt/provider.py"

out=$(cd "$work" && PATH="$work:$PATH" env -i PATH="$work:$PATH" LICHESS_API_TOKEN=x ENGINE_PATH="$fake_engine" \
      ENGINE_COUNT=2 ENGINE_1_NAME="Engine 1" ENGINE_2_NAME="Engine 2" bash "$work/entrypoint.sh" 2>&1)
code=$?

if [ "$code" -ne 0 ]; then echo "ok   Exit-Code != 0 ($code)"; else echo "FAIL Exit 0 — Container startet nicht neu"; fails=$((fails+1)); fi
if grep -q "Exit 3" <<< "$out"; then echo "ok   Meldung nennt den echten Code (3)"; else echo "FAIL Meldung fehlt/falscher Code:"; echo "$out" | tail -3; fails=$((fails+1)); fi
exit $fails
