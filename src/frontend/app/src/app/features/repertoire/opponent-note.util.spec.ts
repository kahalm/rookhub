import { Chess } from 'chess.js';
import { OpponentPositions, positionKey } from '../../shared/player-card/opponent-positions';
import { opponentNoteView } from './opponent-note.util';

describe('opponentNoteView (Anzeige „aus Sicht seiner Partien", 0.892.0)', () => {
  const sans = ['e4', 'c5', 'Nf3', 'd6'];
  const c = new Chess();
  const fens = [c.fen(), ...sans.map(s => { c.move(s); return c.fen(); })];
  const positions: OpponentPositions = {
    [positionKey(fens[0])]: { n: 14, y: 2024, m: {} },
    [positionKey(fens[1])]: { n: 14, y: 2024, m: { c5: 9, e6: 3 } },
    [positionKey(fens[2])]: { n: 9, y: 2023, m: {} },
  };
  const t = (k: string, p?: Record<string, unknown>) => `${k}|${JSON.stringify(p ?? {})}`;
  const base = { who: 'Maras', band: null as string | null, lang: 'de', t };

  it('erreicht + seine Züge', () => {
    const v = opponentNoteView(fens, sans, 1, positions, base)!;
    expect(v.left).toBeFalse();
    expect(v.text).toBe('opponentNote.reached|{"who":"Maras","n":14,"year":2024}');
    expect(v.moves).toBe('opponentNote.played|{"moves":"1…c5 9×, 1…e6 3×"}');
  });

  it('ab dem ersten Zug ohne Partien: mit Schätzung, ohne Schätzung (Repertoire) und im Abweichungs-Trainer (Engine)', () => {
    expect(opponentNoteView(fens, sans, 3, positions, { ...base, band: '2143–2443' })).toEqual(
      { text: 'opponentNote.leftEstimated|{"move":"2.Sf3","who":"Maras","band":"2143–2443"}', moves: null, left: true });
    expect(opponentNoteView(fens, sans, 4, positions, base)!.text).toBe('opponentNote.leftRepertoire|{"move":"2.Sf3","who":"Maras","band":null}');
    expect(opponentNoteView(fens, sans, 4, positions, { ...base, after: 'engine', band: 'x' })!.text).toContain('opponentNote.leftEngine');
  });

  it('ohne Namen „des Gegners", ohne Jahr eigener Text', () => {
    const v = opponentNoteView(fens, sans, 2, { ...positions, [positionKey(fens[2])]: { n: 2, y: null, m: {} } }, { ...base, who: null })!;
    expect(v.text).toBe('opponentNote.reachedNoYear|{"who":"opponentNote.thisOpponent|{}","n":2,"year":null}');
    expect(v.moves).toBeNull();
  });
});
