#!/usr/bin/env python3
"""RookHub — Log-Retention in Elasticsearch (ILM-Policy + Template-Verknuepfung).

Die Log-Data-Streams enthalten personenbezogene Daten (labels.XRealIp, user.name,
metadata.UserId, User-Agent). Ohne Loeschfrist wachsen sie unbegrenzt — DSGVO-
relevant. Dieses Skript legt eine ILM-Policy an und haengt sie an die vom
Serilog/ECS-Sink gebootstrappten Data-Streams UND deren Index-Templates.

    python3 scripts/es_log_retention.py --dry-run            # nur anzeigen
    python3 scripts/es_log_retention.py                      # anwenden
    python3 scripts/es_log_retention.py --retention-days 30
    ES_URL=http://localhost:9200 python3 scripts/es_log_retention.py

FALLE (warum Template UND Data-Stream angefasst werden):
  * `PUT <stream>/_settings` setzt index.lifecycle.name nur auf den EXISTIERENDEN
    Backing-Indices. Nach dem naechsten Rollover kaeme der neue Backing-Index aus
    dem Template — ohne Policy, die Kette risse ab.
  * Umgekehrt greift eine reine Template-Aenderung erst ab dem naechsten Rollover;
    die aktuellen Backing-Indices blieben ewig liegen.
  Deshalb beides. Das Template wird IN PLACE gepatcht (GET -> Setting ergaenzen ->
  PUT), NICHT durch ein eigenes hoeher priorisiertes Template ersetzt: sonst
  gingen die ECS-Mappings des Sinks verloren und Kibana-Felder waeren kaputt.

FALLE 2: Der Sink bootstrappt sein Template beim App-Start nur, wenn es noch
  nicht existiert — der Patch ueberlebt also normale Deploys. Wird das Template
  jemals geloescht/ueberschrieben, muss dieses Skript erneut laufen (idempotent).
  Darum am besten per cron/systemd-Timer regelmaessig ausfuehren, siehe docs/backup.md.

Restore/Rueckbau: Policy von einem Stream loesen ->
    curl -XPUT "$ES_URL/<stream>/_settings" -H 'Content-Type: application/json' \\
         -d '{"index.lifecycle.name": null}'
    curl -XDELETE "$ES_URL/_ilm/policy/rookhub-logs-retention"
"""

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

DEFAULT_ES_URL = os.environ.get("ES_URL", "http://localhost:9200")
POLICY_NAME = "rookhub-logs-retention"
# Data-Streams des Stacks (rookhub/crawler/piratechess, je prod + dev).
STREAM_PATTERN = re.compile(r"^[a-z-]+-logs-generic-default$")
# Vom Sink gebootstrappte Index-Templates dazu ("<stream-prefix>-generic-<ecs-version>").
TEMPLATE_PATTERN = re.compile(r"^[a-z-]+-logs-generic-\d+\.\d+\.\d+$")


def request(method, url, body=None, timeout=15):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode() or "{}"
            return resp.status, json.loads(raw)
    except urllib.error.HTTPError as e:
        raw = e.read().decode() or "{}"
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, {"error": raw}


def policy_body(retention_days, rollover_age, rollover_size):
    """Hot-Phase rollt regelmaessig um, Delete-Phase raeumt ab.

    min_age der Delete-Phase zaehlt bei Data-Streams AB DEM ROLLOVER, nicht ab
    dem ersten Dokument — die tatsaechliche Vorhaltezeit ist also retention_days
    plus die Laufzeit eines Backing-Index (<= rollover_age).
    """
    return {
        "policy": {
            "phases": {
                "hot": {
                    "min_age": "0ms",
                    "actions": {
                        "rollover": {
                            "max_age": rollover_age,
                            "max_primary_shard_size": rollover_size,
                        }
                    },
                },
                "delete": {
                    "min_age": f"{retention_days}d",
                    "actions": {"delete": {}},
                },
            }
        }
    }


def main():
    ap = argparse.ArgumentParser(description="ILM-Log-Retention fuer die RookHub-Log-Data-Streams")
    ap.add_argument("--es-url", default=DEFAULT_ES_URL)
    ap.add_argument("--retention-days", type=int, default=90,
                    help="Loeschfrist in Tagen (Default 90)")
    ap.add_argument("--rollover-age", default="7d")
    ap.add_argument("--rollover-size", default="5gb")
    ap.add_argument("--policy-name", default=POLICY_NAME)
    ap.add_argument("--dry-run", action="store_true", help="nur lesen, nichts aendern")
    args = ap.parse_args()

    if args.retention_days < 1:
        print("retention-days muss >= 1 sein", file=sys.stderr)
        return 2

    es = args.es_url.rstrip("/")
    status, _ = request("GET", es)
    if status != 200:
        print(f"Elasticsearch unter {es} nicht erreichbar (HTTP {status})", file=sys.stderr)
        return 1

    failures = 0

    # 1) Policy anlegen/aktualisieren
    body = policy_body(args.retention_days, args.rollover_age, args.rollover_size)
    if args.dry_run:
        print(f"[dry-run] PUT _ilm/policy/{args.policy_name} "
              f"(delete nach {args.retention_days}d, Rollover {args.rollover_age}/{args.rollover_size})")
    else:
        status, resp = request("PUT", f"{es}/_ilm/policy/{args.policy_name}", body)
        if status == 200:
            print(f"Policy '{args.policy_name}': delete nach {args.retention_days}d gesetzt.")
        else:
            print(f"Policy '{args.policy_name}' FEHLER (HTTP {status}): {resp}", file=sys.stderr)
            failures += 1

    # 2) Index-Templates des Sinks in place patchen (gilt fuer kuenftige Backing-Indices)
    status, resp = request("GET", f"{es}/_index_template")
    templates = resp.get("index_templates", []) if status == 200 else []
    for entry in templates:
        name = entry.get("name", "")
        if not TEMPLATE_PATTERN.match(name):
            continue
        tpl = entry["index_template"]
        settings = tpl.setdefault("template", {}).setdefault("settings", {})
        current = settings.get("index", {}).get("lifecycle", {}).get("name") \
            or settings.get("index.lifecycle.name")
        if current == args.policy_name:
            print(f"Template '{name}': bereits verknuepft.")
            continue
        settings.setdefault("index", {}).setdefault("lifecycle", {})["name"] = args.policy_name
        if args.dry_run:
            print(f"[dry-run] PUT _index_template/{name} (+ index.lifecycle.name)")
            continue
        status, resp = request("PUT", f"{es}/_index_template/{name}", tpl)
        if status == 200:
            print(f"Template '{name}': verknuepft.")
        else:
            print(f"Template '{name}' FEHLER (HTTP {status}): {resp}", file=sys.stderr)
            failures += 1

    # 3) Bestehende Data-Streams (= ihre aktuellen Backing-Indices) nachziehen
    status, resp = request("GET", f"{es}/_data_stream")
    streams = [s["name"] for s in resp.get("data_streams", [])] if status == 200 else []
    matched = [s for s in streams if STREAM_PATTERN.match(s)]
    if not matched:
        print("Keine passenden Log-Data-Streams gefunden (noch keine Logs geschrieben?).")
    for stream in matched:
        if args.dry_run:
            print(f"[dry-run] PUT {stream}/_settings (index.lifecycle.name={args.policy_name})")
            continue
        status, resp = request("PUT", f"{es}/{stream}/_settings",
                               {"index.lifecycle.name": args.policy_name})
        if status == 200:
            print(f"Data-Stream '{stream}': Policy gesetzt.")
        else:
            print(f"Data-Stream '{stream}' FEHLER (HTTP {status}): {resp}", file=sys.stderr)
            failures += 1

    print(f"Fertig ({'dry-run, ' if args.dry_run else ''}Fehler: {failures}).")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
