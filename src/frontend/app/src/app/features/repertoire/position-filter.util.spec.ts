import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { findPositionInGames, formatSansWithNumbers, normalizeFen } from './position-filter.util';
import { applyUserMove, legalDests } from '../../shared/pgn-viewer/board-moves.util';

describe('position-filter.util', () => {
  it('normalizeFen behält Brett/Seite/Rochade und lässt ep + Zähler weg (Spiegel der Server-Normalisierung)', () => {
    expect(normalizeFen('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 3 12'))
      .toBe('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq');
    expect(normalizeFen('kaputt')).toBe('kaputt');
  });

  it('normalizeFen ignoriert das en-passant-Feld — dieselbe Stellung matcht mit und ohne', () => {
    // Der Server lässt ep ausdrücklich weg (RepertoirePositionLookupService.NormalizeKey), damit
    // Zugumstellungen matchen: dieselbe Stellung entsteht mit Doppelschritt (ep gesetzt) oder über
    // zwei Einzelschritte (ep „-"). Mit ep im Schlüssel filterte das Brett solche Linien auf 0
    // heraus („kommt nicht vor"), während „In welchen Repertoires?" sie fand.
    const withEp = 'rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3';
    const withoutEp = 'rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3';
    expect(normalizeFen(withEp)).toBe(normalizeFen(withoutEp));
  });

  const games = parsePgnText(
    '[Event "1"]\n[White "A"]\n[Black "a"]\n\n1. e4 e5 2. Nf3 Nc6 *\n\n' +
    '[Event "2"]\n[White "B"]\n[Black "b"]\n\n1. Nf3 Nc6 2. e4 e5 *\n\n' +
    '[Event "3"]\n[White "C"]\n[Black "c"]\n\n1. d4 d5 *');

  it('findet die Stellung in allen Linien inkl. Transposition', () => {
    // Stellung nach 1. e4 e5 2. Nf3 Nc6 — Linie B erreicht sie in anderer Zugfolge.
    const fen = games[0].fens[4];
    const res = findPositionInGames(games, fen);
    expect([...res.gameIndices].sort()).toEqual([0, 1]);
    expect(res.plyByGame.get(0)).toBe(4);
    expect(res.plyByGame.get(1)).toBe(4);
  });

  it('Zwischenstellungen matchen nur die Linien mit derselben Zugfolge', () => {
    const fen = games[0].fens[1]; // nach 1. e4
    const res = findPositionInGames(games, fen);
    expect([...res.gameIndices]).toEqual([0]);
    expect(res.plyByGame.get(0)).toBe(1);
  });

  it('Startstellung matcht jede Linie bei Ply 0', () => {
    const res = findPositionInGames(games, games[0].fens[0]);
    expect(res.gameIndices.size).toBe(3);
    expect(res.plyByGame.get(2)).toBe(0);
  });

  it('unbekannte Stellung matcht nichts', () => {
    const res = findPositionInGames(games, '8/8/8/8/8/8/8/K6k w - - 0 1');
    expect(res.gameIndices.size).toBe(0);
  });

  it('formatSansWithNumbers nummeriert die weißen Halbzüge', () => {
    expect(formatSansWithNumbers([])).toBe('');
    expect(formatSansWithNumbers(['e4'])).toBe('1. e4');
    expect(formatSansWithNumbers(['e4', 'e5', 'Nf3'])).toBe('1. e4 e5 2. Nf3');
  });
});

describe('board-moves.util', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  it('legalDests liefert die 20 Startzüge für Weiß', () => {
    const legal = legalDests(START)!;
    expect(legal.color).toBe('white');
    const total = [...legal.dests.values()].reduce((n, arr) => n + arr.length, 0);
    expect(total).toBe(20);
    expect(legal.dests.get('e2')).toEqual(jasmine.arrayContaining(['e3', 'e4']));
  });

  it('legalDests gibt bei kaputter FEN null zurück (Brett bleibt Anzeige)', () => {
    expect(legalDests('kein fen')).toBeNull();
    expect(legalDests('8/8/8/8/8/8/8/8 w - - 0 1')).toBeNull(); // ohne Könige illegal
  });

  it('applyUserMove wendet den Zug an und liefert SAN + Folge-FEN', () => {
    const applied = applyUserMove(START, 'e2', 'e4')!;
    expect(applied.san).toBe('e4');
    expect(applied.fen).toContain(' b ');
  });

  it('applyUserMove wandelt automatisch in eine Dame um', () => {
    const applied = applyUserMove('8/P6k/8/8/8/8/7K/8 w - - 0 1', 'a7', 'a8')!;
    expect(applied.san).toBe('a8=Q');
    expect(applied.fen.startsWith('Q7/')).toBeTrue();
  });

  it('applyUserMove gibt bei illegalem Zug null zurück', () => {
    expect(applyUserMove(START, 'e2', 'e5')).toBeNull();
  });
});
