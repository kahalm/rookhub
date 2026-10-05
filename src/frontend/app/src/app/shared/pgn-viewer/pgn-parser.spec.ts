import { foldVariationsIntoComments, parsePgnText, parsePgnTextWithSource, ParsedGame, splitPgnGames, START_FEN } from './pgn-parser';

const SINGLE_GAME = `[Event "Test"]
[White "Kasparov"]
[Black "Karpov"]
[Result "1-0"]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0`;

const MULTI_GAME = `[Event "Game 1"]
[White "Player A"]
[Black "Player B"]
[Result "1-0"]

1. e4 e5 1-0

[Event "Game 2"]
[White "Player C"]
[Black "Player D"]
[Result "0-1"]

1. d4 d5 2. c4 e6 0-1`;

describe('parsePgnText', () => {
  it('should parse a single game', () => {
    const games = parsePgnText(SINGLE_GAME);
    expect(games.length).toBe(1);
    expect(games[0].headers['White']).toBe('Kasparov');
    expect(games[0].headers['Black']).toBe('Karpov');
    expect(games[0].moves.length).toBe(6);
  });

  it('should precompute FEN positions', () => {
    const games = parsePgnText(SINGLE_GAME);
    expect(games[0].fens.length).toBe(7);
    expect(games[0].fens[0]).toBe(START_FEN);
  });

  it('should parse multiple games', () => {
    const games = parsePgnText(MULTI_GAME);
    expect(games.length).toBe(2);
    expect(games[0].headers['White']).toBe('Player A');
    expect(games[1].headers['White']).toBe('Player C');
    expect(games[0].moves.length).toBe(2);
    expect(games[1].moves.length).toBe(4);
  });

  it('should handle empty string', () => {
    expect(parsePgnText('').length).toBe(0);
  });

  it('should skip invalid PGN', () => {
    expect(parsePgnText('not valid pgn %%%').length).toBe(0);
  });

  it('should skip invalid games in mixed input', () => {
    const mixed = SINGLE_GAME + '\n\n[Event "Bad"]\n\n1. Zz9 ???';
    const games = parsePgnText(mixed);
    expect(games.length).toBe(1);
  });
});

describe('annotated PGN', () => {
  const ANNOTATED_PGN = `[Event "Repertoire"]
[White "Nimzo-Indian"]
[Black "Guide"]
[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]
[Result "*"]

{[%tqu "En","find the move","","","d2d4","",10]} 1. d4 Nf6 2. c4 e6 3. Nc3 {Comment.} Bb4 4. Qc2 O-O 5. e4 ({A)} 5.a3 Bxc3+ 6.Qxc3) ({B)} 5.Nf3) c5 6. e5 (6.a3 Bxc3+ 7.bxc3) Ne8 *`;

  it('should parse PGN with RAV variations', () => {
    const games = parsePgnText(ANNOTATED_PGN);
    expect(games.length).toBe(1);
    // Main line: 1.d4 Nf6 2.c4 e6 3.Nc3 Bb4 4.Qc2 O-O 5.e4 c5 6.e5 Ne8 = 12 half-moves
    expect(games[0].moves.length).toBe(12);
    expect(games[0].moves[0].san).toBe('d4');
    expect(games[0].moves[11].san).toBe('Ne8');
  });

  it('should strip NAG symbols', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 e5 2. Nf3 $1 Nc6 $14 *`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].moves.length).toBe(4);
  });

  it('should handle nested variations', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 (1. d4 d5 (1...Nf6 2. c4)) e5 2. Nf3 *`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].moves.length).toBe(3);
    expect(games[0].moves[0].san).toBe('e4');
    expect(games[0].moves[1].san).toBe('e5');
    expect(games[0].moves[2].san).toBe('Nf3');
  });
});

describe('comments', () => {
  it('should extract comments and associate with moves', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 {Best move} e5 {Solid reply} 2. Nf3 *`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].comments[0]).toBe('Best move');
    expect(games[0].comments[1]).toBe('Solid reply');
    expect(games[0].comments[2]).toBeUndefined();
  });

  it('should strip Chessbase annotations from comments', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 {[%csl Ge4]A strong move.} e5 {[%cal Re5e4]} *`;
    const games = parsePgnText(pgn);
    expect(games[0].comments[0]).toBe('A strong move.');
    expect(games[0].comments[1]).toBeUndefined(); // only annotation, no text
  });

  it('should handle comment before first move', () => {
    const pgn = `[Event "Test"]
[Result "*"]

{Starting comment} 1. e4 e5 *`;
    const games = parsePgnText(pgn);
    expect(games[0].comments[-1]).toBe('Starting comment');
  });

  it('should return empty comments for PGN without comments', () => {
    const games = parsePgnText(SINGLE_GAME);
    expect(Object.keys(games[0].comments).length).toBe(0);
  });

  it('parses a game with an unterminated comment brace instead of dropping it', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 e5 {unterminated comment`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].moves.length).toBe(2);
  });
});

describe('START_FEN', () => {
  it('should be the standard starting position', () => {
    expect(START_FEN).toBe('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1');
  });
});

describe('parsePgnText input limits', () => {
  it('caps the number of parsed games (no unbounded work)', () => {
    const game = '[Event "G"]\n\n1. e4 e5 *';
    const many = Array.from({ length: 600 }, () => game).join('\n\n');
    const games = parsePgnText(many);
    expect(games.length).toBe(500); // MAX_GAMES
  });

  it('skips a single pathologically large game instead of freezing', () => {
    const huge = '[Event "X"]\n\n{' + 'a'.repeat(200_001) + '} 1. e4 *';
    const games = parsePgnText(huge);
    expect(games.length).toBe(0);
  });
});

describe('parsePgnTextWithSource', () => {
  it('liefert den Originaltext je Spiel — auch hinter einem übersprungenen Spiel passt der Index', () => {
    const pgn = '[Event "A"]\n[Result "*"]\n\n1. e4 (1. d4 {Damen}) e5 {[%alt c5]gut} *\n\n'
      + '[Event "Kaputt"]\n[Result "*"]\n\n1. Kz9 *\n\n'
      + '[Event "B"]\n[Result "*"]\n\n1. c4 *\n';

    const parsed = parsePgnTextWithSource(pgn);

    expect(parsed.map(p => p.game.headers['Event'])).toEqual(['A', 'B']);
    expect(parsed[0].raw).toBe('[Event "A"]\n[Result "*"]\n\n1. e4 (1. d4 {Damen}) e5 {[%alt c5]gut} *');
    expect(parsed[1].raw).toBe('[Event "B"]\n[Result "*"]\n\n1. c4 *');
    expect(parsePgnText(pgn).length).toBe(2);             // alte Funktion unverändert
  });
});

describe('Partie-Trennung wie der Server (CRLF, BOM, ohne Leerzeile)', () => {
  // Drei Linien, wie sie ein Windows-/ChessBase-Export ablegt. Der Server (PgnMoveTree.ParseSections)
  // sieht in allen Schreibweisen drei Abschnitte; der Client muss dieselben drei in derselben Reihenfolge
  // liefern, sonst meint der gameIndex der Stellungssuche eine andere Linie.
  const lines = [
    '[Event "Rep"]\n[White "L1"]\n[Black "Kap"]\n[Result "*"]\n\n1. e4 e5 2. Nf3 *',
    '[Event "Rep"]\n[White "L2"]\n[Black "Kap"]\n[Result "*"]\n\n1. d4 d5 *',
    '[Event "Rep"]\n[White "L3"]\n[Black "Kap"]\n[Result "*"]\n\n1. c4 *',
  ];
  const names = (pgn: string) => parsePgnText(pgn).map(g => g.headers['White']);
  const moveCounts = (pgn: string) => parsePgnText(pgn).map(g => g.moves.length);

  it('LF mit Leerzeile (bisheriger Fall) bleibt bei drei Linien', () => {
    expect(names(lines.join('\n\n'))).toEqual(['L1', 'L2', 'L3']);
  });

  it('CRLF-Datei liefert alle drei Linien statt nur der letzten', () => {
    const crlf = lines.join('\n\n').replace(/\n/g, '\r\n') + '\r\n';
    expect(names(crlf)).toEqual(['L1', 'L2', 'L3']);
    expect(moveCounts(crlf)).toEqual([3, 2, 1]);
  });

  it('BOM vor einer CRLF-Datei stört nicht', () => {
    const bom = '\uFEFF' + lines.join('\n\n').replace(/\n/g, '\r\n');
    expect(names(bom)).toEqual(['L1', 'L2', 'L3']);
    expect(parsePgnText(bom)[0].headers['Event']).toBe('Rep');
  });

  it('ohne Leerzeile zwischen den Partien wird trotzdem an jedem [Event getrennt', () => {
    expect(names(lines.join('\n'))).toEqual(['L1', 'L2', 'L3']);
    expect(names(lines.join('\n').replace(/\n/g, '\r\n'))).toEqual(['L1', 'L2', 'L3']);
  });

  it('alte Mac-Zeilenenden (nur CR) werden ebenso gelesen', () => {
    expect(names(lines.join('\n\n').replace(/\n/g, '\r'))).toEqual(['L1', 'L2', 'L3']);
  });

  it('der Originaltext je Partie kommt mit LF zurück', () => {
    const parsed = parsePgnTextWithSource(lines.join('\n\n').replace(/\n/g, '\r\n'));
    expect(parsed.length).toBe(3);
    expect(parsed[1].raw).toBe(lines[1]);
  });

  it('splitPgnGames (Trenner für Einzel-Parser wie die Flashcards) liefert dieselben drei Abschnitte', () => {
    const blocks = splitPgnGames('\uFEFF' + lines.join('\n').replace(/\n/g, '\r\n'));
    expect(blocks.length).toBe(3);
    expect(blocks.map(b => b.trim())).toEqual(lines);
  });
});

const REAL_LINE = '[Event "Lifetime Repertoires: Martinovićs Französisch"]\n[Round "004.002"]\n'
  + '[White "1A | 2.Sf3 | Weiß spielt 6.Lxa3"]\n[Black "1) Weiß spielt ohne 2.d4"]\n'
  + '[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]\n[Result "*"]\n\n'
  + '{Liebe Schachfreunde. Es gibt nichts weniger kritisches als wenn unser Gegner nicht ausnutzt, dass er \n2.d4\n'
  + 'spielen kann.} 1. e4 {[%tqu "En","find the move","","","e7e6","",10]} e6 {[%cal Gd7d5][%alt c5 e5]Bereits '
  + 'nach diesem Zug.} 2. Nf3 {Dieser Zug hat fast keine eigenständige Bedeutung.} d5 3. e5 (3.Nc3 Nf6 4.e5 Nfd7 5.d4 '
  + '{werden wir später sehen.}) (3.exd5 exd5 4.d4 {geht über in die Abtauschvariante.}) (3.d3 {ist hier in der '
  + 'Zugfolge }) {2.d3 d5 3.Nf3 analysiert.} c5 4. b4 {[%cal Bb4c5][%csl Rc5]} ({Dieses Gambit nach 2.Sf3.} 4.c3 '
  + '{ist besser:} 4...Nc6 5.d4 {würde überleiten.}) cxb4 {[%alt b6]Es gibt auch gute Alternativen.} 5. a3 *\n';

describe('Varianten als Kommentartext (foldVariations)', () => {
  it('foldVariationsIntoComments: Varianten werden zu Text-Kommentaren, Marker und Klammern fallen weg', () => {
    expect(foldVariationsIntoComments('1. e4 (1. d4 {Damen} $1 (1. c4)) e5 {[%alt c5]gut} *'))
      .toBe('1. e4  {1. d4 Damen 1. c4}  e5 {[%alt c5]gut} *');
    expect(foldVariationsIntoComments('1. e4 {Klammer (im) Kommentar} e5 *')).toBe('1. e4 {Klammer (im) Kommentar} e5 *');
    expect(foldVariationsIntoComments('1. e4 ( $1 ) e5 *')).toBe('1. e4  e5 *');   // leere Variante fällt weg
  });

  it('echte Chessable-Linie: Varianten hängen am Zug, Züge bleiben, alle Hauptzüge gelesen', () => {
    const [game] = parsePgnText(REAL_LINE, { foldVariations: true });
    expect(game.moves.map(m => m.san)).toEqual(['e4', 'e6', 'Nf3', 'd5', 'e5', 'c5', 'b4', 'cxb4', 'a3']);
    expect(game.comments[4]).toBe('3.Nc3 Nf6 4.e5 Nfd7 5.d4 werden wir später sehen. 3.exd5 exd5 4.d4 geht über in die '
      + 'Abtauschvariante. 3.d3 ist hier in der Zugfolge 2.d3 d5 3.Nf3 analysiert.');
    expect(game.comments[6]).toBe('Dieses Gambit nach 2.Sf3. 4.c3 ist besser: 4...Nc6 5.d4 würde überleiten.');
    expect(game.comments[7]).toBe('Es gibt auch gute Alternativen.');
  });

  it('aufeinanderfolgende Kommentare verwerfen das Spiel nicht mehr (auch ohne Einfalten)', () => {
    const [game] = parsePgnText('[Event "x"]\n[Result "*"]\n\n{Intro} {mehr} 1. e4 {[%cal Ge2e4]} {Text} e5 *\n');
    expect(game.moves.map(m => m.san)).toEqual(['e4', 'e5']);
    expect(game.comments[-1]).toBe('Intro mehr');
    expect(game.comments[0]).toBe('Text');
  });

  it('ohne die Option bleibt es beim Verwerfen der Varianten', () => {
    const [game] = parsePgnText(REAL_LINE);
    expect(game.comments[4]).toBe('2.d3 d5 3.Nf3 analysiert.');
    expect(game.comments[6]).toBeUndefined();
  });
});

describe('Chessables Null-Zug („--")', () => {
  // Einleitungs-/Erklärlinien enden bei Chessable mit einem Null-Zug. chess.js kennt ihn nicht und
  // verwarf früher die GANZE Partie — die Linie fehlte in Repertoire-Ansicht und Zugliste.
  const INTRO = '[Event "The Gold Standard 1.e4"]\n[Round "002.002"]\n[White "Introduction"]\n'
    + `[FEN "${START_FEN}"]\n[Result "*"]\n\n`
    + '1. e4 {Hallo und willkommen zum Kurs.} 1... -- {Hier geht es weiter mit 1.d4 als Vergleich.} *\n';

  it('die Linie wird gelesen, der Kommentar dahinter bleibt erhalten', () => {
    const [game] = parsePgnText(INTRO);
    expect(game).toBeTruthy();
    expect(game.moves.map(m => m.san)).toEqual(['e4']);
    expect(game.comments[0]).toBe('Hallo und willkommen zum Kurs. Hier geht es weiter mit 1.d4 als Vergleich.');
  });

  it('auch mit eingefalteten Varianten und ohne Zugnummer vor dem Null-Zug', () => {
    const pgn = '[Event "x"]\n[Result "*"]\n\n1. e4 {Intro} -- ({Vergleich} 1.d4 d5) {Schluss} *\n';
    const [game] = parsePgnText(pgn, { foldVariations: true });
    expect(game.moves.map(m => m.san)).toEqual(['e4']);
    expect(game.comments[0]).toContain('Vergleich 1.d4 d5');
    expect(game.comments[0]).toContain('Schluss');
  });

  it('ein Gedankenstrich im Kommentar bleibt stehen', () => {
    const [game] = parsePgnText('[Event "x"]\n[Result "*"]\n\n1. e4 {kurz -- und knapp} e5 *\n');
    expect(game.moves.map(m => m.san)).toEqual(['e4', 'e5']);
    expect(game.comments[0]).toBe('kurz -- und knapp');
  });

  it('Rochaden und Ergebnisse werden nicht angetastet', () => {
    const [game] = parsePgnText('[Event "x"]\n[Result "1/2-1/2"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. O-O Nf6 5. d3 O-O 1/2-1/2\n');
    expect(game.moves.map(m => m.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bc4', 'Bc5', 'O-O', 'Nf6', 'd3', 'O-O']);
  });
});
