#!/bin/bash
# Baut den Aufruf des Lichess-Providers aus den Umgebungsvariablen (siehe .env.example),
# damit in compose.yml/`docker run` nichts als Kommandozeile gepflegt werden muss.
#
# ENGINE_COUNT>1 startet MEHRERE Provider (= mehrere bei Lichess registrierte Engines, je ein
# eigener Stockfish-Prozess) in diesem einen Container — z. B. „Server 1" für die Live-Analyse
# und „Server 2" als Hintergrund-Engine, die RookHub pausiert, sobald Live rechnet. Jede Engine
# kann Name/Threads/Hash per ENGINE_<i>_NAME / ENGINE_<i>_MAX_THREADS / ENGINE_<i>_MAX_HASH
# überschreiben; ohne Override gelten ENGINE_NAME (+ " <i>"), MAX_THREADS, MAX_HASH. Stirbt
# ein Provider, endet der ganze Container mit dessen Exit-Code, damit `restart:` alles sauber
# neu hochzieht (kein halb lebender Pool). bash statt sh: `wait -n` und indirekte Variablen.
#
# FALLE (bewusst if/fi statt `[ -n "$X" ] && set -- …`): unter `set -e` beendet ein
# fehlschlagender Test die letzte Zeile eines &&-Ausdrucks mit Exit-Code 1 — der Container
# stürbe dann kommentarlos, nur weil eine OPTIONALE Variable nicht gesetzt ist.
set -eu

if [ -z "${LICHESS_API_TOKEN:-}" ]; then
    echo "FEHLER: LICHESS_API_TOKEN ist nicht gesetzt." >&2
    echo "        Token mit den Scopes engine:read UND engine:write anlegen und in die .env eintragen:" >&2
    echo "        https://lichess.org/account/oauth/token/create?scopes[]=engine:read&scopes[]=engine:write" >&2
    exit 1
fi
export LICHESS_API_TOKEN

ENGINE_PATH="${ENGINE_PATH:-/opt/stockfish/stockfish}"
# `-f` ZUSÄTZLICH zu `-x`: Verzeichnisse tragen das Ausführbar-Bit, ein vergessener Dateiname
# (ENGINE_PATH=/engine statt /engine/stockfish — der wahrscheinlichste Vertipper beim Einhängen
# einer eigenen Engine) käme sonst an dieser Prüfung vorbei und stürbe erst später im Provider,
# mit Stacktrace und in der Neustart-Schleife.
if [ ! -f "$ENGINE_PATH" ] || [ ! -x "$ENGINE_PATH" ]; then
    echo "FEHLER: Keine ausführbare Engine-DATEI unter '$ENGINE_PATH'." >&2
    if [ -d "$ENGINE_PATH" ]; then
        echo "        Das ist ein Verzeichnis — ENGINE_PATH muss auf die Binärdatei selbst zeigen," >&2
        echo "        z. B. ENGINE_PATH=/engine/stockfish statt ENGINE_PATH=/engine." >&2
    else
        echo "        ENGINE_PATH prüfen (Volume eingehängt? Datei ausführbar: chmod +x)." >&2
    fi
    exit 1
fi

ENGINE_NAME="${ENGINE_NAME:-RookHub Engine}"

ENGINE_COUNT="${ENGINE_COUNT:-1}"
if ! [[ "$ENGINE_COUNT" =~ ^[0-9]+$ ]] || [ "$ENGINE_COUNT" -lt 1 ] || [ "$ENGINE_COUNT" -gt 16 ]; then
    echo "FEHLER: ENGINE_COUNT muss eine Zahl von 1 bis 16 sein (ist '$ENGINE_COUNT')." >&2
    exit 1
fi

# `--engine` ist für den Provider eine SHELL-Zeile (er startet sie mit `sh -c`), nicht ein
# fertiges Argument. Ein Pfad mit Leerzeichen („/engine/my stockfish") würde dort zerlegt.
# Deshalb hier in einfache Anführungszeichen fassen (enthaltene ' korrekt maskiert) — dass es
# wirklich eine ausführbare Datei ist, hat die Prüfung oben schon sichergestellt.
ENGINE_QUOTED="'$(printf '%s' "$ENGINE_PATH" | sed "s/'/'\\\\''/g")'"

# Einstellung der Engine $1: ENGINE_<i>_<Suffix>, sonst der gemeinsame Default $3.
engine_setting() {
    local var="ENGINE_$1_$2"
    printf '%s' "${!var:-$3}"
}

# Der Name ist die IDENTITÄT der Registrierung: der Provider aktualisiert beim Start den
# Eintrag GLEICHEN Namens, statt einen zweiten anzulegen. Stabil halten — und auf zwei
# Rechnern (oder für zwei Engines im selben Container) zwei verschiedene Namen verwenden,
# sonst überschreiben sie sich gegenseitig. Bei ENGINE_COUNT>1 heißt Engine i deshalb
# standardmäßig "<ENGINE_NAME> <i>".
engine_name() {
    local default="$ENGINE_NAME"
    [ "$ENGINE_COUNT" -gt 1 ] && default="$ENGINE_NAME $1"
    engine_setting "$1" NAME "$default"
}

# Der Provider liest seine Argumente mit argparse und `fromfile_prefix_chars='@'`: ein Name mit
# führendem @ würde als „lies die Argumente aus dieser DATEI" verstanden und bricht den Start ab.
check_name() {
    case "$1" in
        @*)
            echo "FEHLER: Engine-Name '$1' darf nicht mit '@' beginnen (der Provider liest das als Dateiverweis)." >&2
            echo "        Anderen Namen wählen, z. B. '${1#@}'." >&2
            exit 1
            ;;
    esac
}

# Argumentliste für Engine $1 in das globale Array ARGS legen.
build_args() {
    local i="$1" name threads hash
    name=$(engine_name "$i"); check_name "$name"
    threads=$(engine_setting "$i" MAX_THREADS "${MAX_THREADS:-}")
    hash=$(engine_setting "$i" MAX_HASH "${MAX_HASH:-}")
    ARGS=(--engine "exec $ENGINE_QUOTED" --name "$name")
    if [ -n "$threads" ];             then ARGS+=(--max-threads "$threads"); fi
    if [ -n "$hash" ];                then ARGS+=(--max-hash "$hash"); fi
    if [ -n "${KEEP_ALIVE:-}" ];  then ARGS+=(--keep-alive "$KEEP_ALIVE"); fi
    if [ -n "${LOG_LEVEL:-}" ];   then ARGS+=(--log-level "$LOG_LEVEL"); fi
    # Nur für einen späteren RookHub-EIGENEN Broker (Phase 2) nötig; leer = lichess.org.
    if [ -n "${LICHESS_URL:-}" ]; then ARGS+=(--lichess "$LICHESS_URL"); fi
    if [ -n "${BROKER_URL:-}" ];  then ARGS+=(--broker "$BROKER_URL"); fi
}

# Testmodus: nur die Aufrufe zeigen (eine Zeile je Engine), kein Preflight, kein Start.
if [ -n "${ENTRYPOINT_DRY_RUN:-}" ]; then
    for ((i = 1; i <= ENGINE_COUNT; i++)); do
        build_args "$i"
        printf 'DRY-RUN %d:' "$i"; printf ' %q' "${ARGS[@]}"; printf '\n'
    done
    exit 0
fi

# Token vorab prüfen, damit ein fehlender Scope als Klartext-Satz erscheint statt als
# 401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt.
python /opt/preflight.py

if [ "$ENGINE_COUNT" -eq 1 ]; then
    build_args 1
    echo "Starte Engine-Provider: $ENGINE_PATH als '$(engine_name 1)'"
    exec python /opt/provider.py "${ARGS[@]}"
fi

# Mehrere Engines: alle starten, beim ERSTEN Ausfall alle anderen beenden und mit dessen Code
# enden. Die Logzeilen der Provider laufen unpräfixiert zusammen (jeder meldet beim Start
# seinen Namen; LOG_LEVEL=debug zeigt je Job die Engine).
PIDS=()
trap 'kill "${PIDS[@]}" 2>/dev/null; wait; exit 143' TERM INT
for ((i = 1; i <= ENGINE_COUNT; i++)); do
    build_args "$i"
    echo "Starte Engine-Provider $i/$ENGINE_COUNT: $ENGINE_PATH als '$(engine_name "$i")'"
    python /opt/provider.py "${ARGS[@]}" &
    PIDS+=("$!")
done
wait -n "${PIDS[@]}"; code=$?
echo "FEHLER: Ein Engine-Provider hat sich beendet (Exit $code) — alle Engines werden neu gestartet." >&2
kill "${PIDS[@]}" 2>/dev/null; wait
exit "$code"
