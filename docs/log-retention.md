# Log-Retention in Elasticsearch (DSGVO)

Die Log-Data-Streams enthalten personenbezogene Daten: `labels.IpAddress`, `labels.ForwardedFor` (ältere Einträge auch `labels.XRealIp`),
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

**Erfolg heißt zurückgelesen**: jede ES-Antwort außerhalb 2xx bricht den
jeweiligen Schritt ab (auch die Listen-GETs — früher wurde deren Fehlstatus
verschluckt und das Skript meldete Erfolg ohne Wirkung), und nach jedem PUT wird
der Zustand per GET verifiziert (Policy trägt die Delete-Phase, Template und
alle Backing-Indices tragen `index.lifecycle.name`). Erst dann erscheint
`… (zurückgelesen)`; jeder Fehlschlag führt zu **Exit-Code != 0** — im
Timer-Betrieb steht die Unit dann in `systemctl --failed` (siehe unten). Tests
(gegen ein Fake-`urlopen`, kein ES nötig):
`python3 scripts/tests/test_es_log_retention.py` — in der CI im Job `test-scripts`
(`.github/workflows/test.yml`).

## Regelmäßig ausführen

Einmal anwenden reicht nicht (siehe „Fallen“): die Löschfrist hängt am Template
des Sinks und geht verloren, sobald es neu geschrieben wird. Vorlagen für einen
**monatlichen** Lauf liegen unter
`scripts/systemd/rookhub-log-retention.service.example` +
`scripts/systemd/rookhub-log-retention.timer.example` (am 1. des Monats nach dem
nächtlichen Update-Fenster, `Persistent=true`; Installationsbefehle im Kopf der
Datei).

Der Exit-Code ist das Signal. **!= 0** heißt: ein Schritt ist gescheitert, ODER
es wurde kein Sink-Template `<dienst>-logs-generic-<ecs-version>` bzw. kein
Log-Data-Stream gefunden (Namensschema geändert? noch keine Logs?), ODER ein
Data-Stream benutzt ein Template, das die Policy nicht trägt — seine nächsten
Backing-Indices bekämen keine Löschfrist. „Nichts gefunden“ ist bewusst kein
Erfolg: sonst meldete der Timer Monat für Monat grün, während die Logs
unbegrenzt liegen bleiben.

**Laut wird dieses Signal nur mit der systemd-Unit**: eine fehlgeschlagene Unit
steht in `systemctl --failed`, und wer benachrichtigt werden will, hängt eine
`OnFailure=`-Unit an (in der Vorlage vorbereitet). Darum ist der Timer der
empfohlene Weg.

Notfalls geht es auch per cron, als `/etc/cron.d/rookhub-log-retention`. Die Datei
muss root gehören, darf nicht gruppen- oder weltbeschreibbar sein und muss mit
einem Zeilenumbruch enden. Das Skript muss für `nobody` lesbar sein, Pfad
anpassen:

```
40 5 1 * * nobody (ES_URL=http://localhost:9200 /usr/bin/python3 /opt/rookhub/scripts/es_log_retention.py 2>&1 || echo "FEHLGESCHLAGEN (Exit $?)") | logger -t rookhub-log-retention
```

Die Ausgabe geht über `logger` ins Journal (`journalctl -t rookhub-log-retention`).
Eine Umleitung nach `/var/log/…` taugt hier nicht: `/var/log` gehört root, als
`nobody` scheitert die Umleitung der Shell, bevor Python überhaupt startet, und
die Fehlermeldung ginge an die cron-Mail, die ohne MTA verworfen wird. Aber auch
so bleibt cron **leise**: es wertet den Exit-Code nicht aus und holt einen
verpassten Lauf (Host am 1. um 05:40 aus) nicht nach. Ein Fehlschlag steht dann
nur als `FEHLGESCHLAGEN (Exit …)` im Journal und fällt erst auf, wenn jemand
nachsieht. Wer cron wählt, muss das selbst regelmäßig tun.

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
  erneut laufen lassen (idempotent) — dafür der monatliche Timer oben.
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
