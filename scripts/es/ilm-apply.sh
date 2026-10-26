#!/usr/bin/env bash
# RookHub — ILM-Löschfrist für die Log-Datenströme anwenden (rookhub, crawler, piratechess; je prod + dev).
#
#   scripts/es/ilm-apply.sh http://localhost:9200            # anwenden, Frist ILM_DAYS (Standard 90)
#   ILM_DAYS=60 scripts/es/ilm-apply.sh http://localhost:9200
#   scripts/es/ilm-apply.sh http://localhost:9200 --dry-run  # nur zeigen, nichts ändern
#
# Dünne Hülle um scripts/es_log_retention.py (die EINE Umsetzung: Policy `rookhub-logs-retention` mit hot-Rollover und
# delete nach ILM_DAYS, Verknüpfung mit allen `<dienst>-logs-generic-default`-Datenströmen UND ihren Sink-Templates
# `<dienst>-logs-generic-<ecs-version>`, jeder Schritt zurückgelesen). Idempotent; Exit != 0 bei jedem Fehlschlag und
# wenn kein Template/Datenstrom gefunden wurde. Danach mit scripts/es/ilm-check.sh prüfen. Doku: docs/log-retention.md.
#
# Die Namen kommen aus den Serilog-Sinks: Elasticsearch:IndexFormat (Vorgabe `rookhub-logs-{0:yyyy.MM}`,
# `crawler-logs-…`, `piratechess-logs-…`) → Data-Stream `<präfix>-logs-generic-default` (LoggingSetup.cs bzw. Program.cs
# der Dienste); der log-watcher liest dieselben `*-logs-*`-Muster.
set -euo pipefail

if [[ $# -lt 1 || "$1" == -h || "$1" == --help ]]; then
  sed -n '2,8p' "$0"; exit 2
fi
ES_URL="$1"; shift
ILM_DAYS="${ILM_DAYS:-90}"
if ! [[ "$ILM_DAYS" =~ ^[1-9][0-9]*$ ]]; then
  echo "ILM_DAYS muss eine positive ganze Zahl sein (ist: $ILM_DAYS)" >&2; exit 2
fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$HERE/../es_log_retention.py" --es-url "$ES_URL" --retention-days "$ILM_DAYS" "$@"
