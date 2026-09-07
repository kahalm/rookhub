#!/usr/bin/env bash
###############################################################################
# Test fuer scripts/directory-runs.sh — laeuft komplett gegen ein Fake-`curl`
# (keine API noetig). Prueft:
#   1. Die REIHENFOLGE: erst Sweep, dann FIDE-Details, dann Rundenplaene.
#      Kurz vor lang, damit man frueh sieht, ob etwas grundsaetzlich klemmt.
#   2. Die langen Laeufe wiederholen sich, BIS `checked` 0 meldet — und hoeren
#      dann auf. Das ist die dokumentierte Abbruchbedingung („checked" zaehlt
#      die VERSUCHTEN), und ohne sie bliebe der halbe Rueckstand liegen.
#   3. `retryEmpty=true` steht am Rundenplan-Aufruf. Ohne den Schalter nimmt der
#      Endpunkt die als geprueft vermerkten Eintraege nie wieder vor — genau die,
#      um die es nach dem Parser-Fix geht.
#   4. Das PASSWORT steht in keiner Kommandozeile (es waere in /proc/<pid>/cmdline
#      fuer jeden Nutzer des Rechners lesbar, solange der Aufruf laeuft).
#   5. FEHLERFALL: ein gescheiterter Lauf beendet das Skript mit Exit != 0.
#   6. Der DECKEL greift: meldet der Endpunkt endlos dieselbe Zahl, bricht das
#      Skript nach MAX_ROUNDS ab statt ewig zu laufen.
#
#   ./scripts/tests/test_directory_runs.sh                 # testet scripts/directory-runs.sh
#   ./scripts/tests/test_directory_runs.sh /pfad/zu/alt.sh # beliebige Version testen
###############################################################################
set -u

here="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="${1:-$here/../directory-runs.sh}"
[ -r "$SCRIPT" ] || { echo "FAIL: $SCRIPT nicht lesbar"; exit 1; }

sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
fails=0

check() {
  if [ "$2" = "ok" ]; then printf '  ok   %s\n' "$1"
  else printf '  FAIL %s\n' "$1"; fails=$((fails + 1)); fi
}

# --- Fake-curl: schreibt jeden Aufruf mit und antwortet je nach Pfad ---------
make_curl() {
  cat > "$sandbox/bin/curl" <<'FAKE'
#!/usr/bin/env bash
# Ruf mitschreiben (ALLE Argumente, damit der Passwort-Test etwas zu pruefen hat)
printf '%s\n' "$*" >> "$CALLS"

url=""
for a in "$@"; do case "$a" in http*) url="$a";; esac; done

case "$url" in
  *"/api/auth/login")      echo '{"token":"tok"}' ;;
  *"/sweep")               echo '{"federation":"AUT","rows":104}' ;;
  *"/fide-details"*)
      n=$(cat "$STATE/fide" 2>/dev/null || echo 0); n=$((n+1)); echo "$n" > "$STATE/fide"
      # Zwei volle Durchgaenge, dann leer.
      if [ "$n" -le 2 ]; then echo '{"checked":200,"withDetails":180,"geocoded":90}'
      else echo '{"checked":0,"withDetails":0,"geocoded":0}'; fi ;;
  *"/round-plans"*)
      n=$(cat "$STATE/plans" 2>/dev/null || echo 0); n=$((n+1)); echo "$n" > "$STATE/plans"
      if [ "$n" -le 1 ]; then echo '{"checked":200,"withPlan":41,"failed":0}'
      else echo '{"checked":0,"withPlan":0,"failed":0}'; fi ;;
  *) echo '{}' ;;
esac
FAKE
  chmod +x "$sandbox/bin/curl"
}

# CALLS/STATE gehoeren in die AEUSSERE Shell: `run_script` wird per $( ) aufgerufen und laeuft
# damit in einer Subshell — dort gesetzte Variablen sind hinterher weg, und die Pruefungen unten
# saehen eine leere Mitschrift.
export CALLS="$sandbox/calls.txt"
export STATE="$sandbox/state"

run_script() {
  rm -rf "$sandbox/bin" "$sandbox/state"; mkdir -p "$sandbox/bin" "$sandbox/state"
  : > "$CALLS"
  make_curl
  "$@" 2>&1
}

# ---------------------------------------------------------------------------
echo "== Normalfall"
out=$(printf 'admin\ngeheim123\n' | PATH="$sandbox/bin:$PATH" \
      run_script bash "$SCRIPT" http://api.test AUT 200)
rc=$?

grep -q "Alle Laeufe durch." <<<"$out" && check "laeuft durch" ok || { check "laeuft durch" no; echo "$out"; }
[ "$rc" -eq 0 ] && check "Exit 0" ok || check "Exit 0" no

# 1. Reihenfolge
order=$(grep -o -e '/sweep' -e '/fide-details' -e '/round-plans' "$CALLS" | uniq | tr '\n' ' ')
[ "$order" = "/sweep /fide-details /round-plans " ] \
  && check "Reihenfolge Sweep -> FIDE -> Rundenplaene" ok \
  || { check "Reihenfolge Sweep -> FIDE -> Rundenplaene" no; echo "     war: $order"; }

# 2. Wiederholung bis 0 — und dann Schluss
[ "$(grep -c '/fide-details' "$CALLS")" -eq 3 ] \
  && check "FIDE: 2 volle Durchgaenge + 1 leerer, dann Stopp" ok \
  || { check "FIDE: 2 volle + 1 leerer" no; echo "     war: $(grep -c '/fide-details' "$CALLS")"; }
[ "$(grep -c '/round-plans' "$CALLS")" -eq 2 ] \
  && check "Rundenplaene: 1 voller + 1 leerer, dann Stopp" ok \
  || { check "Rundenplaene: 1 voller + 1 leerer" no; echo "     war: $(grep -c '/round-plans' "$CALLS")"; }

# 3. retryEmpty
grep -q 'round-plans?limit=200&retryEmpty=true' "$CALLS" \
  && check "retryEmpty=true am Rundenplan-Aufruf" ok || check "retryEmpty=true am Rundenplan-Aufruf" no

# 4. Passwort NICHT in der Kommandozeile
grep -q 'geheim123' "$CALLS" \
  && { check "Passwort steht in keinem curl-Aufruf" no; echo "     gefunden in: $(grep -n geheim123 "$CALLS" | head -1)"; } \
  || check "Passwort steht in keinem curl-Aufruf" ok

# 5. Foederationen normalisiert
grep -q '"AUT"' "$CALLS" && check "Foederation im Rumpf" ok || check "Foederation im Rumpf" no

# ---------------------------------------------------------------------------
echo "== Sweep ausgelassen (leere Foederationsliste)"
out=$(printf 'admin\ngeheim123\n' | PATH="$sandbox/bin:$PATH" \
      run_script bash "$SCRIPT" http://api.test "" 200)
grep -q '/sweep' "$CALLS" \
  && check "kein Sweep ohne Foederationen" no || check "kein Sweep ohne Foederationen" ok
grep -q '/fide-details' "$CALLS" \
  && check "die uebrigen Laeufe trotzdem" ok || check "die uebrigen Laeufe trotzdem" no

# ---------------------------------------------------------------------------
echo "== Login schlaegt fehl"
rm -rf "$sandbox/bin"; mkdir -p "$sandbox/bin"
cat > "$sandbox/bin/curl" <<'FAKE'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS"
echo '{"message":"Unauthorized"}'
FAKE
chmod +x "$sandbox/bin/curl"
: > "$CALLS"
out=$(printf 'admin\nfalsch\n' | PATH="$sandbox/bin:$PATH" CALLS="$CALLS" STATE="$sandbox/state" \
      bash "$SCRIPT" http://api.test AUT 200 2>&1)
rc=$?
[ "$rc" -ne 0 ] && check "Exit != 0 bei falschem Passwort" ok || check "Exit != 0 bei falschem Passwort" no
grep -q '/sweep' "$CALLS" \
  && check "kein Lauf ohne gueltige Anmeldung" no || check "kein Lauf ohne gueltige Anmeldung" ok

# ---------------------------------------------------------------------------
echo "== Endloser Rueckstand laeuft in den Deckel"
rm -rf "$sandbox/bin" "$sandbox/state"; mkdir -p "$sandbox/bin" "$sandbox/state"
cat > "$sandbox/bin/curl" <<'FAKE'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS"
url=""; for a in "$@"; do case "$a" in http*) url="$a";; esac; done
case "$url" in
  *"/api/auth/login") echo '{"token":"tok"}' ;;
  *)                  echo '{"checked":200,"withDetails":1,"withPlan":1,"geocoded":0}' ;;
esac
FAKE
chmod +x "$sandbox/bin/curl"
: > "$CALLS"
out=$(printf 'admin\ngeheim123\n' | PATH="$sandbox/bin:$PATH" CALLS="$CALLS" STATE="$sandbox/state" \
      MAX_ROUNDS=3 bash "$SCRIPT" http://api.test "" 200 2>&1)
[ "$(grep -c '/fide-details' "$CALLS")" -eq 3 ] \
  && check "haelt bei MAX_ROUNDS an" ok \
  || { check "haelt bei MAX_ROUNDS an" no; echo "     war: $(grep -c '/fide-details' "$CALLS")"; }
grep -q 'Deckel von 3' <<<"$out" && check "sagt, dass noch etwas offen ist" ok \
  || { check "sagt, dass noch etwas offen ist" no; echo "$out" | tail -5; }

echo
[ "$fails" -eq 0 ] && { echo "ALLE TESTS OK"; exit 0; } || { echo "$fails FEHLER"; exit 1; }
