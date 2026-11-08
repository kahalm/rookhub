import { moveLabel, parseInline, parseSummary, positionsOf } from './summary-markdown.util';

describe('summary-markdown.util (Partie-Zusammenfassung, 0.913.0)', () => {
  it('zerlegt Überschriften, Absätze, Listen, Zitat, Trennlinie und Tabelle', () => {
    const md = [
      '# Partie 236', '', 'Erster Satz', 'geht weiter.', '', '## Fehler', '- eins', '- **zwei**', '1. a', '2. b',
      '> **Merksatz:** Loch mit Leichtfigur', '---', '| Zug | Bewertung |', '|---|---|', '| 22.Td6 | +1,7 |',
    ].join('\n');
    const b = parseSummary(md);
    expect(b.map(x => x.kind)).toEqual(['heading', 'paragraph', 'heading', 'list', 'list', 'quote', 'rule', 'table']);
    expect(b[1]).toEqual({ kind: 'paragraph', parts: [{ text: 'Erster Satz geht weiter.' }] });
    expect((b[3] as { ordered: boolean }).ordered).toBeFalse();
    expect((b[4] as { ordered: boolean }).ordered).toBeTrue();
    const t = b[7] as { head: unknown[]; rows: unknown[][] };
    expect(t.head.length).toBe(2);
    expect(t.rows[0][0]).toEqual([{ text: '22.Td6' }]);
  });

  it('liest diagram-Blöcke mit ply, Pfeilen, Unterschrift und Blickrichtung; ohne Stellung kein Diagramm', () => {
    const b = parseSummary('```diagram\nply: 43\narrows: c1a3, a3d6 zz99\ncaption: d6 ist ein **Loch**\norientation: black\n```\n'
      + '```diagram\ncaption: nichts\n```');
    expect(b.length).toBe(1);
    expect(b[0]).toEqual({ kind: 'diagram', spec: {
      ply: 43, arrows: [{ from: 'c1', to: 'a3' }, { from: 'a3', to: 'd6' }],
      caption: [{ text: 'd6 ist ein ' }, { text: 'Loch', bold: true }], orientation: 'black' } });
  });

  it('Inline: fett, kursiv, Code; Zugnotation mit Sternchen bleibt Text', () => {
    expect(parseInline('a **b** *c* _d_ `e`')).toEqual([
      { text: 'a ' }, { text: 'b', bold: true }, { text: ' ' }, { text: 'c', italic: true }, { text: ' ' },
      { text: 'd', italic: true }, { text: ' ' }, { text: 'e', code: true }]);
    expect(parseInline('9...Sf8 und 10.Se5')).toEqual([{ text: '9...Sf8 und 10.Se5' }]);
  });

  it('HTML im Text bleibt Text (wird nie als HTML gerendert)', () => {
    expect(parseSummary('<img src=x onerror=alert(1)>')).toEqual([{ kind: 'paragraph', parts: [{ text: '<img src=x onerror=alert(1)>' }] }]);
  });

  it('positionsOf: Stellung nach n Halbzügen, letzter Zug und SAN; kaputtes PGN = nur Anfang', () => {
    const p = positionsOf('1. e4 e5 2. Nf3 *');
    expect(p.fens.length).toBe(4);
    expect(p.fens[3].startsWith('rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b')).toBeTrue();
    expect(p.lastMoves[3]).toEqual(['g1', 'f3']);
    expect(p.sans).toEqual(['e4', 'e5', 'Nf3']);
    expect(positionsOf('1. e5 xx').fens.length).toBe(1);
  });

  it('moveLabel: Weiß „n.“, Schwarz „n…“', () => {
    expect(moveLabel(43, 'Td6')).toBe('22.Td6');
    expect(moveLabel(44, 'c5')).toBe('22…c5');
    expect(moveLabel(0)).toBe('');
  });
});
