#!/bin/sh
# Baut den Aufruf des Lichess-Providers aus den Umgebungsvariablen (siehe .env.example),
# damit in compose.yml/`docker run` nichts als Kommandozeile gepflegt werden muss.
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
# Der Provider liest seine Argumente mit argparse und `fromfile_prefix_chars='@'`: ein Name mit
# führendem @ würde als „lies die Argumente aus dieser DATEI" verstanden und bricht den Start ab.
case "$ENGINE_NAME" in
    @*)
        echo "FEHLER: ENGINE_NAME darf nicht mit '@' beginnen (der Provider liest das als Dateiverweis)." >&2
        echo "        Anderen Namen wählen, z. B. '${ENGINE_NAME#@}'." >&2
        exit 1
        ;;
esac

# `--engine` ist für den Provider eine SHELL-Zeile (er startet sie mit `sh -c`), nicht ein
# fertiges Argument. Ein Pfad mit Leerzeichen („/engine/my stockfish") würde dort zerlegt.
# Deshalb hier in einfache Anführungszeichen fassen (enthaltene ' korrekt maskiert) — dass es
# wirklich eine ausführbare Datei ist, hat die Prüfung oben schon sichergestellt.
ENGINE_QUOTED="'$(printf '%s' "$ENGINE_PATH" | sed "s/'/'\\\\''/g")'"

# Der Name ist die IDENTITÄT der Registrierung: der Provider aktualisiert beim Start den
# Eintrag GLEICHEN Namens, statt einen zweiten anzulegen. Stabil halten — und auf zwei
# Rechnern zwei verschiedene Namen verwenden, sonst überschreiben sie sich gegenseitig.
set -- --engine "exec $ENGINE_QUOTED" --name "$ENGINE_NAME"

if [ -n "${MAX_THREADS:-}" ]; then set -- "$@" --max-threads "$MAX_THREADS"; fi
if [ -n "${MAX_HASH:-}" ];    then set -- "$@" --max-hash "$MAX_HASH"; fi
if [ -n "${KEEP_ALIVE:-}" ];  then set -- "$@" --keep-alive "$KEEP_ALIVE"; fi
if [ -n "${LOG_LEVEL:-}" ];   then set -- "$@" --log-level "$LOG_LEVEL"; fi
# Nur für einen späteren RookHub-EIGENEN Broker (Phase 2) nötig; leer = lichess.org.
if [ -n "${LICHESS_URL:-}" ]; then set -- "$@" --lichess "$LICHESS_URL"; fi
if [ -n "${BROKER_URL:-}" ];  then set -- "$@" --broker "$BROKER_URL"; fi

# Token vorab prüfen, damit ein fehlender Scope als Klartext-Satz erscheint statt als
# 401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt.
python /opt/preflight.py

echo "Starte Engine-Provider: $ENGINE_PATH als '$ENGINE_NAME'"
exec python /opt/provider.py "$@"
