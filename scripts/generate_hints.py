#!/usr/bin/env python3
"""
Generiert vorberechnete, gestufte Lösungstipps (1=Motiv, 2=Figur/Bereich, 3=erster Zug)
für Buch-Puzzles und schreibt sie sprach-keyed (de/en/hr) nach BookPuzzles.HintsJson.

Spiegelt die Backend-Logik (RookHub.Api/Services/HintGenerationService.cs):
  - Quelle ist die VERIFIZIERTE Lösungslinie (Moves ab StartPly) + Buch-Kommentare
    (Comment + MoveComments) + optional ein Stockfish-Signal. Das LLM beschreibt nur die
    gegebene Lösung (kein Selbst-Lösen → kein Halluzinieren).
  - Anti-Leak: Stufe 1 + 2 dürfen kein konkretes Feld/Rochade nennen, sonst wird die
    komplette Tripel für diese Sprache verworfen.
  - Idempotent: überspringt Puzzles mit aktueller HintsVersion (außer --force).

Standard-Ziel: Buch „1001 Deadly Checkmates" auf der DEV-DB (Port 3309).

------------------------------------------------------------------------------------------
ABHÄNGIGKEITEN (einmalig):
    pip install anthropic pymysql
  Optional für das Engine-Signal: ein `stockfish`-Binary im PATH (apt install stockfish).
  Ohne Stockfish läuft das Skript normal weiter (nur ohne Engine-Hinweis).

NUTZUNG:
    export ANTHROPIC_API_KEY=sk-ant-...
    export ROOKHUB_DB_PASSWORD='<dev-db-passwort>'        # Pflicht
    # optional (Defaults zeigen auf den lokalen DEV-Container):
    export ROOKHUB_DB_HOST=127.0.0.1 ROOKHUB_DB_PORT=3309 \
           ROOKHUB_DB_USER=root ROOKHUB_DB_NAME=rookhub

    # Erst trocken testen (nichts schreiben), nur 5 Puzzles:
    python3 scripts/generate_hints.py --limit 5 --dry-run

    # Dann scharf (schreibt in die DB), alle noch offenen Puzzles des Buchs:
    python3 scripts/generate_hints.py

    # Anderes Buch / alles neu / Prod (Port 3307 — bewusst selbst setzen!):
    python3 scripts/generate_hints.py --book "%Polgar%" --force
    ROOKHUB_DB_PORT=3307 python3 scripts/generate_hints.py   # PROD: mit Vorsicht!

Das Skript committet pro Puzzle einzeln → bei Abbruch bleibt der Fortschritt erhalten.
Danach erscheint im Buch-/Kurs-Solver automatisch der „Tipp"-Knopf.
------------------------------------------------------------------------------------------
"""
import argparse
import json
import os
import re
import subprocess
import sys

HINTS_VERSION = 1
LANGUAGES = (("de", "German"), ("en", "English"), ("hr", "Croatian"))
COORDINATE = re.compile(r"[a-h][1-8]|O-O")          # konkretes Feld/Rochade → in Stufe 1+2 verboten
STOCKFISH_DEPTH = int(os.environ.get("STOCKFISH_DEPTH", "18"))
DEFAULT_BOOK_LIKE = "%1001%eadly%heckmate%"          # „1001 Deadly Checkmates" (case-insensitiv via LIKE)


# ---------------------------------------------------------------- Lösungslinie zerlegen
def tokens(moves):
    return [t for t in (moves or "").split(" ") if t]


def setup_moves_uci(moves, start_ply):
    toks = tokens(moves)
    count = max(0, min(start_ply + 1, len(toks)))
    return " ".join(toks[:count])


def solution_uci(moves, start_ply):
    toks = tokens(moves)
    skip = max(0, min(start_ply + 1, len(toks)))
    return toks[skip:]


def side_to_move(fen, setup):
    parts = (fen or "").split(" ")
    white_start = len(parts) < 2 or parts[1] != "b"
    white = white_start if len(tokens(setup)) % 2 == 0 else not white_start
    return "White" if white else "Black"


# ---------------------------------------------------------------- Stockfish (optional)
def stockfish_signal(fen, setup):
    """Liefert z.B. '#3 (forced mate in 3)' oder '+2.4', oder None wenn keine Engine da ist."""
    try:
        proc = subprocess.Popen(
            ["stockfish"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, text=True, bufsize=1,
        )
    except (FileNotFoundError, OSError):
        return None
    pos = f"position fen {fen}" + (f" moves {setup}" if setup else "")
    try:
        proc.stdin.write(f"uci\nisready\nucinewgame\n{pos}\ngo depth {STOCKFISH_DEPTH}\n")
        proc.stdin.flush()
        score_cp, mate = None, None
        for line in proc.stdout:                       # bis "bestmove"
            line = line.strip()
            if line.startswith("info") and " score " in line:
                t = line.split()
                for i in range(len(t) - 2):
                    if t[i] == "score":
                        if t[i + 1] == "cp":
                            score_cp, mate = int(t[i + 2]), None
                        elif t[i + 1] == "mate":
                            mate, score_cp = int(t[i + 2]), None
                        break
            elif line.startswith("bestmove"):
                break
    except Exception:
        return None
    finally:
        try:
            proc.kill()
        except Exception:
            pass
    if mate is not None:
        return f"#{mate} (forced mate in {abs(mate)})" if mate != 0 else "#"
    if score_cp is not None:
        return ("+" if score_cp >= 0 else "") + f"{score_cp / 100:.1f}"
    return None


# ---------------------------------------------------------------- LLM
SCHEMA = {
    "type": "object",
    "properties": {"hint1": {"type": "string"}, "hint2": {"type": "string"}, "hint3": {"type": "string"}},
    "required": ["hint1", "hint2", "hint3"],
    "additionalProperties": False,
}


def system_prompt(language):
    return (
        "You write graded chess hints for a training puzzle. You are GIVEN the verified solution — "
        "never solve it yourself, only describe and grade it. Produce exactly three escalating hints:\n"
        "- hint1: the motif / idea only (e.g. \"back-rank weakness\"). NO concrete square, file, rank, "
        "piece move, or coordinate.\n"
        "- hint2: which piece or area delivers it, and why it works. Still NO concrete square or move "
        "(no coordinates like e4, no O-O).\n"
        "- hint3: the first move or key idea, may name the move (e.g. \"Queen to h8\"). Do NOT give the "
        "full variation.\n"
        f"Keep each hint short (one sentence, encouraging). Write all three hints in {language}.\n"
        "Return only the JSON object {hint1,hint2,hint3}."
    )


def build_user_prompt(p, setup, solution, engine):
    lines = [f"Start FEN: {p['Fen']}"]
    if setup:
        lines.append(f"Setup moves already played (UCI): {setup}")
    lines.append(f"Side to move in the puzzle: {side_to_move(p['Fen'], setup)}")
    lines.append(f"Verified solution from this position (UCI): {' '.join(solution)}")
    if engine:
        lines.append(f"Engine evaluation (side to move): {engine}.")
    if p.get("Title"):
        lines.append(f"Puzzle title: {p['Title']}")
    if p.get("Tags"):
        lines.append(f"Tags/themes: {p['Tags']}")
    comments = collect_comments(p)
    if comments:
        lines.append("Book annotations (human, may contain the idea):")
        lines.append(comments[:1500])
    return "\n".join(lines)


def collect_comments(p):
    out = []
    if p.get("Comment"):
        out.append(p["Comment"].strip())
    mc = p.get("MoveComments")
    if mc:
        try:
            d = json.loads(mc)
            for k in sorted(d, key=lambda x: int(x)):
                if d[k] and str(d[k]).strip():
                    out.append(str(d[k]).strip())
        except Exception:
            pass
    return "\n".join(out).strip()


def parse_and_validate(text):
    """{hint1,hint2,hint3} → [h1,h2,h3]; None wenn unvollständig oder Stufe 1/2 ein Feld nennt."""
    if not text:
        return None
    try:
        d = json.loads(text)
    except Exception:
        return None
    h1, h2, h3 = (str(d.get(k, "")).strip() for k in ("hint1", "hint2", "hint3"))
    if not (h1 and h2 and h3):
        return None
    if COORDINATE.search(h1) or COORDINATE.search(h2):
        return None
    return [h1, h2, h3]


def generate_hints(client, p, setup, solution, engine):
    result = {}
    for code, language in LANGUAGES:
        try:
            resp = client.messages.create(
                model="claude-opus-4-8",
                max_tokens=4096,
                system=system_prompt(language),
                thinking={"type": "adaptive"},
                output_config={"format": {"type": "json_schema", "schema": SCHEMA}},
                messages=[{"role": "user", "content": build_user_prompt(p, setup, solution, engine)}],
            )
        except Exception as e:
            print(f"    [{code}] API-Fehler: {e}", file=sys.stderr)
            continue
        if getattr(resp, "stop_reason", None) == "refusal":
            print(f"    [{code}] refusal — übersprungen", file=sys.stderr)
            continue
        text = next((b.text for b in resp.content if getattr(b, "type", None) == "text"), None)
        hints = parse_and_validate(text)
        if hints:
            result[code] = hints
        else:
            print(f"    [{code}] keine validen Tipps (Anti-Leak/Format)", file=sys.stderr)
    return result


# ---------------------------------------------------------------- DB + main
def main():
    ap = argparse.ArgumentParser(description="Generiert gestufte Tipps für Buch-Puzzles.")
    ap.add_argument("--book", default=DEFAULT_BOOK_LIKE,
                    help="SQL-LIKE-Muster für Buch (DisplayName/FileName). Default: 1001 Deadly Checkmates.")
    ap.add_argument("--limit", type=int, default=0, help="Max. Anzahl Puzzles (0 = alle).")
    ap.add_argument("--force", action="store_true", help="Auch bereits betipste Puzzles neu generieren.")
    ap.add_argument("--dry-run", action="store_true", help="Nur generieren + anzeigen, nichts schreiben.")
    args = ap.parse_args()

    if not os.environ.get("ANTHROPIC_API_KEY"):
        sys.exit("FEHLER: ANTHROPIC_API_KEY nicht gesetzt.")
    db_password = os.environ.get("ROOKHUB_DB_PASSWORD")
    if db_password is None:
        sys.exit("FEHLER: ROOKHUB_DB_PASSWORD nicht gesetzt.")

    try:
        import anthropic
        import pymysql
    except ImportError as e:
        sys.exit(f"FEHLER: fehlende Abhängigkeit ({e}). Bitte: pip install anthropic pymysql")

    conn = pymysql.connect(
        host=os.environ.get("ROOKHUB_DB_HOST", "127.0.0.1"),
        port=int(os.environ.get("ROOKHUB_DB_PORT", "3309")),
        user=os.environ.get("ROOKHUB_DB_USER", "root"),
        password=db_password,
        database=os.environ.get("ROOKHUB_DB_NAME", "rookhub"),
        charset="utf8mb4",
        cursorclass=pymysql.cursors.DictCursor,
        autocommit=True,
    )
    client = anthropic.Anthropic()

    like = args.book
    where_fresh = "" if args.force else " AND (bp.HintsJson IS NULL OR bp.HintsVersion < %s)"
    sql = (
        "SELECT bp.Id, bp.LineId, bp.Fen, bp.Moves, bp.StartPly, bp.Title, bp.Comment, "
        "bp.MoveComments, bp.Tags "
        "FROM BookPuzzles bp LEFT JOIN Books b ON b.Id = bp.BookId "
        "WHERE (b.DisplayName LIKE %s OR b.FileName LIKE %s OR bp.BookFileName LIKE %s)" + where_fresh +
        " ORDER BY bp.Id"
    )
    params = [like, like, like] + ([] if args.force else [HINTS_VERSION])
    with conn.cursor() as cur:
        cur.execute(sql, params)
        puzzles = cur.fetchall()
    if args.limit:
        puzzles = puzzles[: args.limit]

    print(f"{len(puzzles)} Puzzle(s) zu verarbeiten (book LIKE {like!r}, "
          f"{'force' if args.force else 'nur offene'}, {'DRY-RUN' if args.dry_run else 'SCHREIBT'}).")
    if not puzzles:
        return

    done = 0
    for i, p in enumerate(puzzles, 1):
        setup = setup_moves_uci(p["Moves"], p["StartPly"] or 0)
        solution = solution_uci(p["Moves"], p["StartPly"] or 0)
        if not solution:
            print(f"[{i}/{len(puzzles)}] Id={p['Id']} ohne Lösungszüge — übersprungen.")
            continue
        engine = stockfish_signal(p["Fen"], setup)
        hints = generate_hints(client, p, setup, solution, engine)
        if not hints:
            print(f"[{i}/{len(puzzles)}] Id={p['Id']} — keine Tipps erzeugt, übersprungen.")
            continue
        payload = json.dumps(hints, ensure_ascii=False)
        if args.dry_run:
            print(f"[{i}/{len(puzzles)}] Id={p['Id']} (dry-run) {payload}")
        else:
            with conn.cursor() as cur:
                cur.execute("UPDATE BookPuzzles SET HintsJson=%s, HintsVersion=%s WHERE Id=%s",
                            (payload, HINTS_VERSION, p["Id"]))
            done += 1
            print(f"[{i}/{len(puzzles)}] Id={p['Id']} ✓ ({'/'.join(hints)})"
                  if False else f"[{i}/{len(puzzles)}] Id={p['Id']} ✓ {list(hints.keys())}")

    print(f"\nFertig. {done} Puzzle(s) geschrieben." if not args.dry_run else "\nFertig (dry-run, nichts geschrieben).")


if __name__ == "__main__":
    main()
