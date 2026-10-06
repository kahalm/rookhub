#!/bin/bash
# Baut den Aufruf des Lichess-Providers aus den Umgebungsvariablen (siehe .env.example),
# damit in compose.yml/`docker run` nichts als Kommandozeile gepflegt werden muss. Mit ROOKHUB_URL
# spricht der Provider direkt mit RookHub (eigener Broker), sonst ueber Lichess. EIN Container = EIN
# Broker; beide Wege gleichzeitig sind zwei Container aus demselben Abbild (compose.yml: der zweite
# haengt am Profil `lichess` und liest .env.lichess — ohne ROOKHUB_URL).
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

# ---------------------------------------------------------------------------
# DIREKT MIT ROOKHUB (empfohlen, ohne Lichess): ROOKHUB_URL ist dann EINE Adresse fuer beides —
# Registrierung (sonst lichess.org, --lichess) und Arbeit holen/hochladen (sonst engine.lichess.ovh,
# --broker). RookHub spricht an dieser Stelle dasselbe Protokoll wie Lichess, der Provider bleibt
# unveraendert. Ausdruecklich gesetzte LICHESS_URL/BROKER_URL gewinnen (z. B. Registrierung ueber eine
# andere Adresse als die Arbeit).
#
# ROOKHUB_API_TOKEN (rkh_…, im RookHub-Profil mit Scope „Engine" angelegt) ist der Token des direkten
# Wegs; der Provider selbst liest ausschliesslich LICHESS_API_TOKEN, deshalb wird er unter diesem Namen
# weitergereicht. Mit ROOKHUB_URL hat ROOKHUB_API_TOKEN Vorrang — ein daneben stehender Lichess-Token
# (aus einer fuer beide Container kopierten .env) gilt bei RookHub nicht. Ein rkh_-Token unter
# LICHESS_API_TOKEN (aeltere Anleitung, Windows-Skript) bleibt als Rueckfall gueltig. Ohne ROOKHUB_URL
# ist ein alleiniger ROOKHUB_API_TOKEN dagegen ein Fehler: bei Lichess gilt er nicht, und die
# Registrierung stuerbe erst spaeter mit einem 401 in der Neustart-Schleife.
# ---------------------------------------------------------------------------
TOKEN_SOURCE=LICHESS_API_TOKEN
if [ -n "${ROOKHUB_URL:-}" ]; then
    ROOKHUB_URL="${ROOKHUB_URL%/}"
    LICHESS_URL="${LICHESS_URL:-$ROOKHUB_URL}"
    BROKER_URL="${BROKER_URL:-$ROOKHUB_URL}"
    if [ -n "${ROOKHUB_API_TOKEN:-}" ]; then
        LICHESS_API_TOKEN="$ROOKHUB_API_TOKEN"
        TOKEN_SOURCE=ROOKHUB_API_TOKEN
    fi
elif [ -z "${LICHESS_API_TOKEN:-}" ] && [ -n "${ROOKHUB_API_TOKEN:-}" ]; then
    echo "FEHLER: ROOKHUB_API_TOKEN ist gesetzt, aber ROOKHUB_URL fehlt — ein RookHub-Token gilt bei Lichess nicht." >&2
    echo "        Direkt mit RookHub: ROOKHUB_URL dazu (siehe .env.example)." >&2
    echo "        Ueber Lichess (.env.lichess): LICHESS_API_TOKEN statt ROOKHUB_API_TOKEN eintragen." >&2
    exit 1
fi
# preflight.py liest LICHESS_URL (Token-Pruefung gegen denselben Server, bei dem registriert wird).
if [ -n "${LICHESS_URL:-}" ]; then export LICHESS_URL; fi

if [ -z "${LICHESS_API_TOKEN:-}" ]; then
    if [ -n "${ROOKHUB_URL:-}" ]; then
        echo "FEHLER: ROOKHUB_API_TOKEN ist nicht gesetzt." >&2
        echo "        Im RookHub-Profil unter „API-Tokens" einen Token mit Scope „Engine" anlegen und in die .env" >&2
        echo "        eintragen: ROOKHUB_API_TOKEN=rkh_…" >&2
    else
        echo "FEHLER: LICHESS_API_TOKEN ist nicht gesetzt." >&2
        echo "        Direkt mit RookHub (empfohlen): ROOKHUB_URL und ROOKHUB_API_TOKEN setzen (siehe .env.example)." >&2
        echo "        Ueber Lichess: Token mit den Scopes engine:read UND engine:write anlegen und eintragen:" >&2
        echo "        https://lichess.org/account/oauth/token/create?scopes[]=engine:read&scopes[]=engine:write" >&2
    fi
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

# RUECKFALL auf die frueheren Namen (LIVE_*/BACKGROUND_*). Gepflegt wird die Schreibweise mit
# ENGINE_-Praefix: in einer .env steht sonst ein halbes Dutzend Variablen ohne erkennbare
# Zusammengehoerigkeit, und `env | grep ENGINE_` zeigte die Haelfte der Engine-Einstellungen nicht.
# Eine bestehende .env laeuft unveraendert weiter — ein Umbenennen, das eine laufende Anlage
# stilllegt, waere die teuerste Art, Ordnung zu schaffen.
ENGINE_PRIMARY_NAME="${ENGINE_PRIMARY_NAME:-${LIVE_NAME:-}}"
ENGINE_PRIMARY_MAX_THREADS="${ENGINE_PRIMARY_MAX_THREADS:-${LIVE_MAX_THREADS:-}}"
ENGINE_PRIMARY_MAX_HASH="${ENGINE_PRIMARY_MAX_HASH:-${LIVE_MAX_HASH:-}}"
ENGINE_BACKGROUND_NAME="${ENGINE_BACKGROUND_NAME:-${BACKGROUND_NAME:-}}"
ENGINE_BACKGROUND_MAX_THREADS="${ENGINE_BACKGROUND_MAX_THREADS:-${BACKGROUND_MAX_THREADS:-}}"
ENGINE_BACKGROUND_MAX_HASH="${ENGINE_BACKGROUND_MAX_HASH:-${BACKGROUND_MAX_HASH:-}}"
ENGINE_BACKGROUND_COUNT="${ENGINE_BACKGROUND_COUNT:-${BACKGROUND_COUNT:-}}"

# ---------------------------------------------------------------------------
# EINFACHES SCHEMA (bevorzugt): eine HAUPT-Engine (live), n HINTERGRUND-Engines.
#
# Die Aufteilung ist immer dieselbe — vorne eine Engine mit vielen Threads, weil dort ein Mensch
# auf EINE Stellung wartet, dahinter mehrere mit wenigen, weil dort der Durchsatz zaehlt (siehe
# README: eine Suche wird von 1 auf 8 Threads nur um den Faktor 1,13 schneller, vier Suchen
# nebeneinander um fast das Dreifache). Sie als ENGINE_1_…, ENGINE_2_… durchzunummerieren hiess,
# dieselben zwei Werte fuenfmal zu tippen und bei jeder Aenderung fuenfmal nachzuziehen.
#
# ENGINE_BACKGROUND_COUNT gesetzt => ENGINE_COUNT = 1 + ENGINE_BACKGROUND_COUNT, und die
# Namen/Threads/Hashes der einzelnen Engines werden daraus abgeleitet. Ein ausdrueckliches
# ENGINE_<i>_… schlaegt das weiterhin (das alte Schema bleibt gueltig).
#
# NAMENSREGEL: die erste Hintergrund-Engine heisst genau ENGINE_BACKGROUND_NAME, die zweite
# "ENGINE_BACKGROUND_NAME 2" und so weiter. Das ist kein Schoenheitsentscheid — der Name IST die
# Identitaet der Lichess-Registrierung. Haette die erste eine " 1" bekommen, waeren alle
# bestehenden Registrierungen neue Engines mit neuen Kennungen, und die Hintergrund-Auswahl in
# jedem RookHub-Profil zeigte ins Leere.
if [ -n "${ENGINE_BACKGROUND_COUNT:-}" ]; then
    if ! [[ "$ENGINE_BACKGROUND_COUNT" =~ ^[0-9]+$ ]] || [ "$ENGINE_BACKGROUND_COUNT" -gt 15 ]; then
        echo "FEHLER: ENGINE_BACKGROUND_COUNT muss eine Zahl von 0 bis 15 sein (ist '$ENGINE_BACKGROUND_COUNT')." >&2
        exit 1
    fi
    if [ -n "${ENGINE_COUNT:-}" ] && [ "$ENGINE_COUNT" -ne $((ENGINE_BACKGROUND_COUNT + 1)) ]; then
        echo "FEHLER: ENGINE_COUNT ($ENGINE_COUNT) und ENGINE_BACKGROUND_COUNT ($ENGINE_BACKGROUND_COUNT) widersprechen sich." >&2
        echo "        Das einfache Schema rechnet ENGINE_COUNT selbst aus — die Zeile ENGINE_COUNT weglassen." >&2
        exit 1
    fi
    ENGINE_COUNT=$((ENGINE_BACKGROUND_COUNT + 1))

    # Setzt <var> auf <wert>, SOFERN die Variable noch leer ist und der Wert etwas hergibt.
    # Bewusst if/fi und kein `[ … ] && …`: unter `set -e` beendet ein fehlschlagender Test als
    # letzter Befehl einer Funktion sie mit Code 1 — und damit den ganzen Container (siehe die
    # Falle oben im Kopf dieser Datei).
    default_to() {
        local var="$1" value="$2"
        if [ -n "$value" ] && [ -z "${!var:-}" ]; then
            printf -v "$var" '%s' "$value"
        fi
    }

    default_to ENGINE_1_NAME        "${ENGINE_PRIMARY_NAME:-$ENGINE_NAME}"
    default_to ENGINE_1_MAX_THREADS "$ENGINE_PRIMARY_MAX_THREADS"
    default_to ENGINE_1_MAX_HASH    "$ENGINE_PRIMARY_MAX_HASH"

    for ((b = 1; b <= ENGINE_BACKGROUND_COUNT; b++)); do
        idx=$((b + 1))
        base="${ENGINE_BACKGROUND_NAME:-$ENGINE_NAME Hintergrund}"
        if [ "$b" -gt 1 ]; then base="$base $b"; fi
        default_to "ENGINE_${idx}_NAME"        "$base"
        default_to "ENGINE_${idx}_MAX_THREADS" "$ENGINE_BACKGROUND_MAX_THREADS"
        default_to "ENGINE_${idx}_MAX_HASH"    "$ENGINE_BACKGROUND_MAX_HASH"
    done
fi

ENGINE_COUNT="${ENGINE_COUNT:-1}"
if ! [[ "$ENGINE_COUNT" =~ ^[0-9]+$ ]] || [ "$ENGINE_COUNT" -lt 1 ] || [ "$ENGINE_COUNT" -gt 16 ]; then
    echo "FEHLER: ENGINE_COUNT muss eine Zahl von 1 bis 16 sein (ist '$ENGINE_COUNT')." >&2
    exit 1
fi

# GESTAFFELTE STARTS bei mehreren Engines. Jeder Provider registriert sich beim Start bei
# lichess.org (GET + PUT /api/external-engine). 13 Provider, die das im selben Augenblick tun,
# sehen für den DDoS-Schutz von Lichess wie ein Angriff aus — am 2026-09-11 auf der zweiten
# Maschine erlebt: erst 429, dann eine Sperre der ganzen IP, null registrierte Engines, und ein
# Neustart des Containers wiederholte genau das. PROVIDER_START_DELAY Sekunden Pause zwischen zwei
# Starts (Vorgabe 3, 0 = aus, Dezimalzahl erlaubt); bei einer einzelnen Engine ohne Wirkung.
PROVIDER_START_DELAY="${PROVIDER_START_DELAY:-3}"
if ! [[ "$PROVIDER_START_DELAY" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
    echo "FEHLER: PROVIDER_START_DELAY muss eine Zahl in Sekunden sein, z. B. 3 oder 0.5 (ist '$PROVIDER_START_DELAY')." >&2
    exit 1
fi

# ===========================================================================
# ZEITPLAN (ENGINE_SCHEDULE) — wann wie viel gerechnet wird
# ===========================================================================
# Leer = immer alles, also genau das bisherige Verhalten. Sonst Regeln, mit ";" getrennt:
#
#     ENGINE_SCHEDULE="Mo-Do 08:00-17:00 0%; Fr 08:00-14:00 25%; Sa,So 100%"
#
# Je Regel "<Tage> <von>-<bis> <Prozent>". Tage als mo di mi do fr sa so (oder mon tue wed thu
# fri sat sun), als Bereich (mo-do), als Liste (sa,so) oder "*" für jeden Tag. Zeiten HH:MM; die
# Spanne darf über Mitternacht gehen (22:00-06:00). Ohne Uhrzeit meint eine Regel den ganzen Tag
# ("Sa,So 100%"). Die ERSTE passende Regel gilt, passt keine, wird mit 100 % gerechnet.
#
# Was der Prozentsatz bedeutet: 0 = alle Provider aus — die Engines verschwinden aus RookHub wie
# bei einem ausgeschalteten Rechner, laufende Aufträge brechen ab und werden neu vergeben.
# 100 = alle an. Dazwischen bleibt die LIVE-Engine (Engine 1) an und der Anteil gilt den
# HINTERGRUND-Engines: dort wartet kein Mensch auf das Ergebnis, und dort liegt die Dauerlast.
# Gerundet wird kaufmännisch, bei mehr als 0 % bleibt mindestens eine Hintergrund-Engine übrig.
#
# Prüfen, ohne etwas zu starten (zeigt Prozent und Zahl der Engines zu diesem Zeitpunkt):
#     docker compose run --rm -e ENGINE_SCHEDULE_AT="Mo 09:00" engine-provider
SCHEDULE_TICK="${SCHEDULE_TICK:-20}"

# "mo"/"mon"/"montag" → 1 … "so"/"sun" → 7, wie `date +%u`. Unbekannt → leere Ausgabe.
schedule_day_number() {
    case "$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" in
        mo|mon|montag|monday)         echo 1 ;;
        di|die|tue|tuesday|dienstag)  echo 2 ;;
        mi|mit|wed|wednesday|mittwoch) echo 3 ;;
        do|don|thu|thursday|donnerstag) echo 4 ;;
        fr|fre|fri|friday|freitag)    echo 5 ;;
        sa|sam|sat|saturday|samstag)  echo 6 ;;
        so|son|sun|sunday|sonntag)    echo 7 ;;
        *) echo "" ;;
    esac
}

# "08:00" → Minuten seit Mitternacht. Ungültig → leere Ausgabe. 24:00 ist als ENDE erlaubt.
schedule_minutes() {
    local h m
    [[ "$1" =~ ^([0-9]{1,2}):([0-9]{2})$ ]] || { echo ""; return; }
    h=$((10#${BASH_REMATCH[1]})); m=$((10#${BASH_REMATCH[2]}))
    if [ "$h" -gt 24 ] || [ "$m" -gt 59 ] || { [ "$h" -eq 24 ] && [ "$m" -ne 0 ]; }; then echo ""; return; fi
    echo $((h * 60 + m))
}

# Passt der Wochentag $2 (1..7) auf die Tage-Angabe $1? 0 = ja, 1 = nein, 2 = Angabe kaputt.
schedule_days_match() {
    local spec="$1" day="$2" part a b lower
    lower=$(printf '%s' "$spec" | tr '[:upper:]' '[:lower:]')
    case "$lower" in
        '*'|daily|all|taeglich|täglich|immer) return 0 ;;
    esac
    local parts=()
    IFS=',' read -ra parts <<< "$spec"
    for part in "${parts[@]}"; do
        [ -z "$part" ] && continue
        if [[ "$part" == *-* ]]; then
            a=$(schedule_day_number "${part%%-*}")
            b=$(schedule_day_number "${part##*-}")
            if [ -z "$a" ] || [ -z "$b" ]; then return 2; fi
            if [ "$a" -le "$b" ]; then
                if [ "$day" -ge "$a" ] && [ "$day" -le "$b" ]; then return 0; fi
            # Bereich über das Wochenende hinweg, z. B. fr-mo
            elif [ "$day" -ge "$a" ] || [ "$day" -le "$b" ]; then
                return 0
            fi
        else
            a=$(schedule_day_number "$part")
            if [ -z "$a" ]; then return 2; fi
            if [ "$day" -eq "$a" ]; then return 0; fi
        fi
    done
    return 1
}

# Liegt Minute $3 in der Spanne "$1-$2" (beide schon in Minuten)? Ende ausschliesslich,
# damit "08:00-17:00" und "17:00-22:00" sich nicht überlappen.
schedule_time_match() {
    local from="$1" to="$2" min="$3"
    if [ "$from" -eq "$to" ]; then return 0; fi                      # "00:00-00:00" = ganzer Tag
    if [ "$from" -lt "$to" ]; then
        if [ "$min" -ge "$from" ] && [ "$min" -lt "$to" ]; then return 0; fi
        return 1
    fi
    # über Mitternacht
    if [ "$min" -ge "$from" ] || [ "$min" -lt "$to" ]; then return 0; fi
    return 1
}

# Jede Regel einmal zerlegen und meckern, wenn etwas nicht stimmt — beim START, nicht erst
# nachts um drei, wenn die Regel zum ersten Mal greifen würde.
schedule_validate() {
    local rule days span pct rest from to ok=0 rules=()
    # IFS gilt sonst auch fuer `read` unten und zerlegte die Regel an ';' statt an Leerzeichen.
    IFS=';' read -ra rules <<< "$ENGINE_SCHEDULE"
    for rule in "${rules[@]}"; do
        read -r days span pct rest <<< "$rule"
        [ -z "${days:-}" ] && continue
        # Kurzform ohne Uhrzeit: "Sa,So 100%" meint den ganzen Tag.
        if [ -z "${pct:-}" ] && [[ "${span:-}" =~ ^[0-9]+%?$ ]]; then pct="$span"; span="00:00-24:00"; fi
        if [ -z "${span:-}" ] || [ -z "${pct:-}" ] || [ -n "${rest:-}" ]; then
            echo "FEHLER: ENGINE_SCHEDULE-Regel '$rule' muss '<Tage> <von>-<bis> <Prozent>' sein, z. B. 'Mo-Do 08:00-17:00 0%'." >&2
            return 1
        fi
        schedule_days_match "$days" 1; [ $? -eq 2 ] && { echo "FEHLER: ENGINE_SCHEDULE: '$days' ist keine Tagesangabe (mo di mi do fr sa so, Bereiche mo-do, Listen sa,so, * für jeden Tag)." >&2; return 1; }
        from=$(schedule_minutes "${span%%-*}"); to=$(schedule_minutes "${span##*-}")
        if [ "$span" = "${span#*-}" ] || [ -z "$from" ] || [ -z "$to" ]; then
            echo "FEHLER: ENGINE_SCHEDULE: '$span' ist keine Zeitspanne HH:MM-HH:MM." >&2
            return 1
        fi
        pct="${pct%\%}"
        if ! [[ "$pct" =~ ^[0-9]+$ ]] || [ "$pct" -gt 100 ]; then
            echo "FEHLER: ENGINE_SCHEDULE: '$pct' ist kein Prozentwert von 0 bis 100." >&2
            return 1
        fi
        ok=$((ok + 1))
    done
    if [ "$ok" -eq 0 ]; then
        echo "FEHLER: ENGINE_SCHEDULE ist gesetzt, enthält aber keine Regel." >&2
        return 1
    fi
    return 0
}

# Prozentsatz für Wochentag $1 (1..7) und Minute $2 — die erste passende Regel gewinnt.
schedule_percent_at() {
    local day="$1" min="$2" rule days span pct from to rules=()
    if [ -z "${ENGINE_SCHEDULE:-}" ]; then echo 100; return; fi
    IFS=';' read -ra rules <<< "$ENGINE_SCHEDULE"
    for rule in "${rules[@]}"; do
        read -r days span pct <<< "$rule"
        [ -z "${days:-}" ] && continue
        # Kurzform ohne Uhrzeit: "Sa,So 100%" meint den ganzen Tag.
        if [ -z "${pct:-}" ] && [[ "${span:-}" =~ ^[0-9]+%?$ ]]; then pct="$span"; span="00:00-24:00"; fi
        schedule_days_match "$days" "$day" || continue
        from=$(schedule_minutes "${span%%-*}"); to=$(schedule_minutes "${span##*-}")
        schedule_time_match "$from" "$to" "$min" || continue
        printf '%s\n' "${pct%\%}"
        return
    done
    echo 100
}

# Wie viele Engines laufen bei $1 Prozent? 0 = keine; sonst immer die Live-Engine plus den
# Anteil der Hintergrund-Engines.
schedule_target_count() {
    local pct="$1" bg n
    if [ "$pct" -le 0 ]; then echo 0; return; fi
    if [ "$pct" -ge 100 ]; then echo "$ENGINE_COUNT"; return; fi
    bg=$((ENGINE_COUNT - 1))
    n=$(( (bg * pct + 50) / 100 ))
    if [ "$n" -lt 1 ] && [ "$bg" -gt 0 ]; then n=1; fi
    echo $((1 + n))
}

schedule_percent_now() {
    schedule_percent_at "$(date +%u)" "$((10#$(date +%H) * 60 + 10#$(date +%M)))"
}

if [ -n "${ENGINE_SCHEDULE:-}" ]; then
    schedule_validate || exit 1
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
    # Registrierung und Arbeit: bei ROOKHUB_URL beides RookHub (siehe oben); leer = lichess.org bzw.
    # engine.lichess.ovh, die Vorgaben des Providers.
    if [ -n "${LICHESS_URL:-}" ]; then ARGS+=(--lichess "$LICHESS_URL"); fi
    if [ -n "${BROKER_URL:-}" ];  then ARGS+=(--broker "$BROKER_URL"); fi
}

# Testmodus: nur die Aufrufe zeigen (eine Zeile je Engine), kein Preflight, kein Start.
if [ -n "${ENTRYPOINT_DRY_RUN:-}" ]; then
    for ((i = 1; i <= ENGINE_COUNT; i++)); do
        build_args "$i"
        printf 'DRY-RUN %d:' "$i"; printf ' %q' "${ARGS[@]}"; printf '\n'
    done
    if [ "$ENGINE_COUNT" -gt 1 ]; then
        printf 'DRY-RUN Staffelung: %s s zwischen den Provider-Starts\n' "$PROVIDER_START_DELAY"
    fi
    # Woher der Token kam — NIE der Token selbst; nur beim Alias, sonst bleibt die Ausgabe wie bisher.
    if [ "$TOKEN_SOURCE" != LICHESS_API_TOKEN ]; then printf 'TOKEN-QUELLE: %s\n' "$TOKEN_SOURCE"; fi
    exit 0
fi

# Zeitplan nachrechnen statt starten: ENGINE_SCHEDULE_AT="<Tag> <HH:MM>" sagt, was zu diesem
# Zeitpunkt liefe. Gedacht zum Prüfen einer frisch geschriebenen Regel, bevor man sie nachts wirken
# lässt — und für die Tests in test/entrypoint.test.sh.
if [ -n "${ENGINE_SCHEDULE_AT:-}" ]; then
    read -r at_day at_time <<< "$ENGINE_SCHEDULE_AT"
    at_dn=$(schedule_day_number "${at_day:-}")
    at_min=$(schedule_minutes "${at_time:-}")
    if [ -z "$at_dn" ] || [ -z "$at_min" ]; then
        echo "FEHLER: ENGINE_SCHEDULE_AT erwartet '<Tag> <HH:MM>', z. B. 'Mo 09:00' (ist '$ENGINE_SCHEDULE_AT')." >&2
        exit 1
    fi
    at_pct=$(schedule_percent_at "$at_dn" "$at_min")
    printf 'ZEITPLAN %s: %s%% — %s von %s Engine(s)\n' "$ENGINE_SCHEDULE_AT" "$at_pct" "$(schedule_target_count "$at_pct")" "$ENGINE_COUNT"
    exit 0
fi

# Token vorab prüfen, damit ein fehlender Scope als Klartext-Satz erscheint statt als
# 401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt.
python /opt/preflight.py

# MIT ZEITPLAN: nicht einmal starten und warten, sondern im Takt nachsehen, wie viele Engines
# gerade laufen SOLLEN, und die Differenz herstellen. Deshalb hier auch kein `exec` bei einer
# einzelnen Engine — die Aufsicht muss am Leben bleiben, um sie später wieder anzuwerfen.
if [ -n "${ENGINE_SCHEDULE:-}" ]; then
    declare -a PID=()
    for ((i = 1; i <= ENGINE_COUNT; i++)); do PID[i]=""; done

    stop_engine() {
        local i="$1"
        [ -n "${PID[i]}" ] || return 0
        kill "${PID[i]}" 2>/dev/null || true
        wait "${PID[i]}" 2>/dev/null || true
        PID[i]=""
    }
    stop_all() { local i; for ((i = 1; i <= ENGINE_COUNT; i++)); do stop_engine "$i"; done; }
    trap 'stop_all; exit 143' TERM INT

    echo "Zeitplan aktiv: $ENGINE_SCHEDULE"
    last=-1
    while :; do
        pct=$(schedule_percent_now)
        target=$(schedule_target_count "$pct")
        if [ "$target" -ne "$last" ]; then
            echo "Zeitplan: $pct % — $target von $ENGINE_COUNT Engine(s)"
            last="$target"
        fi

        # Zu viele: von hinten abschalten, die Live-Engine (1) geht als letzte.
        for ((i = ENGINE_COUNT; i > target; i--)); do
            if [ -n "${PID[i]}" ]; then
                echo "Zeitplan: beende '$(engine_name "$i")'"
                stop_engine "$i"
            fi
        done

        # Zu wenige: anwerfen. Ein Provider, der laufen SOLL und trotzdem weg ist, ist ein echter
        # Fehler — dann endet der Container wie bisher, und `restart: unless-stopped` holt alles
        # sauber zurueck.
        started=0
        for ((i = 1; i <= target; i++)); do
            if [ -n "${PID[i]}" ]; then
                if kill -0 "${PID[i]}" 2>/dev/null; then continue; fi
                code=0; wait "${PID[i]}" 2>/dev/null || code=$?
                echo "FEHLER: Engine-Provider '$(engine_name "$i")' hat sich beendet (Exit $code) — alle Engines werden neu gestartet." >&2
                PID[i]=""
                stop_all
                [ "$code" -eq 0 ] && code=70
                exit "$code"
            fi
            if [ "$started" -gt 0 ] && awk "BEGIN { exit !($PROVIDER_START_DELAY > 0) }"; then
                sleep "$PROVIDER_START_DELAY"
            fi
            build_args "$i"
            echo "Zeitplan: starte '$(engine_name "$i")'"
            python /opt/provider.py "${ARGS[@]}" &
            PID[i]=$!
            started=$((started + 1))
        done

        # Im Hintergrund schlafen und darauf warten: so kommt ein TERM waehrend der Pause sofort
        # beim Trap an, statt bis zum Ende des Taktes zu liegen.
        sleep "$SCHEDULE_TICK" & wait $! 2>/dev/null || true
    done
fi

if [ "$ENGINE_COUNT" -eq 1 ]; then
    build_args 1
    echo "Starte Engine-Provider: $ENGINE_PATH als '$(engine_name 1)'"
    exec python /opt/provider.py "${ARGS[@]}"
fi

# Mehrere Engines: alle starten, beim ERSTEN Ausfall alle anderen beenden und mit dessen Code
# enden. Die Logzeilen der Provider laufen unpräfixiert zusammen (jeder meldet beim Start
# seinen Namen; LOG_LEVEL=debug zeigt je Job die Engine).
PIDS=()
# `|| true` hinter kill/wait: unter `set -e` beendete ein fehlgeschlagenes kill (Prozess schon weg)
# bzw. ein wait auf einen mit Exit≠0 gestorbenen Kindprozess die Shell SOFORT — der Trap wäre
# mitten im Aufräumen abgebrochen und der Container mit dem falschen Code beendet.
trap 'kill "${PIDS[@]}" 2>/dev/null || true; wait || true; exit 143' TERM INT
for ((i = 1; i <= ENGINE_COUNT; i++)); do
    # Pause VOR jedem weiteren Provider (nicht vor dem ersten, nicht nach dem letzten): die
    # Registrierungen sollen nacheinander bei lichess.org ankommen, siehe PROVIDER_START_DELAY oben.
    if [ "$i" -gt 1 ] && awk "BEGIN { exit !($PROVIDER_START_DELAY > 0) }"; then
        echo "Warte $PROVIDER_START_DELAY s vor Engine $i/$ENGINE_COUNT (gestaffelte Registrierung)"
        sleep "$PROVIDER_START_DELAY"
    fi
    build_args "$i"
    echo "Starte Engine-Provider $i/$ENGINE_COUNT: $ENGINE_PATH als '$(engine_name "$i")'"
    python /opt/provider.py "${ARGS[@]}" &
    PIDS+=("$!")
done
# FALLE `set -e`: `wait -n` liefert den Exit-Code des zuerst beendeten Kindes — bei Exit≠0 (genau der
# Fehlerfall!) riss das unter `set -e` die Shell mit, BEVOR die Meldung und das Aufräumen liefen.
# Sichtbar war das als „FEHLER … (Exit 0)" ausgerechnet im harmlosen Fall und als komplettes
# Schweigen im echten. Deshalb den Code hier bewusst einsammeln statt abbrechen.
code=0
wait -n "${PIDS[@]}" || code=$?
echo "FEHLER: Ein Engine-Provider hat sich beendet (Exit $code) — alle Engines werden neu gestartet." >&2
kill "${PIDS[@]}" 2>/dev/null || true
wait || true
# Exit 0 wäre hier gelogen: der Container SOLL neu starten (restart: unless-stopped greift nur bei ≠0).
[ "$code" -eq 0 ] && code=70
exit "$code"
