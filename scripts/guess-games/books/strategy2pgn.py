#!/usr/bin/env python3
"""Edward Lasker, 'Chess Strategy' (1915, gemeinfrei) -> PGN mit Zug-Kommentaren.

Dasselbe Notationsproblem wie bei Capablanca (beschreibende Notation aus Sicht des Ziehenden),
deshalb wird der AUFLOESER aus `capa2pgn` wiederverwendet: zu jedem Token werden die legalen Zuege
der Stellung gefiltert, und was uebrig bleibt, muss eindeutig sein.

Der LAYOUT-Teil ist ein anderer und steht darum hier:

* Die Partien stehen im Abschnitt 'ILLUSTRATIVE GAMES FROM MASTER TOURNAMENTS' und beginnen je mit
  'GAME No. n' (mal 'No.', mal 'NO.'), gefolgt von 'White: X.   Black: Y' und einer Zeile mit der
  Eroeffnung.
* Zwischen den Zuegen stehen ASCII-DIAGRAMME (Rahmen aus '-' und '|', Feldlegende, 'Diag. n').
  Ungefiltert landen sie als Kommentar am letzten Zug — deshalb fliegen sie zeilenweise raus.
* Der Kommentar ist FREITEXT zwischen den Zugzeilen und gehoert zum zuletzt gespielten Zug.

Anders als bei Capablanca kommentiert hier ein DRITTER (Edward Lasker), nicht einer der beiden
Spieler: die Anmerkungen liegen daher auf beiden Seiten.
"""
import re
import sys

import chess

sys.path.insert(0, __file__.rsplit('/', 1)[0])
from capa2pgn import ENDMARK, MOVE_LINE, PAGE, clean_prose, resolve  # noqa: E402

GAME_HEAD = re.compile(r'^\s*GAME N[oO]\.\s*(\d+[a-z]?)\s*$', re.M)


def normalise(tok):
    """Die engere Typografie dieses Buchs auf die Form bringen, die der Aufloeser kennt.

    Zwei Unterschiede zu Capablanca, beide rein schreibweise:

    * **'B-R5ch'** — das Schachzeichen klebt an der Ziffer. Der Aufloeser schneidet 'ch' nur mit
      Wortgrenze ab, und zwischen '5' und 'c' ist keine. Ein Leerzeichen genuegt; das Merkmal
      'gibt Schach' bleibt damit erhalten und hilft beim Unterscheiden.
    * **'QR-Q sq'** — die Grundreihe heisst in dieser Notation 'sq' (square) statt '1'.
    """
    t = re.sub(r'(?<=[1-8])\s*(ch|mate)\b', r' \1', tok, flags=re.I)
    return re.sub(r'\bsq\b\.?', '1', t, flags=re.I)

# Eine Diagramm-Zeile: Rahmen, Figurenzeile, Feldlegende oder die Bildunterschrift.
DIAGRAM = re.compile(r'^\s*(-{5,}|\d?\s*\|.*\||\|.*|[A-H](\s+[A-H]){3,}\s*|Diag\.\s*\d+)\s*$')


def games_section(book):
    """Der ZWEITE Treffer ist der Abschnitt selbst — der erste ist das Inhaltsverzeichnis."""
    hits = [m.start() for m in re.finditer(r'^ILLUSTRATIVE GAMES FROM MASTER TOURNAMENTS\s*$',
                                           book, flags=re.M)]
    if not hits:
        raise ValueError('Abschnitt mit den Partien nicht gefunden')
    start = hits[-1]
    tail = re.search(r'^\s*(INDEX|APPENDIX|\*\*\* END OF|End of Project Gutenberg)',
                     book[start:], flags=re.M)
    return book[start:start + (tail.start() if tail else len(book))]


def split_games(book):
    """-> [(Nummer, Rohtext)] in Buchreihenfolge."""
    body = games_section(book)
    marks = [(m.group(1), m.start(), m.end()) for m in GAME_HEAD.finditer(body)]
    out = []
    for i, (num, _s, e) in enumerate(marks):
        end = marks[i + 1][1] if i + 1 < len(marks) else len(body)
        out.append((num, body[e:end]))
    return out


def parse_game(num, raw):
    """Zuege + Kommentare einer Partie. Kommentar gehoert zum LETZTEN Zug davor."""
    head = raw[:600]
    white = black = '?'
    m = re.search(r'White:\s*(.+)', head)
    if m:
        # Die Namen tragen selbst Punkte ('Ed. Lasker'), deshalb wird an 'Black:' getrennt.
        parts = re.split(r'\s*Black:\s*', m.group(1).strip(), maxsplit=1)
        white = parts[0].strip().rstrip('.').strip()
        if len(parts) > 1:
            black = re.split(r'\s{2,}', parts[1].strip())[0].strip().rstrip('.').strip()

    # Die Zeile nach den Namen nennt Eroeffnung und Turnier: 'Ruy Lopez (compare p. 30).'
    opening = ''
    for line in head.splitlines():
        s = line.strip()
        if s and 'White:' not in s and not GAME_HEAD.match(line) and not s.startswith('Black:'):
            # Die Zeile traegt oft noch einen Seitenverweis ('(compare p. 35)') — der gehoert
            # nicht in den Titel der Partie.
            opening = re.sub(r'\s*\((compare|see)[^)]*\)', '', s).strip().rstrip('.')
            break

    toks, prose = [], []

    def flush():
        txt = clean_prose(' '.join(prose))
        prose.clear()
        if txt and toks:
            toks[-1]['comment'] = ((toks[-1]['comment'] + ' ') if toks[-1]['comment'] else '') + txt

    for line in raw.splitlines():
        if DIAGRAM.match(line):
            continue
        mv = MOVE_LINE.match(line)
        if not mv:
            s = line.strip()
            if (s and 'White:' not in s and not s.startswith('Black:')
                    and s.rstrip('.') != opening and not re.fullmatch(r'\(.*\)', s)):
                prose.append(s)
            continue
        flush()
        rest = PAGE.sub('', mv.group(2)).strip()
        for tok in [x.strip() for x in re.split(r'\s{2,}', rest) if x.strip()]:
            if tok.startswith('.'):          # '...' = kein Zug dieser Seite
                continue
            if ENDMARK.match(tok):           # 'Resigns' ist das Ergebnis, kein Zug
                continue
            toks.append({'tok': normalise(tok), 'comment': None})
    flush()
    if not toks:
        raise ValueError('keine Zuege gefunden')

    board = chess.Board()
    sans, tries, deepest = [], [0], [0]

    def play(i):
        deepest[0] = max(deepest[0], i)
        if i == len(toks):
            return True
        for m2 in resolve(board, toks[i]['tok']):
            tries[0] += 1
            if tries[0] > 20000:
                raise ValueError('Rueckverfolgung zu gross')
            san = board.san(m2)
            board.push(m2)
            sans.append(san)
            if play(i + 1):
                return True
            sans.pop()
            board.pop()
        return False

    if not play(0):
        i = deepest[0]
        raise ValueError(f'Zug {i + 1} von {len(toks)} ({toks[i]["tok"]!r}) passt in keiner Lesart')

    return dict(num=num, white=white, black=black, opening=opening,
                moves=[(sans[i], toks[i]['comment']) for i in range(len(sans))])


def to_pgn(g):
    lines = [f'[Event "Edward Lasker, Chess Strategy - Game {g["num"]}: {g["opening"]}"]',
             '[Site "Master tournament"]', '[Date "????.??.??"]', f'[Round "{g["num"]}.1"]',
             f'[White "{g["white"]}"]', f'[Black "{g["black"]}"]', '[Result "*"]',
             '[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]', '[SetUp "1"]',
             '[Annotator "Edward Lasker (Chess Strategy, 1915, public domain)"]', '']
    body = ['{[%info]}']
    for i, (san, com) in enumerate(g['moves']):
        if i % 2 == 0:
            body.append(f'{i // 2 + 1}.')
        body.append(san)
        if com:
            body.append('{' + com + '}')
    body.append('*')
    return '\n'.join(lines) + '\n' + ' '.join(body) + '\n'


if __name__ == '__main__':
    book = open(sys.argv[1], encoding='utf-8', errors='replace').read()
    want = int(sys.argv[3]) if len(sys.argv) > 3 else 10
    ok, fail = [], []
    for num, raw in split_games(book):
        try:
            g = parse_game(num, raw)
            ok.append(g)
            print(f'  ok   {num:>3}  {g["white"]} - {g["black"]:<22} {len(g["moves"]):>3} Halbzuege, '
                  f'{sum(1 for _, c in g["moves"] if c):>2} kommentiert')
        except Exception as exc:  # noqa: BLE001 — Fehler je Partie melden, nicht abbrechen
            fail.append((num, str(exc)))
            print(f'  --   {num:>3}  {exc}')
        if len(ok) >= want:
            break
    with open(sys.argv[2], 'w', encoding='utf-8') as fh:
        fh.write('\n\n'.join(to_pgn(g) for g in ok))
    print(f'\n{len(ok)} Partien geschrieben, {len(fail)} uebersprungen -> {sys.argv[2]}')
