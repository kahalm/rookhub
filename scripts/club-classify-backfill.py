#!/usr/bin/env python3
"""Liga + Jahrgang der Vereins-Datenbank (LeagueClubGames) bestimmen — TROCKENLAUF, schreibt nichts (0.666.0).

Gegner-FIDE-ID (die NICHT ersetzte Seite) gegen die Liga-Spielpläne: Partien, in denen dieser Spieler gegen ein Schwaz-Team
antrat, in einer Saison nahe dem Partienjahr. Nur EINDEUTIGE Treffer (genau eine Liga + Saison) kommen ins SQL. Ausgabe:
Tabelle auf stdout + SQL-Datei (Argument 1). Das SQL setzt Classifier1/2 nur, wo beide noch leer sind.
"""
import collections, os, subprocess, sys

OUT = sys.argv[1] if len(sys.argv) > 1 else "club-classify-backfill.sql"
ro = os.path.expanduser("~/bin/rookhub-prod-ro")


def q(sql):
    return [l.split("\t") for l in subprocess.run([ro, "-N", "-B", "-e", sql], capture_output=True, text=True, check=True).stdout.splitlines()]


games = q("SELECT Id, IFNULL(Year,''), White, Black, IFNULL(WhiteFide,''), IFNULL(BlackFide,'') FROM LeagueClubGames ORDER BY Id")
lg = q("SELECT t.Season, t.League, g.HomeTeam, g.AwayTeam, IFNULL(g.HomeFide,''), IFNULL(g.AwayFide,'') "
       "FROM LeagueGames g JOIN LeagueTournaments t ON t.Tnr=g.Tnr WHERE g.HomeFide<>'' OR g.AwayFide<>''")
by_fide = collections.defaultdict(list)
for r in lg:
    for f in (r[4], r[5]):
        if f: by_fide[f].append(r)

sql, count = [], collections.Counter()
for id_, year, white, black, wf, bf in games:
    opp = bf if white == "Schwaz" else wf if black == "Schwaz" else ""
    if not opp: count["ohne Gegner-FIDE"] += 1; continue
    cands = [r for r in by_fide.get(opp, []) if "schwaz" in (r[2] + r[3]).lower()]
    if year: cands = [r for r in cands if int(r[0][:4]) in (int(year), int(year) - 1)]
    kinds = sorted({(r[0], r[1]) for r in cands})
    if len(kinds) != 1: count["mehrdeutig" if kinds else "kein Treffer"] += 1; continue
    season, league = kinds[0]
    count["eindeutig"] += 1
    print(f"{id_:>4}  {year or '—':<5} {league} – {season}")
    sql.append(f"UPDATE LeagueClubGames SET Classifier1='{league}', Classifier2='{season}' "
               f"WHERE Id={id_} AND Classifier1 IS NULL AND Classifier2 IS NULL;")
open(OUT, "w").write("\n".join(sql) + "\n")
print(dict(count), f"-> {OUT} (nichts geschrieben)")
