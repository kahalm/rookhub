#!/bin/sh
###############################################################################
# Kibana Data Views Auto-Init
#
# Creates 3 data views via Kibana API (idempotent — ignores already-existing).
# Runs as one-shot container after Kibana is healthy.
###############################################################################
set -e

KIBANA_URL="http://kibana:5601"

create_data_view() {
  id="$1"
  name="$2"
  pattern="$3"

  echo "Creating data view: $name ($pattern) ..."
  status=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST "$KIBANA_URL/api/data_views/data_view" \
    -H "kbn-xsrf: true" \
    -H "Content-Type: application/json" \
    -d "{\"data_view\":{\"id\":\"$id\",\"title\":\"$pattern\",\"name\":\"$name\",\"timeFieldName\":\"@timestamp\"}}")

  if [ "$status" = "200" ]; then
    echo "  -> Created."
  elif [ "$status" = "409" ]; then
    echo "  -> Already exists, skipping."
  else
    echo "  -> Unexpected status $status (continuing anyway)."
  fi
}

set_default_data_view() {
  id="$1"
  echo "Setting default data view: $id ..."
  curl -s -o /dev/null \
    -X POST "$KIBANA_URL/api/data_views/default" \
    -H "kbn-xsrf: true" \
    -H "Content-Type: application/json" \
    -d "{\"data_view_id\":\"$id\",\"force\":true}"
  echo "  -> Done."
}

# 1) RookHub API Logs
create_data_view "rookhub-logs" "RookHub Logs" "rookhub-logs-*"

# 2) Crawler Logs
create_data_view "crawler-logs" "Crawler Logs" "crawler-logs-*"

# 3) All Logs (combined)
create_data_view "all-logs" "Alle Logs" "*-logs-*"

# Set combined view as default
set_default_data_view "all-logs"

echo "Kibana data views initialized."
