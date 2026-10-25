import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import {
  TrainerLineSet, byRank, countUserMoves, extendQueue, hasUserMove, lineKeyOf, opponentLines, playableLines,
} from './repertoire-trainer-queue.util';
import { lineKeyFromSans } from './repertoire-line-key.util';

const PGN = [
  '[Event "R"]', '[White "A"]', '[Black "W1"]', '', '1. e4 e5 2. Nf3 *', '',
  '[Event "R"]', '[White "B"]', '[Black "W1"]', '', '1. d4 d5 2. c4 *', '',
  '[Event "R"]', '[White "C"]', '[Black "S1"]', '', '1. e4 c5 2. Nf3 d6 *', '',
  '[Event "R"]', '[White "D"]', '[Black "S1"]', '', '1. e4 *', '',        // für Schwarz nichts zu üben
].join('\n');
const LINES = parsePgnText(PGN);
const names = (ls: { headers: Record<string, string> }[]) => ls.map(l => l.headers['White']);
const set = (chapter: string | null = null): TrainerLineSet => ({
  lines: LINES, chapter, colorOf: l => (l.headers['Black'] === 'S1' ? 'b' : 'w'),
});
const KEY = (n: string) => lineKeyOf(LINES.find(l => l.headers['White'] === n)!);

describe('repertoire-trainer-queue.util', () => {
  it('lineKeyOf = Schlüssel der Linienliste', () => {
    expect(lineKeyOf(LINES[0])).toBe(lineKeyFromSans(['e4', 'e5', 'Nf3']));
  });

  it('playableLines: Kapitel-Filter, Einzellinie, nur Linien mit eigenem Zug', () => {
    expect(names(playableLines(set()))).toEqual(['A', 'B', 'C']);
    expect(names(playableLines(set('S1')))).toEqual(['C']);
    expect(names(playableLines(set(), KEY('B')))).toEqual(['B']);
    expect(hasUserMove(set(), LINES[3])).toBeFalse();
  });

  it('countUserMoves zählt je Linie die Züge ihrer Farbe', () => {
    expect(countUserMoves(set(), LINES.slice(0, 3))).toBe(2 + 2 + 2);
  });

  it('byRank: bekannte nach Platz, unbekannte dahinter in alter Reihenfolge', () => {
    const rank = new Map([[KEY('C'), 0], [KEY('A'), 1]]);
    expect(names(byRank(LINES.slice(0, 3), rank))).toEqual(['C', 'A', 'B']);
  });

  it('opponentLines: Farbe, Reihung und Einstieg', () => {
    const rank = new Map([[KEY('B'), 0], [KEY('A'), 1]]);
    expect(names(opponentLines(set(), { color: 'w', rank, start: null }))).toEqual(['B', 'A']);
    expect(names(opponentLines(set(), { color: 'w', rank, start: KEY('A') }))).toEqual(['A']);
    expect(names(opponentLines(set(), { color: null, rank: null, start: KEY('C') }))).toEqual(['C']);
    expect(names(opponentLines(set(), { color: null, rank: null, start: null }))).toEqual(['A', 'B', 'C']);
    expect(names(opponentLines(set(), { color: 'w', rank, start: 'unbekannt' }))).toEqual(['B', 'A']);
  });

  it('extendQueue: laufende Linie bleibt, danach die nach ihr in der Reihung', () => {
    const [a, b, c] = LINES;
    expect(extendQueue([b], 0, [a, b, c])).toEqual([b, c]);
    expect(extendQueue([b], 0, [a, c])).toEqual([b, a, c]);
    expect(extendQueue([], 0, [a, c])).toEqual([a, c]);
  });
});
