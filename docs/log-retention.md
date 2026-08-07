# Log-Retention in Elasticsearch (DSGVO)

Die Log-Data-Streams enthalten personenbezogene Daten: `labels.XRealIp`,
`user.name`, `metadata.UserId`, User-Agent/Gerätetyp. Ohne Löschfrist wachsen
sie unbegrenzt — sowohl ein Speicher- als auch ein Datenschutzproblem
(Speicherbegrenzung, Art. 5 Abs. 1 lit. e DSGVO).

`scripts/es_log_retention.py` legt dafür eine ILM-Policy an und verknüpft sie mit
den Log-Data-Streams **und** deren Index-Templates.

## Anwenden

```bash
python3 scripts/es_log_retention.py --dry-run          # zeigt nur, was passieren würde
python3 scripts/es_log_retention.py                    # anwenden (Default: 90 Tage)
python3 scripts/es_log_retention.py --retention-days 30
ES_URL=http://localhost:9200 python3 scripts/es_log_retention.py
```

Nur Standardbibliothek, idempotent, Default-Ziel `http://localhost:9200` (auf dem
Deploy-Host — von außen ist :9200 je nach Firewall dicht).

Erfasst werden alle Data-Streams `<dienst>-logs-generic-default` (rookhub,
crawler, piratechess — je prod und dev) samt der zugehörigen Sink-Templates
`<dienst>-logs-generic-<ecs-version>`.

Policy `rookhub-logs-retention`:

- **hot**: Rollover nach 7 Tagen oder 5 GB primärer Shard-Größe
- **delete**: 90 Tage nach dem Rollover

Die effektive Vorhaltezeit ist damit 90 Tage **plus** die Laufzeit eines
Backing-Index (≤ 7 Tage) — ILM zählt `min_age` in der Delete-Phase ab dem
Rollover, nicht ab dem einzelnen Dokument.

## Warum Data-Stream *und* Template

- `PUT <stream>/_settings` wirkt nur auf die **existierenden** Backing-Indices.
  Nach dem nächsten Rollover käme der neue Index aus dem Template — ohne Policy,
  die Kette risse ab.
- Eine reine Template-Änderung greift umgekehrt erst ab dem nächsten Rollover;
  die aktuellen Backing-Indices lägen ewig herum.

Das Template wird **in place** gepatcht (GET → Setting ergänzen → PUT), nicht
durch ein eigenes, höher priorisiertes Template ersetzt: sonst gingen die
ECS-Mappings des Serilog-Sinks verloren und die Kibana-Felder wären kaputt.

## Fallen

- Der Sink bootstrappt sein Index-Template nur, wenn es noch **nicht existiert** —
  der Patch überlebt also normale Deploys. Wird ein Template gelöscht oder mit
  `OverwriteTemplate` neu geschrieben, ist das `lifecycle`-Setting weg: Skript
  erneut laufen lassen (idempotent, am einfachsten monatlich per cron).
- Die ältere, handangelegte Policy `rookhub-dev-logs` (nur Rollover, **ohne**
  Delete-Phase) wird von diesem Skript auf den Dev-Streams ersetzt. Sie kann
  danach entfallen.
- `schach-bot` schreibt weiterhin in klassische Monats-Indizes
  (`schach-bot-logs-{yyyy.MM}`) statt in Data-Streams und wird hier bewusst
  **nicht** angefasst — die werden über das Bot-Repo bzw. manuell (`DELETE
  schach-bot-logs-YYYY.MM`) abgeräumt.

## Prüfen

```bash
curl -s "$ES_URL/_ilm/policy/rookhub-logs-retention?pretty"
curl -s "$ES_URL/rookhub-logs-generic-default/_ilm/explain?pretty"
```
