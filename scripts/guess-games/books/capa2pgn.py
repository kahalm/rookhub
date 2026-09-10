#!/usr/bin/env python3
"""Capablanca, 'Chess Fundamentals' (1921, gemeinfrei) -> PGN mit Zug-Kommentaren.

Das Buch schreibt die alte englische BESCHREIBENDE Notation ('P - Q 4', 'Kt - K B 3',
'B x B P') aus der Sicht des Ziehenden. Hier wird sie nicht uebersetzt, sondern AUFGELOEST:
zu jedem Token werden die legalen Zuege der Stellung gefiltert. Was uebrig bleibt, muss
eindeutig sein — sonst bricht die Partie mit Meldung ab, statt einen falschen Zug zu raten.
"""
import re, sys, chess

FILES = {'QR': 'a', 'QKT': 'b', 'QB': 'c', 'Q': 'd', 'K': 'e', 'KB': 'f', 'KKT': 'g', 'KR': 'h'}
AMBIG = {'R': 'ah', 'KT': 'bg', 'B': 'cf', 'Q': 'd', 'K': 'e', 'P': 'abcdefgh'}
PIECE = {'P': chess.PAWN, 'KT': chess.KNIGHT, 'N': chess.KNIGHT, 'B': chess.BISHOP,
         'R': chess.ROOK, 'Q': chess.QUEEN, 'K': chess.KING}

def dest_squares(spec, rank, white):
    """Beschreibende Feldangabe -> moegliche echte Felder."""
    spec = spec.upper().replace(' ', '')
    files = FILES.get(spec) or AMBIG.get(spec)
    if not files:
        return []
    r = rank if white else 9 - rank
    if not 1 <= r <= 8:
        return []
    return [chess.parse_square(f + str(r)) for f in files]

def split_piece(raw, default=chess.PAWN):
    """'K R' -> (Turm, 'k'); 'Q Kt' -> (Springer, 'q'); 'Kt' -> (Springer, None).

    Die Reihenfolge der Pruefungen ist der ganze Trick: 'Kt' IST der Springer, 'K R' ist der
    Turm der Koenigsseite. Wer nur auf den ersten Buchstaben schaut, macht aus jedem Springer
    eine Koenigsfigur — genau daran sind im ersten Anlauf alle 14 Partien gescheitert.
    """
    p = (raw or '').upper().replace(' ', '')
    if not p:
        return default, None
    if p in PIECE:
        return PIECE[p], None
    if p[0] in 'KQ' and p[1:] in PIECE:
        return PIECE[p[1:]], ('k' if p[0] == 'K' else 'q')
    if p[-1] in PIECE:
        return PIECE[p[-1]], None
    raise ValueError(f'Figur unlesbar: {raw!r}')

def resolve(board, tok):
    """Ein beschreibendes Token -> genau ein legaler Zug (oder Fehler).

    Zweistufig: HARTE Bedingungen (Figur, Zielfeld, Schlagen ja/nein, Umwandlung, Herkunftsdatei
    bei Bauern, ausgeschriebene Felder in Klammern) sieben die Kandidaten. Bleibt mehr als einer,
    entscheiden WEICHE Merkmale in dieser Reihenfolge: Schach/Matt, Seite der Figur, Seite des
    Ziels. Jedes weiche Merkmal wird nur angewandt, wenn danach noch ein Zug uebrig ist — die
    alte Notation nennt die Seite naemlich nach der HERKUNFT der Figur ('Q Kt'), nicht nach ihrem
    jetzigen Standort, und ein Springer, der schon dreimal gezogen ist, steht laengst woanders.
    """
    t = tok.strip().rstrip('.,;:').replace('\u2014', '-')
    t = re.sub(r'\s*\(\s*([QRBN])\s*\)\s*$', r'=\1', t)          # Umwandlung 'P - K 8 (Q)'
    t = re.sub(r'\s*=\s*([QRBN])\s*$', r'=\1', t)

    checkish = bool(re.search(r'\b(ch|mate)\b|[+#]', t, flags=re.I))
    t = re.sub(r'\b(dis\s+ch|ch|mate|e\.p\.)\b\.?', '', t, flags=re.I).strip()
    t = t.rstrip('+#!?').strip()
    white = board.turn == chess.WHITE

    if re.fullmatch(r'(O\s*-\s*O(\s*-\s*O)?|Castles(\s+(KR|QR))?)', t, flags=re.I):
        longside = bool(re.search(r'O\s*-\s*O\s*-\s*O|QR', t, flags=re.I))
        return [m for m in board.legal_moves
                if board.is_castling(m) and board.is_queenside_castling(m) == longside]

    promo = None
    m = re.search(r'=([QRBN])$', t)
    if m:
        promo = PIECE[m.group(1)]
        t = t[:m.start()].strip()

    # Ausgeschriebenes Feld in Klammern: 'R (K 2) - Q 2' (Herkunft) / 'Kt x P (K 3)' (Ziel)
    paren = None
    m = re.search(r'\(\s*((?:[KQ]\s*)?(?:R|Kt|N|B|Q|K)\s*[1-8])\s*\)', t, flags=re.I)
    if m:
        paren = m.group(1)
        t = (t[:m.start()] + t[m.end():]).strip()

    def squares_of(spec_and_rank):
        mm = re.fullmatch(r'((?:[KQ]\s*)?(?:R|KT|N|B|Q|K))\s*([1-8])',
                          spec_and_rank.upper().replace('N', 'KT') if spec_and_rank.upper().strip() == 'N'
                          else spec_and_rank.upper())
        if not mm:
            return []
        return dest_squares(mm.group(1), int(mm.group(2)), white)

    is_capture = ' x ' in f' {t} ' or bool(re.search(r'\sx\s|^\S+x', t))
    hard = []
    hint = None
    dest_hint_side = None

    if re.search(r'x', t, flags=re.I):
        m = re.fullmatch(r'(.*?)\s*x\s*(.*)', t, flags=re.I)
        mover_raw, victim_raw = m.group(1).strip(), m.group(2).strip()
        # Bauer mit Datei-Angabe: 'R P x B' = Randbauer schlaegt Laeufer
        pawn_files = None
        mv_norm = mover_raw.upper().replace(' ', '')
        if mv_norm.endswith('P') and len(mv_norm) > 1:
            pawn_files = FILES.get(mv_norm[:-1]) or AMBIG.get(mv_norm[:-1])
            mover, hint = chess.PAWN, None
        else:
            mover, hint = split_piece(mover_raw)
        vs = victim_raw.upper().replace(' ', '')
        vic_type = PIECE.get(vs)
        vic_files = None
        if vic_type is None and vs.endswith('P'):
            vic_type, vic_files = chess.PAWN, (FILES.get(vs[:-1]) or AMBIG.get(vs[:-1]))
        elif vic_type is None and vs[-1:] in PIECE:
            vic_type = PIECE[vs[-1:]]
        for mv in board.legal_moves:
            if board.piece_type_at(mv.from_square) != mover: continue
            if not board.is_capture(mv): continue
            if pawn_files and chess.square_name(mv.from_square)[0] not in pawn_files: continue
            cap = board.piece_type_at(mv.to_square)
            if cap is None and board.is_en_passant(mv): cap = chess.PAWN
            if vic_type and cap != vic_type: continue
            if vic_files and chess.square_name(mv.to_square)[0] not in vic_files: continue
            if promo and mv.promotion != promo: continue
            if paren and mv.to_square not in squares_of(paren): continue
            hard.append(mv)
    else:
        m = re.fullmatch(r'(.*?)\s*-\s*(.+)', t)
        if not m:
            raise ValueError(f'unlesbar: {tok!r}')
        mover_raw, field = m.group(1).strip(), m.group(2).strip()
        pawn_files = None
        mv_norm = mover_raw.upper().replace(' ', '')
        if mv_norm.endswith('P') and len(mv_norm) > 1:
            pawn_files = FILES.get(mv_norm[:-1]) or AMBIG.get(mv_norm[:-1])
            mover, hint = chess.PAWN, None
        else:
            mover, hint = split_piece(mover_raw)
        targets = squares_of(field)
        if not targets:
            raise ValueError(f'Zielfeld unlesbar: {tok!r}')
        if hint and len(targets) > 1:
            dest_hint_side = hint
        for mv in board.legal_moves:
            if board.piece_type_at(mv.from_square) != mover: continue
            if mv.to_square not in targets: continue
            if board.is_capture(mv): continue
            if promo and mv.promotion != promo: continue
            if pawn_files and chess.square_name(mv.from_square)[0] not in pawn_files: continue
            if paren and mv.from_square not in squares_of(paren): continue
            hard.append(mv)

    if not hard:
        return []
    if len(hard) == 1:
        return hard
    # Mehrere Lesarten: nach weichen Merkmalen SORTIEREN, nicht ausschliessen. Welche richtig
    # ist, entscheidet am Ende die Rueckverfolgung — falsch gelesen laeuft die Partie nach
    # wenigen Zuegen in einen illegalen Zug.
    def rang(m):
        r = 0
        if checkish and not board.gives_check(m): r += 4
        if hint and not from_side(board, m, hint): r += 2
        if dest_hint_side:
            f = chess.square_name(m.to_square)[0]
            if (f < 'e') if dest_hint_side == 'k' else (f > 'd'): r += 1
        if hint:   # die Dame-/Koenigsfigur ist die AEUSSERE ihrer Seite ('Q R' = a-Turm)
            ff = chess.square_name(m.from_square)[0]
            r += (ord(ff) - ord('a')) if hint == 'q' else (ord('h') - ord(ff))
        return r
    return sorted(hard, key=rang)


def from_side(board, mv, hint):
    """Kommt die Figur von der Koenigs- oder Damenseite? (grob nach Datei des Startfelds)"""
    f = chess.square_name(mv.from_square)[0]
    return (f >= 'e') if hint == 'k' else (f <= 'd')

# ---------------------------------------------------------------------------
# Buchtext -> Partien
# ---------------------------------------------------------------------------

MOVE_LINE = re.compile(r'^\s{2,}(\d+)\.\s+(.*)$')
PAGE = re.compile(r'\{\d+\}')

def clean_prose(text):
    text = PAGE.sub('', text)
    text = text.replace('[Illustration]', '')
    text = text.replace('{', '(').replace('}', ')')      # PGN-Kommentare duerfen keine Klammern tragen
    text = re.sub(r'(\s*\*){2,}\s*(Notes?)?\s*$', '', text, flags=re.I)   # '* * * * * Notes' = Trenner
    text = re.sub(r'^\s*\[\s*\]\s*', '', text)
    return re.sub(r'\s+', ' ', text).strip()

def split_games(book):
    """Der Abschnitt 'ILLUSTRATIVE GAMES' -> Liste von (Nummer, Titel, Rohtext)."""
    start = [m.start() for m in re.finditer(r'^GAME 1\. ', book, flags=re.M)][-1]
    # Nach der letzten Partie folgen Anhang, Fussnotenliste und der Gutenberg-Abspann — alles
    # davon wuerde sonst als Kommentar des letzten Zuges landen.
    tail = re.search(r'^\s*(APPENDIX|INDEX|\*\*\* END OF|End of Project Gutenberg|FOOTNOTES?\b|\[\d+\]\s)',
                     book[start:], flags=re.M)
    body = book[start:start + (tail.start() if tail else len(book))]
    parts = re.split(r'^GAME (\d+)\.\s*(.*)$', body, flags=re.M)[1:]
    return [(int(parts[i]), parts[i+1].strip(), parts[i+2]) for i in range(0, len(parts), 3)]

ENDMARK = re.compile(r'^(resigns?|drawn|draw|mate|wins?|black resigns?|white resigns?|'
                     r'and wins?|and black wins?|and white wins?|abandoned)\b', re.I)

def parse_game(num, title, raw):
    """Zuege + Kommentare einer Partie. Kommentar gehoert zum LETZTEN Zug davor."""
    head = raw[:400]
    ev = re.search(r'\(([^)]*\d{4}[^)]*)\)', head)
    # 'White: F. J. Marshall. Black: J. R. Capablanca.' — die Namen tragen selbst Punkte,
    # deshalb wird an 'Black:' getrennt und nicht am ersten Punkt.
    white = black = '?'
    mline = re.search(r'White:\s*(.+)', head)
    if mline:
        rest = re.split(r'\s*Black:\s*', mline.group(1).strip(), maxsplit=1)
        white = rest[0].strip().rstrip('.').strip()
        if len(rest) > 1:
            black = rest[1].strip().rstrip('.').strip()

    toks = []            # [{tok, comment}]
    prose = []

    def flush():
        txt = clean_prose(' '.join(prose))
        prose.clear()
        if txt and toks:
            toks[-1]['comment'] = ((toks[-1]['comment'] + ' ') if toks[-1]['comment'] else '') + txt

    for line in raw.splitlines():
        m = MOVE_LINE.match(line)
        if not m:
            s = line.strip()
            if s and not s.startswith(('White:', 'Black:')) and not re.fullmatch(r'\(.*\)', s):
                prose.append(s)
            continue
        flush()
        rest = PAGE.sub('', m.group(2)).strip()
        for tok in [x.strip() for x in re.split(r'\s{2,}', rest) if x.strip()]:
            if tok.startswith('.'):                      # '........' = kein Zug dieser Seite
                continue
            if ENDMARK.match(tok):                       # 'Resigns.' ist das Ergebnis, kein Zug
                continue
            toks.append({'tok': tok, 'comment': None})
    flush()

    board = chess.Board()
    sans = []
    tries = [0]
    tiefste = [0]

    def play(i):
        if i > tiefste[0]:
            tiefste[0] = i
        if i == len(toks):
            return True
        cands = resolve(board, toks[i]['tok'])
        for mv in cands:
            tries[0] += 1
            if tries[0] > 20000:
                raise ValueError('Rueckverfolgung zu gross')
            san = board.san(mv)
            board.push(mv)
            sans.append(san)
            if play(i + 1):
                return True
            sans.pop(); board.pop()
        return False

    if not play(0):
        i = tiefste[0]
        raise ValueError(f'Zug {i + 1} von {len(toks)} ({toks[i]["tok"]!r}) passt in keiner Lesart')

    moves = [(sans[i], toks[i]['comment']) for i in range(len(sans))]
    return dict(num=num, title=title, event=(ev.group(1) if ev else 'Chess Fundamentals'),
                white=white, black=black, moves=moves, result='*')


def to_pgn(g, info_marker=True):
    lines = [f'[Event "Capablanca, Chess Fundamentals - Game {g["num"]}: {g["title"]}"]',
             f'[Site "{g["event"]}"]', '[Date "????.??.??"]', f'[Round "{g["num"]}.1"]',
             f'[White "{g["white"]}"]', f'[Black "{g["black"]}"]', '[Result "*"]',
             '[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]', '[SetUp "1"]',
             f'[Annotator "Jose Raul Capablanca (Chess Fundamentals, 1921, public domain)"]', '']
    body = []
    if info_marker:
        body.append('{[%info]}')
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
    for num, title, raw in split_games(book):
        try:
            g = parse_game(num, title, raw)
            if len(g['moves']) < 10:
                raise ValueError(f'nur {len(g["moves"])} Zuege erkannt')
            ok.append(g)
        except Exception as e:
            fail.append((num, title, str(e)))
    print(f'umgewandelt: {len(ok)}, gescheitert: {len(fail)}')
    for n, t, e in fail:
        print(f'  Partie {n} ({t}): {e}')
    for g in ok[:want]:
        k = sum(1 for _, c in g['moves'] if c)
        print(f'  Partie {g["num"]:2}: {len(g["moves"]):3} Halbzuege, {k:2} Kommentare — {g["white"]} vs {g["black"]}')
    with open(sys.argv[2], 'w', encoding='utf-8') as f:
        for g in ok[:want]:
            f.write(to_pgn(g) + '\n')
    print('geschrieben:', sys.argv[2])
