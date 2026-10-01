#!/usr/bin/env bash
###############################################################################
# Meldet einen Befund der zeitgesteuerten Prueflaeufe (naechtliches E2E, Montags-Audit)
# in den Discord-Kanal der CI — Codereview 2026-09-29, I1-014.
#
# Warum: beide Laeufe meldeten ihr Ergebnis nur in der Actions-Oberflaeche. Das E2E war ab
# 4be56d1b (04.09.) neun Naechte rot, bis es am 13.09. jemand bemerkte; ein kritisches
# npm-Advisory staende nur in einer Job-Summary, die niemand oeffnet.
#
#   WEBHOOK=<Discord-Webhook> ./ci_alert.sh "Text der Meldung"
#
# WEBHOOK kommt aus dem Repo-Secret DISCORD_CI_WEBHOOK (eigener Kanal — NICHT der oeffentliche
# Changelog-Kanal). Fehlt es, steht der Befund als ::warning:: am Lauf und der Schritt bleibt
# gruen: er soll den Befund melden, nicht selbst einer werden. Ein gesetzter, aber kaputter
# Webhook dagegen faellt rot aus (sonst bliebe genau diese Meldung wieder still).
###############################################################################
set -euo pipefail

text="${1:?Meldungstext fehlt}"
run_url="${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-}/actions/runs/${GITHUB_RUN_ID:-}"

if [ -z "${WEBHOOK:-}" ]; then
  echo "::warning title=Keine Discord-Meldung::Secret DISCORD_CI_WEBHOOK fehlt — $text ($run_url)"
  exit 0
fi

# allowed_mentions leer: der Text stammt aus Workflow-Namen/Branches und soll niemanden anpingen.
payload=$(jq -cn --arg content "$text
$run_url" '{content: $content, allowed_mentions: {parse: []}}')
curl -fsS --retry 3 --max-time 20 -H 'Content-Type: application/json' -d "$payload" "$WEBHOOK" >/dev/null
echo "Discord-Meldung gesendet: $text"
