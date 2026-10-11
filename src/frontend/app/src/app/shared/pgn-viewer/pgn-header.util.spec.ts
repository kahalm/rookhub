import { isPgnPlaceholder, pgnHeaderText } from './pgn-header.util';

// UI-Sweep 2026-10-11 (r2-rg-rep-question): Platzhalter-Kopfzeilen gelten in der Anzeige als fehlend.
describe('pgn-header.util', () => {
  it('erkennt Platzhalter', () => {
    expect(['?', ' ? ', '??', '-', '', null, undefined].map(isPgnPlaceholder)).toEqual([true, true, true, true, true, true, true]);
    expect(['Carlsen', '1A | 2.Sf3', '?!'].map(isPgnPlaceholder)).toEqual([false, false, false]);
  });

  it('liefert den getrimmten Wert oder leer', () => {
    expect(pgnHeaderText(' Italienisch ')).toBe('Italienisch');
    expect(pgnHeaderText('?')).toBe('');
  });
});
