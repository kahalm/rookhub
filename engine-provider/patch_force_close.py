#!/usr/bin/env python3
"""EIN Eingriff in den geholten Provider: frische Verbindung je Upload (`force_close=True`).

Aufruf beim Bauen (NACH der Pruefsummen-Kontrolle) und in der CI vor test/provider.test.py:

    python3 patch_force_close.py /opt/provider.py

Seit dem asynchronen Provider (lichess-org/external-engine d0eeb242) laeuft der Upload einer Suche
ueber eine aiohttp-Sitzung mit Verbindungs-Pool: die Verbindung zum Broker bleibt nach einer Antwort
offen und wird fuer den naechsten Upload wiederverwendet. Schliesst die Gegenseite sie inzwischen
(nginx nach dem Ende eines Streams, oder weil der Anfragende weg ist), merkt aiohttp das erst beim
naechsten Schreiben — der Auftrag ist da schon abgeholt, die Engine hat `go`, der Upload stirbt im
ersten Byte („Connection closed while streaming analysis"), und der Anfragende wartet 15 s ins Leere
(Broker-503). Gemeldet als https://github.com/lichess-org/external-engine/issues/45 mit A/B-Nachweis:
`TCPConnector(force_close=True)` fuer die Upload-Sitzung behebt es; die Poll-Sitzung bleibt im Pool
(die Abfrage alle 10 s soll keinen TLS-Handshake kosten).

Fehlt die Textstelle, bricht der Build ab — beim naechsten Pin-Wechsel faellt so auf, ob upstream
die Sache behoben hat (dann diese Datei samt Dockerfile-Zeilen entfernen) oder den Code umgebaut hat.
"""
import sys

OLD = "aiohttp.ClientSession(timeout=stream_timeout) as submit_http,"
NEW = ("aiohttp.ClientSession(timeout=stream_timeout, connector=aiohttp.TCPConnector(force_close=True))"
       " as submit_http,")

path = sys.argv[1] if len(sys.argv) > 1 else "/opt/provider.py"
src = open(path, encoding="utf-8").read()
if NEW in src:
    print(f"{path}: bereits gepatcht (force_close)")
    sys.exit(0)
n = src.count(OLD)
if n != 1:
    print(f"FEHLER: Textstelle {OLD!r} {n}x gefunden (erwartet 1x) in {path} — Provider-Stand pruefen, "
          f"Patch anpassen oder entfernen.", file=sys.stderr)
    sys.exit(1)
open(path, "w", encoding="utf-8").write(src.replace(OLD, NEW))
print(f"{path}: Upload-Sitzung auf force_close=True gesetzt")
