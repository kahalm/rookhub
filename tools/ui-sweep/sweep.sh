#!/usr/bin/env bash
# UI-Sweep starten — installiert beim ersten Mal die Abhängigkeiten. Optionen: ./sweep.sh --help
set -euo pipefail
cd "$(dirname "$0")"
[ -d node_modules/playwright-core ] || npm install --silent
exec node sweep.mjs "$@"
