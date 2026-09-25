#!/bin/bash
# Referenz-Vektoren fuer den emit.rs-Port des eigenen Engine-Brokers (EmitBuilderVectorTests).
#
# Holt lila-engine auf dem GEPINNTEN Stand, baut die ORIGINALEN emit.rs/uci.rs/multi_pv.rs mit dem
# Harness hier (main.rs) im Rust-Container und gibt je Testfall aus cases.json aus, was lila-engine an
# den Anfragenden schicken wuerde. Die lila-engine-Quelltexte (AGPL) werden NICHT ins Repo kopiert —
# nur zur Laufzeit in ein Wegwerf-Verzeichnis geholt.
#
#   bash tools/emit-reference/run.sh > /tmp/out.json
#
# Wer den Pin anhebt oder cases.json erweitert: Ausgabe mit den LITERALEN Erwartungen in
# tests/RookHub.Api.Tests/EmitBuilderVectorTests.cs vergleichen (ERR:… dort nur „ERR", und
# `,"bestmove":"(none)"` entfaellt — die eine bewusste Abweichung, siehe Kopf der Testklasse).
set -euo pipefail
LILA_ENGINE_SHA="${LILA_ENGINE_SHA:-60ea115c1be25541b0779abaa65c67a575d3df5b}"
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
git clone --quiet https://github.com/lichess-org/lila-engine.git "$work/lila-engine"
git -C "$work/lila-engine" checkout --quiet "$LILA_ENGINE_SHA"
mkdir -p "$work/h/src/model"
cp "$here/Cargo.toml" "$work/h/"
cp "$work/lila-engine/Cargo.lock" "$work/h/"
cp "$here/main.rs" "$work/h/src/main.rs"
cp "$here/cases.json" "$work/h/"
cp "$work/lila-engine/src/emit.rs" "$work/lila-engine/src/uci.rs" "$work/h/src/"
cp "$work/lila-engine/src/model/multi_pv.rs" "$work/h/src/model/"
printf 'mod multi_pv;\npub use multi_pv::{InvalidMultiPvError, MultiPv};\n' > "$work/h/src/model/mod.rs"
# Als aufrufender Nutzer bauen (sonst gehoeren target/ und die Registry root und das Aufraeumen scheitert).
docker run --rm --user "$(id -u):$(id -g)" -e CARGO_HOME=/w/.cargo -v "$work/h:/w" -w /w rust:1-slim \
    sh -c 'cargo run --quiet --release -- cases.json 2>/dev/null'
