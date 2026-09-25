#!/usr/bin/env bash
# Vertragstest des EIGENEN Engine-Brokers mit dem ECHTEN Provider-Image (Paket 9 aus
# docs/eigener-engine-broker.md). Kein CI-Test — er baut den E2E-Stack und das Provider-Image
# (Stockfish-Download) und laeuft mehrere Minuten. Von Hand starten, wenn am Broker oder am
# Provider-Pin gedreht wurde:
#
#   engine-provider/test/rookhub-broker.e2e.sh            # 1 Live- + 12 Hintergrund-Engines, 3 min Last
#   BACKGROUND_COUNT=4 LOAD_SECONDS=60 engine-provider/test/rookhub-broker.e2e.sh
#
# Ablauf: E2E-Stack hoch (compose.e2e.yml, Projekt rookhub-e2e) → Konto + API-Token mit Bereich
# „Engine" ueber die API → Provider-Image bauen → Container gegen http://host.docker.internal:<FRONTEND_PORT>
# (DERSELBE Weg wie im Betrieb: nginx → API) → Messung (rookhub_broker_e2e.py) → Log-Pruefung →
# alles wieder abbauen.
#
# Lichess ist im Provider-Container bewusst UNERREICHBAR (lichess.org / engine.lichess.ovh zeigen auf
# 127.0.0.1): rechnet der Provider trotzdem, haengt am Weg nichts an Lichess.
#
# Der Token verlaesst die Arbeitsdateien (Modus 600, am Ende geloescht) nie — keine Ausgabe, kein Log.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
COMPOSE_FILE="$ROOT/compose.e2e.yml"
ENV_FILE="$ROOT/.env.e2e"
PROJECT="rookhub-e2e"
export API_PORT="${API_PORT:-15099}" FRONTEND_PORT="${FRONTEND_PORT:-18099}" DB_EXTERNAL_PORT="${DB_EXTERNAL_PORT:-13308}"
BASE="http://127.0.0.1:${FRONTEND_PORT}"
PROVIDER_IMAGE="${PROVIDER_IMAGE:-rookhub-broker-e2e-provider:local}"
PROVIDER_NAME="${PROVIDER_NAME:-rookhub-broker-e2e-provider}"
BACKGROUND_COUNT="${BACKGROUND_COUNT:-12}"
LOAD_SECONDS="${LOAD_SECONDS:-180}"
# Die Last ist eine Last fuer den BROKER (13 Provider pollen, laden hoch, wechseln Auftraege), nicht
# fuer die CPU — deshalb gedeckelt, damit der Rest des Rechners weiterlaeuft.
PROVIDER_CPUS="${PROVIDER_CPUS:-6}"
OUT_DIR="${OUT_DIR:-}"
KEEP_STACK="${KEEP_STACK:-0}"

WORK="$(mktemp -d)"
chmod 700 "$WORK"

compose() { docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" -p "$PROJECT" "$@"; }

cleanup() {
    local code=$?
    if [ -n "$OUT_DIR" ]; then
        mkdir -p "$OUT_DIR"
        docker logs "$PROVIDER_NAME" > "$OUT_DIR/provider.log" 2>&1 || true
        docker logs e2e-api > "$OUT_DIR/api.log" 2>&1 || true
        cp "$WORK/report.json" "$OUT_DIR/" 2>/dev/null || true
    fi
    docker rm -f "$PROVIDER_NAME" >/dev/null 2>&1 || true
    if [ "$KEEP_STACK" != "1" ]; then
        compose down -v --remove-orphans >/dev/null 2>&1 || true
    fi
    rm -rf "$WORK"
    exit "$code"
}
trap cleanup EXIT

python3 -c 'import chess' 2>/dev/null || { echo "FEHLER: python-chess fehlt (pip install chess)" >&2; exit 2; }

echo "==> E2E-Stack bauen und starten (Projekt $PROJECT, Frontend :$FRONTEND_PORT, API :$API_PORT)"
compose down -v --remove-orphans >/dev/null 2>&1 || true
compose up --build -d

echo "==> Warten auf API + Frontend"
for _ in $(seq 1 120); do
    if curl -sf "http://127.0.0.1:${API_PORT}/health" >/dev/null && curl -sf "$BASE/" >/dev/null; then break; fi
    sleep 2
done
curl -sf "http://127.0.0.1:${API_PORT}/health" >/dev/null || { echo "FEHLER: API nicht bereit" >&2; exit 1; }

echo "==> Konto + API-Token (Bereich engine)"
python3 "$HERE/rookhub_broker_e2e.py" setup --base "$BASE" --workdir "$WORK"

echo "==> Provider-Image bauen ($PROVIDER_IMAGE)"
docker build -q -t "$PROVIDER_IMAGE" "$ROOT/engine-provider" >/dev/null

# Env-Datei fuer den Container: der Token geht NUR ueber diese Datei (Modus 600) hinein.
{
    echo "ROOKHUB_URL=http://host.docker.internal:${FRONTEND_PORT}"
    printf 'ROOKHUB_API_TOKEN=%s\n' "$(cat "$WORK/engine-token")"
    echo "ENGINE_NAME=E2E Live"
    echo "ENGINE_PRIMARY_MAX_THREADS=2"
    echo "ENGINE_PRIMARY_MAX_HASH=128"
    echo "ENGINE_BACKGROUND_COUNT=${BACKGROUND_COUNT}"
    echo "ENGINE_BACKGROUND_NAME=E2E Hintergrund"
    echo "ENGINE_BACKGROUND_MAX_THREADS=1"
    echo "ENGINE_BACKGROUND_MAX_HASH=64"
    echo "PROVIDER_START_DELAY=0.5"
} > "$WORK/provider.env"
chmod 600 "$WORK/provider.env"

echo "==> Provider starten (1 Live + ${BACKGROUND_COUNT} Hintergrund, --cpus ${PROVIDER_CPUS}, Lichess gesperrt)"
docker rm -f "$PROVIDER_NAME" >/dev/null 2>&1 || true
docker run -d --name "$PROVIDER_NAME" --env-file "$WORK/provider.env" \
    --add-host host.docker.internal:host-gateway \
    --add-host lichess.org:127.0.0.1 --add-host engine.lichess.ovh:127.0.0.1 \
    --cpus "$PROVIDER_CPUS" "$PROVIDER_IMAGE" >/dev/null

python3 "$HERE/rookhub_broker_e2e.py" wait --base "$BASE" --workdir "$WORK" --count $((BACKGROUND_COUNT + 1))

echo "==> Messung (Live, dann ${LOAD_SECONDS} s Last)"
set +e
python3 "$HERE/rookhub_broker_e2e.py" measure --base "$BASE" --workdir "$WORK" --primary "E2E Live" \
    --load-seconds "$LOAD_SECONDS" > "$WORK/report.json"
MEASURE=$?
set -e
cat "$WORK/report.json"

echo "==> Logs pruefen"
FAIL=0
PLOG="$(docker logs "$PROVIDER_NAME" 2>&1)"
if [ "$(docker inspect -f '{{.State.Running}}' "$PROVIDER_NAME")" != "true" ]; then
    echo "FEHLER: Provider-Container laeuft nicht mehr"; FAIL=1
fi
if grep -E "ERROR|Traceback" <<<"$PLOG" >/dev/null; then
    echo "FEHLER: Provider-Log enthaelt ERROR/Traceback:"; grep -E -m5 "ERROR|Traceback" <<<"$PLOG"; FAIL=1
fi
ALOG="$(docker logs e2e-api 2>&1)"
count() { grep -c -E "$1" <<<"$ALOG" || true; }
UNAVAILABLE=$(count "EngineBroker: (ProviderTimeout|Schlange voll)|Broker antwortete 50[34]")
PROVIDER_GONE=$(count "EngineBroker: Upload ProviderGone")
COMPLETED=$(count "EngineBroker: Upload Completed")
WITHOUT_BESTMOVE=$(count "EngineBroker: Upload WithoutBestmove")
SKIPPED=$(count "EngineBroker: Zeile uebersprungen")
LICHESS=$(count "lichess\.org|lichess\.ovh")
echo "API-Log: Uploads mit bestmove=$COMPLETED, ohne bestmove=$WITHOUT_BESTMOVE, Provider weg=$PROVIDER_GONE," \
     "503/Timeout/Schlange voll=$UNAVAILABLE, uebersprungene Zeilen=$SKIPPED, Lichess-Erwaehnungen=$LICHESS"
[ "$UNAVAILABLE" -eq 0 ] || { echo "FEHLER: $UNAVAILABLE x 503/ProviderTimeout"; FAIL=1; }
[ "$PROVIDER_GONE" -eq 0 ] || { echo "FEHLER: $PROVIDER_GONE Uploads abgerissen"; FAIL=1; }
[ "$LICHESS" -eq 0 ] || { echo "FEHLER: die API spricht mit Lichess"; FAIL=1; }
[ "$COMPLETED" -gt 0 ] || { echo "FEHLER: kein einziger vollstaendiger Upload"; FAIL=1; }

if [ "$MEASURE" -ne 0 ] || [ "$FAIL" -ne 0 ]; then
    echo "==> FEHLGESCHLAGEN (Messung $MEASURE, Logs $FAIL)"
    exit 1
fi
echo "==> ALLES OK"
