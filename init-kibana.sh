#!/bin/sh
###############################################################################
# Kibana Data Views + Dashboard Auto-Init
#
# Creates 3 data views and a logging dashboard via Kibana API.
# Idempotent — ignores already-existing objects.
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
    -d "{\"data_view\":{\"id\":\"$id\",\"title\":\"$pattern\",\"name\":\"$name\",\"timeFieldName\":\"@timestamp\",\"allowNoIndex\":true}}")

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

import_dashboard() {
  echo "Importing dashboard via saved objects ..."

  # Build NDJSON file with visualizations + dashboard
  cat > /tmp/dashboard.ndjson << 'NDJSON'
{"type":"search","id":"recent-logs-search","attributes":{"title":"Recent Logs","description":"Alle Log-Eintraege","columns":["@timestamp","level","messageTemplate.keyword","fields.SourceContext","fields.RequestPath"],"sort":[["@timestamp","desc"]],"kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"log-volume-histogram","attributes":{"title":"Log Volume","description":"Anzahl Logs ueber Zeit","visState":"{\"title\":\"Log Volume\",\"type\":\"histogram\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"},{\"id\":\"2\",\"enabled\":true,\"type\":\"date_histogram\",\"params\":{\"field\":\"@timestamp\",\"useNormalizedEsInterval\":true,\"scaleMetricValues\":false,\"interval\":\"auto\",\"used_interval\":\"5m\",\"drop_partials\":false,\"min_doc_count\":1,\"extended_bounds\":{}},\"schema\":\"segment\"}],\"params\":{\"type\":\"histogram\",\"grid\":{\"categoryLines\":false},\"categoryAxes\":[{\"id\":\"CategoryAxis-1\",\"type\":\"category\",\"position\":\"bottom\",\"show\":true,\"scale\":{\"type\":\"linear\"},\"labels\":{\"show\":true,\"truncate\":100},\"title\":{}}],\"valueAxes\":[{\"id\":\"ValueAxis-1\",\"name\":\"LeftAxis-1\",\"type\":\"value\",\"position\":\"left\",\"show\":true,\"scale\":{\"type\":\"linear\",\"mode\":\"normal\"},\"labels\":{\"show\":true,\"rotate\":0,\"truncate\":100},\"title\":{\"text\":\"Count\"}}],\"seriesParams\":[{\"show\":true,\"type\":\"histogram\",\"mode\":\"stacked\",\"data\":{\"label\":\"Count\",\"id\":\"1\"},\"valueAxis\":\"ValueAxis-1\",\"drawLinesBetweenPoints\":true,\"lineWidth\":2,\"showCircles\":true}],\"addTooltip\":true,\"addLegend\":true,\"legendPosition\":\"right\",\"times\":[],\"addTimeMarker\":false,\"labels\":{\"show\":false},\"thresholdLine\":{\"show\":false,\"value\":10,\"width\":1,\"style\":\"full\",\"color\":\"#E7664C\"}}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"log-level-pie","attributes":{"title":"Log Levels","description":"Verteilung nach Log-Level","visState":"{\"title\":\"Log Levels\",\"type\":\"pie\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"},{\"id\":\"2\",\"enabled\":true,\"type\":\"terms\",\"params\":{\"field\":\"level.keyword\",\"orderBy\":\"1\",\"order\":\"desc\",\"size\":10,\"otherBucket\":false,\"otherBucketLabel\":\"Other\",\"missingBucket\":false,\"missingBucketLabel\":\"Missing\"},\"schema\":\"segment\"}],\"params\":{\"type\":\"pie\",\"addTooltip\":true,\"addLegend\":true,\"legendPosition\":\"right\",\"isDonut\":true,\"labels\":{\"show\":true,\"values\":true,\"last_level\":true,\"truncate\":100}}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"logs-by-app-pie","attributes":{"title":"Logs by Application","description":"RookHub vs Crawler Log-Anteil","visState":"{\"title\":\"Logs by Application\",\"type\":\"pie\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"},{\"id\":\"2\",\"enabled\":true,\"type\":\"terms\",\"params\":{\"field\":\"_index\",\"orderBy\":\"1\",\"order\":\"desc\",\"size\":10,\"otherBucket\":false,\"otherBucketLabel\":\"Other\",\"missingBucket\":false,\"missingBucketLabel\":\"Missing\"},\"schema\":\"segment\"}],\"params\":{\"type\":\"pie\",\"addTooltip\":true,\"addLegend\":true,\"legendPosition\":\"right\",\"isDonut\":true,\"labels\":{\"show\":true,\"values\":true,\"last_level\":true,\"truncate\":100}}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"error-logs-count","attributes":{"title":"Errors & Warnings","description":"Anzahl Error und Warning Logs","visState":"{\"title\":\"Errors & Warnings\",\"type\":\"metric\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"}],\"params\":{\"addTooltip\":true,\"addLegend\":false,\"type\":\"metric\",\"metric\":{\"percentageMode\":false,\"useRanges\":false,\"colorSchema\":\"Green to Red\",\"metricColorMode\":\"None\",\"colorsRange\":[{\"from\":0,\"to\":10000}],\"labels\":{\"show\":true},\"invertColors\":false,\"style\":{\"bgFill\":\"#000\",\"bgColor\":false,\"labelColor\":false,\"subText\":\"\",\"fontSize\":60}}}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"level.keyword: \\\"Error\\\" OR level.keyword: \\\"Warning\\\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"log-volume-by-app","attributes":{"title":"Log Volume by Application","description":"Log-Aufkommen pro Anwendung ueber Zeit","visState":"{\"title\":\"Log Volume by Application\",\"type\":\"area\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"},{\"id\":\"2\",\"enabled\":true,\"type\":\"date_histogram\",\"params\":{\"field\":\"@timestamp\",\"useNormalizedEsInterval\":true,\"scaleMetricValues\":false,\"interval\":\"auto\",\"used_interval\":\"5m\",\"drop_partials\":false,\"min_doc_count\":1,\"extended_bounds\":{}},\"schema\":\"segment\"},{\"id\":\"3\",\"enabled\":true,\"type\":\"terms\",\"params\":{\"field\":\"_index\",\"orderBy\":\"1\",\"order\":\"desc\",\"size\":5,\"otherBucket\":false,\"missingBucket\":false},\"schema\":\"group\"}],\"params\":{\"type\":\"area\",\"grid\":{\"categoryLines\":false},\"categoryAxes\":[{\"id\":\"CategoryAxis-1\",\"type\":\"category\",\"position\":\"bottom\",\"show\":true,\"scale\":{\"type\":\"linear\"},\"labels\":{\"show\":true,\"truncate\":100},\"title\":{}}],\"valueAxes\":[{\"id\":\"ValueAxis-1\",\"name\":\"LeftAxis-1\",\"type\":\"value\",\"position\":\"left\",\"show\":true,\"scale\":{\"type\":\"linear\",\"mode\":\"normal\"},\"labels\":{\"show\":true,\"rotate\":0,\"truncate\":100},\"title\":{\"text\":\"Count\"}}],\"seriesParams\":[{\"show\":true,\"type\":\"area\",\"mode\":\"stacked\",\"data\":{\"label\":\"Count\",\"id\":\"1\"},\"drawLinesBetweenPoints\":true,\"lineWidth\":2,\"showCircles\":true,\"interpolate\":\"linear\",\"valueAxis\":\"ValueAxis-1\"}],\"addTooltip\":true,\"addLegend\":true,\"legendPosition\":\"right\",\"times\":[],\"addTimeMarker\":false,\"labels\":{\"show\":false},\"thresholdLine\":{\"show\":false,\"value\":10,\"width\":1,\"style\":\"full\",\"color\":\"#E7664C\"}}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"puzzles-solved-metric","attributes":{"title":"Puzzles Solved (24h)","description":"Anzahl geloester Puzzles in den letzten 24 Stunden","visState":"{\"title\":\"Puzzles Solved (24h)\",\"type\":\"metric\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"}],\"params\":{\"addTooltip\":true,\"addLegend\":false,\"type\":\"metric\",\"metric\":{\"percentageMode\":false,\"useRanges\":false,\"colorSchema\":\"Green to Red\",\"metricColorMode\":\"None\",\"colorsRange\":[{\"from\":0,\"to\":10000}],\"labels\":{\"show\":true},\"invertColors\":false,\"style\":{\"bgFill\":\"#000\",\"bgColor\":false,\"labelColor\":false,\"subText\":\"\",\"fontSize\":60}}}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"messageTemplate.keyword: *PuzzleAttempt* AND fields.Result.keyword: \\\"solved\\\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"visualization","id":"puzzles-per-user-table","attributes":{"title":"Puzzles per User (24h)","description":"Puzzle-Attempts pro User in den letzten 24 Stunden","visState":"{\"title\":\"Puzzles per User (24h)\",\"type\":\"table\",\"aggs\":[{\"id\":\"1\",\"enabled\":true,\"type\":\"count\",\"params\":{},\"schema\":\"metric\"},{\"id\":\"2\",\"enabled\":true,\"type\":\"terms\",\"params\":{\"field\":\"fields.UserId\",\"orderBy\":\"1\",\"order\":\"desc\",\"size\":20,\"otherBucket\":false,\"otherBucketLabel\":\"Other\",\"missingBucket\":true,\"missingBucketLabel\":\"Anonymous\"},\"schema\":\"bucket\"}],\"params\":{\"perPage\":20,\"showPartialRows\":false,\"showMetricsAtAllLevels\":false,\"showTotal\":false,\"totalFunc\":\"sum\",\"percentageCol\":\"\"}}","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"index\":\"all-logs\",\"query\":{\"query\":\"messageTemplate.keyword: *PuzzleAttempt*\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"id":"all-logs","name":"kibanaSavedObjectMeta.searchSourceJSON.index","type":"index-pattern"}]}
{"type":"dashboard","id":"rookhub-logging-dashboard","attributes":{"title":"RookHub Logging Dashboard","description":"Zentrales Logging-Dashboard fuer RookHub und Crawler","timeRestore":true,"timeTo":"now","timeFrom":"now-24h","refreshInterval":{"pause":false,"value":30000},"panelsJSON":"[{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":0,\"y\":0,\"w\":24,\"h\":12,\"i\":\"1\"},\"panelIndex\":\"1\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_1\"},{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":24,\"y\":0,\"w\":12,\"h\":12,\"i\":\"2\"},\"panelIndex\":\"2\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_2\"},{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":36,\"y\":0,\"w\":12,\"h\":12,\"i\":\"3\"},\"panelIndex\":\"3\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_3\"},{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":0,\"y\":12,\"w\":16,\"h\":6,\"i\":\"4\"},\"panelIndex\":\"4\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_4\"},{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":16,\"y\":12,\"w\":32,\"h\":6,\"i\":\"5\"},\"panelIndex\":\"5\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_5\"},{\"version\":\"8.17.0\",\"type\":\"search\",\"gridData\":{\"x\":0,\"y\":18,\"w\":48,\"h\":18,\"i\":\"6\"},\"panelIndex\":\"6\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_6\"},{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":0,\"y\":36,\"w\":16,\"h\":8,\"i\":\"7\"},\"panelIndex\":\"7\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_7\"},{\"version\":\"8.17.0\",\"type\":\"visualization\",\"gridData\":{\"x\":16,\"y\":36,\"w\":32,\"h\":8,\"i\":\"8\"},\"panelIndex\":\"8\",\"embeddableConfig\":{\"enhancements\":{}},\"panelRefName\":\"panel_8\"}]","kibanaSavedObjectMeta":{"searchSourceJSON":"{\"query\":{\"query\":\"\",\"language\":\"kuery\"},\"filter\":[]}"}},"references":[{"name":"panel_1","type":"visualization","id":"log-volume-histogram"},{"name":"panel_2","type":"visualization","id":"log-level-pie"},{"name":"panel_3","type":"visualization","id":"logs-by-app-pie"},{"name":"panel_4","type":"visualization","id":"error-logs-count"},{"name":"panel_5","type":"visualization","id":"log-volume-by-app"},{"name":"panel_6","type":"search","id":"recent-logs-search"},{"name":"panel_7","type":"visualization","id":"puzzles-solved-metric"},{"name":"panel_8","type":"visualization","id":"puzzles-per-user-table"}]}
NDJSON

  status=$(curl -s -o /tmp/import-result.txt -w "%{http_code}" \
    -X POST "$KIBANA_URL/api/saved_objects/_import?overwrite=true" \
    -H "kbn-xsrf: true" \
    --form file=@/tmp/dashboard.ndjson)

  if [ "$status" = "200" ]; then
    echo "  -> Dashboard imported successfully."
  else
    echo "  -> Import status $status (continuing anyway)."
    cat /tmp/import-result.txt 2>/dev/null || true
  fi
}

# 1) Data Views
create_data_view "rookhub-logs" "RookHub Logs" "rookhub-logs-*"
create_data_view "crawler-logs" "Crawler Logs" "crawler-logs-*"
create_data_view "all-logs" "Alle Logs" "*-logs-*"

# 2) Set default
set_default_data_view "all-logs"

# 3) Dashboard + Visualizations
import_dashboard

echo "Kibana init complete."
