#!/usr/bin/env bash
# RookHub — ILM-Zustand der Log-Datenströme NUR LESEN (ändert nichts).
#
#   scripts/es/ilm-check.sh http://localhost:9200
#
# Zeigt die Policy `rookhub-logs-retention` (Delete-Phase), je Datenstrom `*-logs-generic-default` das Template und je
# Backing-Index die verknüpfte Policy, je Sink-Template `*-logs-generic-<ecs>` dessen index.lifecycle.name.
# Exit 0 = alles verknüpft; 1 = Policy fehlt, ein Backing-Index oder Template ohne Policy, oder nichts gefunden;
# 2 = Aufruf/ES nicht erreichbar. Anwenden: scripts/es/ilm-apply.sh. Doku: docs/log-retention.md.
set -euo pipefail

if [[ $# -lt 1 || "$1" == -h || "$1" == --help ]]; then
  sed -n '2,8p' "$0"; exit 2
fi
ES_URL="${1%/}"
POLICY="${ILM_POLICY:-rookhub-logs-retention}"

get() { curl -fsS --max-time 15 "$ES_URL/$1" || { echo "GET $1 fehlgeschlagen" >&2; exit 2; }; }

policy="$(curl -sS --max-time 15 "$ES_URL/_ilm/policy/$POLICY" || { echo "ES nicht erreichbar: $ES_URL" >&2; exit 2; })"
streams="$(get '_data_stream/*-logs-generic-default')"
templates="$(get '_index_template/*-logs-generic-*')"
explain="$(get '*-logs-generic-default/_ilm/explain?only_managed=false')"

POLICY="$POLICY" P="$policy" S="$streams" T="$templates" E="$explain" python3 -I - <<'PY'
import json, os, re, sys
policy_name = os.environ["POLICY"]
bad = 0
try:
    p = json.loads(os.environ["P"])
except ValueError:
    p = {}
phases = p.get(policy_name, {}).get("policy", {}).get("phases", {})
delete = phases.get("delete", {})
if "delete" in delete.get("actions", {}):
    print(f"Policy {policy_name}: delete nach {delete.get('min_age')} (Rollover {phases.get('hot', {}).get('actions', {}).get('rollover', {})})")
else:
    print(f"FEHLT: Policy {policy_name} mit Delete-Phase"); bad += 1

streams = json.loads(os.environ["S"]).get("data_streams", [])
explain = json.loads(os.environ["E"]).get("indices", {})
if not streams:
    print("FEHLT: kein Datenstrom *-logs-generic-default"); bad += 1
for s in sorted(streams, key=lambda x: x["name"]):
    print(f"Datenstrom {s['name']} (Template {s.get('template')}):")
    for idx in s.get("indices", []):
        name = idx["index_name"]
        linked = explain.get(name, {}).get("policy")
        state = "ok" if linked == policy_name else "OHNE POLICY"
        if linked != policy_name: bad += 1
        print(f"  {name}: {linked or '-'} [{state}]")

tpls = [t for t in json.loads(os.environ["T"]).get("index_templates", []) if re.match(r"^[a-z-]+-logs-generic-\d+\.\d+\.\d+$", t["name"])]
if not tpls:
    print("FEHLT: kein Sink-Template *-logs-generic-<ecs-version>"); bad += 1
for t in sorted(tpls, key=lambda x: x["name"]):
    st = t.get("index_template", {}).get("template", {}).get("settings", {})
    linked = st.get("index", {}).get("lifecycle", {}).get("name") or st.get("index.lifecycle.name")
    if linked != policy_name: bad += 1
    print(f"Template {t['name']}: {linked or '-'} [{'ok' if linked == policy_name else 'OHNE POLICY'}]")

print("ERGEBNIS: " + ("alles verknüpft" if bad == 0 else f"{bad} Befund(e) — scripts/es/ilm-apply.sh <ES-URL> ausführen"))
sys.exit(0 if bad == 0 else 1)
PY
