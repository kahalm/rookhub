#!/bin/bash
# Testet den Argument-Aufbau von entrypoint.sh im Dry-Run (ENTRYPOINT_DRY_RUN=1: kein Preflight,
# kein Start). Aufruf: bash test/entrypoint.test.sh  — Exit 0 = alles gut.
set -u
cd "$(dirname "$0")/.."
fake=$(mktemp); printf '#!/bin/sh\n' > "$fake"; chmod +x "$fake"
out=$(mktemp)
trap 'rm -f "$fake" "$out"' EXIT
fails=0
check()      { local name="$1"; shift; if "$@" >/dev/null 2>&1;   then echo "ok   $name"; else echo "FAIL $name"; fails=$((fails + 1)); fi; }
check_fail() { local name="$1"; shift; if ! "$@" >/dev/null 2>&1; then echo "ok   $name"; else echo "FAIL $name"; fails=$((fails + 1)); fi; }
run() { env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH="$fake" "$@" bash ./entrypoint.sh > "$out" 2>&1; }

# 1) Eine Engine: Name unverändert (kein Suffix), Threads/Hash wie gesetzt, genau eine Zeile
run ENGINE_NAME="Heim PC" MAX_THREADS=8 MAX_HASH=512
check "eine Engine, Name ohne Suffix"     grep -q -- '--name Heim\\ PC ' "$out"
check "eine Engine, threads+hash"         grep -q -- '--max-threads 8 --max-hash 512' "$out"
check "eine Engine, genau eine Zeile"     test "$(wc -l < "$out")" -eq 1

# 2) Zwei Engines: Standardnamen mit Index, Hash-Override nur für Engine 2, Threads geteilt
run ENGINE_NAME="Server" ENGINE_COUNT=2 MAX_THREADS=16 MAX_HASH=1024 ENGINE_2_MAX_HASH=8192
check "zwei Zeilen"                       test "$(wc -l < "$out")" -eq 2
check "Engine 1 heisst 'Server 1'"        grep -q -- 'DRY-RUN 1:.*--name Server\\ 1 ' "$out"
check "Engine 2 heisst 'Server 2'"        grep -q -- 'DRY-RUN 2:.*--name Server\\ 2 ' "$out"
check "Engine 1 Hash Default 1024"        grep -q -- 'DRY-RUN 1:.*--max-hash 1024' "$out"
check "Engine 2 Hash Override 8192"       grep -q -- 'DRY-RUN 2:.*--max-hash 8192' "$out"
check "beide 16 Threads"                  test "$(grep -c -- '--max-threads 16' "$out")" -eq 2

# 3) Eigener Name je Engine
run ENGINE_COUNT=2 ENGINE_1_NAME="Live" ENGINE_2_NAME="Hintergrund"
check "Engine 1 eigener Name"             grep -qE -- 'DRY-RUN 1:.*--name Live( |$)' "$out"
check "Engine 2 eigener Name"             grep -qE -- 'DRY-RUN 2:.*--name Hintergrund( |$)' "$out"

# 4) Fehlerfälle
check_fail "ENGINE_COUNT=0 abgelehnt"           run ENGINE_COUNT=0
check_fail "ENGINE_COUNT=abc abgelehnt"         run ENGINE_COUNT=abc
check_fail "ENGINE_COUNT=17 abgelehnt"          run ENGINE_COUNT=17
check_fail "@-Name abgelehnt (je Engine)"       run ENGINE_COUNT=2 ENGINE_2_NAME='@datei'
check_fail "@-Name abgelehnt (Einzel-Engine)"   run ENGINE_NAME='@datei'

# 5) Vorprüfungen, die VOR dem Dry-Run-Ausstieg stehen — vorher ungetestet, obwohl sie die
#    beiden häufigsten Bedienfehler abfangen.
check_fail "ohne LICHESS_API_TOKEN abgelehnt" \
    env -i PATH="$PATH" ENTRYPOINT_DRY_RUN=1 ENGINE_PATH="$fake" bash ./entrypoint.sh
dir=$(mktemp -d)
check_fail "ENGINE_PATH als Verzeichnis abgelehnt" \
    env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH="$dir" bash ./entrypoint.sh
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH="$dir" bash ./entrypoint.sh > "$out" 2>&1 || true
check "Verzeichnis-Hinweis genannt"       grep -q 'Das ist ein Verzeichnis' "$out"
rmdir "$dir"

# 6) `--engine` ist für den Provider eine SHELL-ZEILE: der Pfad muss einfach gequotet ankommen,
#    ein enthaltenes ' korrekt maskiert. Der subtilste Teil der Datei, bisher ohne Testfall
#    (alle anderen Fälle benutzen mktemp-Pfade ohne Sonderzeichen).
tricky_dir=$(mktemp -d)
tricky="$tricky_dir/my eng'ine"
printf '#!/bin/sh\n' > "$tricky"; chmod +x "$tricky"
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH="$tricky" \
    bash ./entrypoint.sh > "$out" 2>&1
# Die Dry-Run-Zeile druckt jedes Argument SHELL-ESCAPED (%q), der Apostroph erscheint dort also
# zusätzlich mit Backslashes. Für den Vergleich die Backslashes entfernen — übrig bleibt genau die
# Zeile, die der Provider an `sh -c` gibt: die korrekte Maskierung EINES Apostrophs innerhalb
# einfacher Anführungszeichen, ohne Backslashes geschrieben also drei Apostrophe hintereinander.
plain=$(tr -d '\\' < "$out")
check "Pfad mit Leerzeichen+Apostroph gequotet" \
    grep -qF -- "exec '$tricky_dir/my eng'''ine'" <<< "$plain"
# Gegenprobe: der ROHE Pfad (unmaskierter Apostroph) darf NICHT so durchgereicht werden — genau
# das zerlegte die Shell des Providers und endete beim Nutzer als „exec: not found".
check_fail "roher Pfad nicht durchgereicht" grep -qF -- "exec '$tricky'" <<< "$plain"
rm -rf "$tricky_dir"

# 7) EINFACHES SCHEMA: eine Live-Engine plus n Hintergrund-Engines aus LIVE_*/BACKGROUND_*.
#    Der wichtigste Teil ist die NAMENSREGEL — der Name IST die Identitaet der
#    Lichess-Registrierung. Die erste Hintergrund-Engine heisst OHNE Ziffer, die zweite mit „2".
#    Bekaeme die erste eine „1", waeren alle bestehenden Registrierungen ploetzlich neue Engines
#    mit neuen Kennungen, und die Hintergrund-Auswahl in jedem Profil zeigte ins Leere.
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH=/bin/sh \
    ENGINE_NAME="RookHub Server 19" LIVE_MAX_THREADS=8 LIVE_MAX_HASH=4096 \
    BACKGROUND_COUNT=4 BACKGROUND_NAME="RookHub Server 19 Hintergrund" \
    BACKGROUND_MAX_THREADS=2 BACKGROUND_MAX_HASH=1024 \
    bash ./entrypoint.sh > "$out" 2>&1
plain=$(tr -d '\\' < "$out")
check "eine Live plus vier Hintergrund"   test "$(grep -c '^DRY-RUN' "$out")" -eq 5
check "Live-Engine mit vollen Threads"    grep -q "DRY-RUN 1:.*--name RookHub Server 19 --max-threads 8 --max-hash 4096" <<< "$plain"
check "erste Hintergrund-Engine OHNE 1"   grep -q "DRY-RUN 2:.*--name RookHub Server 19 Hintergrund --max-threads 2" <<< "$plain"
check "zweite Hintergrund-Engine mit 2"   grep -q "DRY-RUN 3:.*--name RookHub Server 19 Hintergrund 2 " <<< "$plain"
check "vierte Hintergrund-Engine mit 4"   grep -q "DRY-RUN 5:.*--name RookHub Server 19 Hintergrund 4 " <<< "$plain"
check_fail "keine Hintergrund-Engine 1"   grep -q "Hintergrund 1 " <<< "$plain"

# Ein ausdrueckliches ENGINE_<i>_… schlaegt das einfache Schema weiterhin — sonst waere das alte
# Schema mit dem neuen nicht mehr mischbar.
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH=/bin/sh \
    ENGINE_NAME="Server" BACKGROUND_COUNT=2 BACKGROUND_MAX_THREADS=2 ENGINE_3_MAX_THREADS=6 \
    bash ./entrypoint.sh > "$out" 2>&1
plain=$(tr -d '\\' < "$out")
check "ausdruecklicher Wert schlaegt Vorgabe" grep -q "DRY-RUN 3:.*--max-threads 6" <<< "$plain"

# Widersprechen sich beide Zaehlungen, ist das ein Abbruch mit Klartext und kein stiller Vorrang.
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH=/bin/sh \
    ENGINE_COUNT=9 BACKGROUND_COUNT=2 bash ./entrypoint.sh > "$out" 2>&1 || true
check "Widerspruch der Zaehlungen faellt auf" grep -q 'widersprechen sich' "$out"

# Ohne BACKGROUND_COUNT bleibt alles wie bisher (eine Engine, kein neues Verhalten).
env -i PATH="$PATH" LICHESS_API_TOKEN=x ENTRYPOINT_DRY_RUN=1 ENGINE_PATH=/bin/sh \
    ENGINE_NAME="Einzeln" bash ./entrypoint.sh > "$out" 2>&1
check "ohne das neue Schema genau eine Engine" test "$(grep -c '^DRY-RUN' "$out")" -eq 1

if [ "$fails" -eq 0 ]; then echo "ALLE TESTS OK"; else echo "$fails Test(s) fehlgeschlagen"; exit 1; fi
