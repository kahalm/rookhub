#!/usr/bin/env python3
"""Startpaket-Partien als Partie-Analysen anlegen (SQL auf stdout).

Schreibt je Partie eine Zeile in `GameAnalyses` (Status = Pending) und je Halbzug eine Zeile in
`GameAnalysisPositions` — genau das, was `GameAnalysisService.CreateAsync` anlegt. Den Rest macht
der `GameAnalysisPumpService` von allein: er nimmt sich alle 20 s jede unfertige Analyse vor und
reiht Auftraege nach, bis der Deckel (`MaxOpenJobsPerGame` 12 je Partie, `MaxOpenJobsPerUser` 50
je Nutzer) erreicht ist.

Gedacht fuers Seeden von Hand (analog `scripts/hints/*.sql`), weil das Anlegen ueber die API ein
eingeloggtes Konto braucht — API-Tokens sind auf `/api/extension` beschraenkt.

    python3 seed_game_analyses.py --user 3 --depth 22 | \\
      docker exec -i rookhub-mariadb-dev sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD" rookhub'

Braucht `python-chess`. Die FEN je Halbzug ist die Stellung VOR dem Zug (wie `GamePlies.Parse`).
"""
import argparse, io, json, os, sys

try:
    import chess, chess.pgn
except ImportError:
    sys.exit("python-chess fehlt:  pip install chess")

HERE = os.path.dirname(os.path.abspath(__file__))


def esc(s):
    """MySQL-String-Literal — Backslash und Hochkomma verdoppeln, Zeilenumbrueche als \\n."""
    if s is None:
        return "NULL"
    s = s.replace("\\", "\\\\").replace("'", "''").replace("\n", "\\n").replace("\r", "")
    return "'" + s + "'"


def title_from(game, i):
    """Titel aus den PGN-Headern — fuer Umsetzungen ohne handgepflegtes Manifest.

    'Marshall - Capablanca (Chess Strategy 7: Max Lange Attack)': Namen aus White/Black, der Rest
    aus Event. Die ratende Seite steht hier NICHT: die leitet der Server ab (Ergebnis, sonst
    Bewertung der letzten gerechneten Stellung, siehe GuessSessionService.WinnerSideAsync).
    """
    h = game.headers
    names = " - ".join(x for x in (h.get("White"), h.get("Black")) if x and x != "?")
    event = (h.get("Event") or "").strip()
    return {"n": i + 1, "guess": "?", "title": f"{names} ({event})" if event else names or f"Partie {i + 1}"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--user", type=int, required=True, help="AppUsers.Id, dem die Analysen gehoeren")
    ap.add_argument("--depth", type=int, default=22, help="Zieltiefe je Stellung (Vorgabe des Modus: 30)")
    ap.add_argument("--multipv", type=int, default=5, help="Linien je Stellung (Protokoll-Maximum 5)")
    ap.add_argument("--pgn", default=os.path.join(HERE, "starter-pack.pgn"))
    ap.add_argument("--manifest", default=os.path.join(HERE, "starter-pack.json"),
                    help="Titel-Liste zum PGN; '-' = ohne, dann kommt der Titel aus den Headern "
                         "(so laufen die Buch-Umsetzungen aus books/)")
    ap.add_argument("--limit", type=int, help="nur die ersten N Partien")
    ap.add_argument("--public", action="store_true",
                    help="gleich in den kuratierten Bestand legen (GameAnalyses.IsPublic) — dann "
                         "spielt sie JEDER, auch ohne Anmeldung, sobald sie fertig gerechnet ist")
    args = ap.parse_args()

    # Ohne Manifest traegt das PGN selbst den Titel. Gebraucht fuer die Buch-Umsetzungen: dort gibt
    # es keine handgepflegte Liste, wohl aber ordentliche Header.
    meta = None if args.manifest == "-" else json.load(io.open(args.manifest, encoding="utf-8"))
    fh = io.open(args.pgn, encoding="utf-8")

    print("-- Startpaket Punktepartien: erzeugt von scripts/guess-games/seed_game_analyses.py")
    print(f"-- Nutzer {args.user}, Tiefe {args.depth}, {args.multipv} Linien")
    print("START TRANSACTION;")
    total_plies = 0
    for i in range(len(meta) if meta is not None else 10_000):
        game = chess.pgn.read_game(fh)
        if game is None:
            break
        m = meta[i] if meta is not None else title_from(game, i)
        if args.limit is not None and i >= args.limit:
            break
        board = game.board()
        plies = []
        for mv in game.mainline_moves():
            plies.append((len(plies), board.fen(), mv.uci(), board.san(mv)))
            board.push(mv)
        if not plies:
            continue
        total_plies += len(plies)

        h = game.headers
        pgn_text = str(game)
        print(f"-- {m['n']:2d}  {m['title']}  ({len(plies)} Halbzuege, raet {m['guess']})")
        print(
            "INSERT INTO GameAnalyses (UserId,Title,Pgn,White,Black,Result,Event,StartFen,"
            "TargetDepth,MultiPv,EngineId,Status,PlyCount,IsPublic,CreatedAt,UpdatedAt) VALUES ("
            f"{args.user},{esc(m['title'][:200])},{esc(pgn_text)},{esc(h.get('White'))},"
            f"{esc(h.get('Black'))},{esc(h.get('Result'))},{esc(h.get('Event'))},"
            f"{esc(game.board().fen())},{args.depth},{args.multipv},NULL,0,{len(plies)},"
            f"{1 if args.public else 0},utc_timestamp(6),utc_timestamp(6));"
        )
        print("SET @ga = LAST_INSERT_ID();")
        rows = ",".join(
            f"(@ga,{p},{esc(f)},{esc(u)},{esc(s)},0)" for p, f, u, s in plies
        )
        print(
            "INSERT INTO GameAnalysisPositions (GameAnalysisId,Ply,Fen,GameMoveUci,GameMoveSan,Depth) "
            f"VALUES {rows};"
        )
    print("COMMIT;")
    print(f"-- Summe: {total_plies} Stellungen", file=sys.stderr)


if __name__ == "__main__":
    main()
