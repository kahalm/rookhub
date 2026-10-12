import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { commentParagraphs, lineChapterTitle, lineLabel, lineMoveTokens, plyComment, plyPrettyLabel } from './repertoire-trainer-line-view.util';

describe('repertoire-trainer-line-view.util', () => {
  const [line] = parsePgnText('[Event "x"]\n[White " 1A | 2.Sf3 "]\n[Black "Französisch"]\n\n1. e4 e6 {Erst e6,\n\ndann d5.} 2. Nf3 d5 *');

  it('lineLabel / lineChapterTitle: getrimmte White-/Black-Kopfzeile, ohne Linie leer', () => {
    expect(lineLabel(line)).toBe('1A | 2.Sf3');
    expect(lineChapterTitle(line)).toBe('Französisch');
    expect(lineLabel(undefined)).toBe('');
    expect(lineChapterTitle(undefined)).toBe('');
  });

  it('lineMoveTokens: bis zum aktuellen Halbzug, nur mit revealCurrent auch der aktuelle', () => {
    expect(lineMoveTokens(line, 2, false).map(t => t.san)).toEqual(['e4', 'e6']);
    const shown = lineMoveTokens(line, 2, true);
    expect(shown.map(t => [t.num, t.san, t.state])).toEqual([[1, 'e4', 'past'], [null, 'e6', 'past'], [2, 'Nf3', 'current']]);
    expect(shown[1].comment).toContain('Erst e6');
    expect(lineMoveTokens(undefined, 0, true)).toEqual([]);
  });

  it('plyComment, commentParagraphs, plyPrettyLabel', () => {
    expect(plyComment(line, 1)).toBe('Erst e6,\n\ndann d5.');
    expect(plyComment(line, 0)).toBe('');
    expect(commentParagraphs(plyComment(line, 1))).toEqual(['Erst e6,', 'dann d5.']);
    expect(commentParagraphs('')).toEqual([]);
    expect(plyPrettyLabel(line, 1)).toBe('1… e6');
    expect(plyPrettyLabel(line, 9)).toBe('');
  });
});
