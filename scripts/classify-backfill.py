#!/usr/bin/env python3
"""Alte gespeicherte Partien nachklassifizieren (0.661.0/0.662.0) — TROCKENLAUF, schreibt nichts in die DB.

Liest über ~/bin/rookhub-prod-ro (nur SELECT) und schlägt vor:
  * Online-Partien ohne Bedenkzeit: TimeControl über die öffentliche API der Plattform nachtragen (lichess: game/export,
    chess.com: callback/live/game). Der Modus (Bullet/Blitz/…) leitet der Server daraus selbst ab.
  * Ligapartien (Quelle pgn/scoresheet): Liga + Jahrgang aus dem Event-Kopf bzw. dem Spieldatum — nur, wo eindeutig.
Ausgabe: Tabelle auf stdout + SQL-Datei (Pfad = Argument 1, Standard classify-backfill.sql). Das SQL läuft erst NACH dem
Deploy (die Spalten Classifier1/2 gibt es vorher nicht) und nur auf ausdrücklichen Zuruf.
"""
import json, os, re, subprocess, sys, time, urllib.request

OUT = sys.argv[1] if len(sys.argv) > 1 else "classify-backfill.sql"
QUERY = ("SELECT Id, Source, IFNULL(TimeControl,'NULL'), IFNULL(DATE(PlayedAt),'NULL'), IFNULL(SourceUrl,''), "
         "SUBSTRING_INDEX(Pgn, CONCAT(CHAR(10), CHAR(10)), 1) FROM SavedGames ORDER BY Id")


def season_of(iso):
    m = re.match(r"(\d{4})-(\d{2})", iso or "")
    if not m: return None
    start = int(m[1]) if int(m[2]) >= 8 else int(m[1]) - 1
    return f"{start}/{(start + 1) % 100:02d}"


def league_of(event):
    e = event or ""
    if re.search(r"landesliga|^LL$", e, re.I): return "Landesliga"
    m = re.search(r"(\d+)\. ?Klasse", e, re.I)
    return f"{m[1]}. Klasse" if m else None


def season_in(event):
    m = re.search(r"(\d{4})\s*/\s*(\d{2,4})", event or "")
    return f"{m[1]}/{m[2][-2:]}" if m else None


def fetch(url, headers=None):
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0", **(headers or {})})
    with urllib.request.urlopen(req, timeout=20) as r: return json.load(r)


def time_control(source, url):
    try:
        if source == "lichess":
            m = re.search(r"lichess\.org/([A-Za-z0-9]{8})", url)
            if not m: return None
            c = fetch(f"https://lichess.org/api/game/{m[1]}", {"Accept": "application/json"}).get("clock")
            return f"{c['initial']}+{c['increment']}" if c and c["increment"] else str(c["initial"]) if c else None
        m = re.search(r"/game/(?:live/)?(\d+)", url)
        if not m: return None
        h = fetch(f"https://www.chess.com/callback/live/game/{m[1]}")["game"]["pgnHeaders"]
        tc = h.get("TimeControl", "")
        return tc if re.fullmatch(r"\d{1,6}(\+\d{1,4})?|\d{1,3}/\d{1,7}", tc) else None
    except Exception as ex:
        print(f"  (Abruf für {url} gescheitert: {ex})", file=sys.stderr)
        return None


rows = subprocess.run([os.path.expanduser("~/bin/rookhub-prod-ro"), "-N", "-B", "-e", QUERY],
                      capture_output=True, text=True, check=True).stdout.splitlines()
sql, report = [], []
for line in rows:
    id_, source, tc, played, url, hdr = line.split("\t", 5)
    event = (re.search(r'\[Event "([^"]*)"\]', hdr) or [None, ""])[1]
    site = (re.search(r'\[Site "([^"]*)"\]', hdr) or [None, ""])[1]
    if source in ("chess.com", "lichess"):
        if tc != "NULL": continue
        got = time_control(source, url or site)
        time.sleep(1)
        if got:
            sql.append(f"UPDATE SavedGames SET TimeControl='{got}' WHERE Id={id_} AND TimeControl IS NULL;")
            report.append((id_, source, f"Bedenkzeit {got}"))
        else:
            report.append((id_, source, "— nicht ermittelbar"))
    elif source in ("pgn", "scoresheet"):
        liga = league_of(event)
        if not liga:
            report.append((id_, source, f"keine Liga erkennbar (Event „{event}“)"))
            continue
        jahr = season_in(event) or season_of(None if played == "NULL" else played)
        sets = [f"Classifier1='{liga}'"] + ([f"Classifier2='{jahr}'"] if jahr else [])
        sql.append(f"UPDATE SavedGames SET {', '.join(sets)} WHERE Id={id_} AND Classifier1 IS NULL AND Classifier2 IS NULL;")
        report.append((id_, source, f"{liga} – {jahr or 'Jahrgang unbekannt (kein Datum/Saison im Event)'}"))
for r in report: print(f"{r[0]:>4}  {r[1]:<10} {r[2]}")
open(OUT, "w").write("\n".join(sql) + "\n")
print(f"\n{len(sql)} Updates -> {OUT} (nichts geschrieben)")
