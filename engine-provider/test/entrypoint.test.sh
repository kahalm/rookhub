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

if [ "$fails" -eq 0 ]; then echo "ALLE TESTS OK"; else echo "$fails Test(s) fehlgeschlagen"; exit 1; fi
