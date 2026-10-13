#!/usr/bin/env bash
# UI-Sweep starten — installiert beim ersten Mal die Abhängigkeiten. Optionen: ./sweep.sh --help
set -euo pipefail
cd "$(dirname "$0")"
[ -d node_modules/playwright-core ] || npm install --silent
# Emoji-Schrift für das lokale Chromium (Medaillen, Sterne …): fehlt sie, zeigt jedes Emoji ein leeres Kästchen.
# Ohne root aus dem Playwright-Image in die Nutzerschriften kopieren (einmalig).
if ! fc-list 2>/dev/null | grep -qi emoji && command -v docker >/dev/null; then
  v=$(node -p "require('./node_modules/playwright-core/package.json').version")
  mkdir -p "$HOME/.local/share/fonts"
  docker run --rm -v "$HOME/.local/share/fonts:/out" "mcr.microsoft.com/playwright:v$v-noble" \
    sh -c 'cp "$(fc-list | grep -i emoji | head -1 | cut -d: -f1)" /out/' && fc-cache -f >/dev/null 2>&1 || true
fi
exec node sweep.mjs "$@"
