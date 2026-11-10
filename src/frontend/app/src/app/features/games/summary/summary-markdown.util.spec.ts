import { InlinePart, linkSummaryMoves, moveLabel, parseInline, parseSummary, positionsOf, visibleSummaryBlocks } from './summary-markdown.util';

describe('summary-markdown.util (Partie-Zusammenfassung, 0.913.0)', () => {
  it('zerlegt Überschriften, Absätze, Listen, Zitat, Trennlinie und Tabelle', () => {
    const md = [
      '# Partie 236', '', 'Erster Satz', 'geht weiter.', '', '## Fehler', '- eins', '- **zwei**', '1. a', '2. b',
      '> Ein Zitat', '---', '| Zug | Bewertung |', '|---|---|', '| 22.Td6 | +1,7 |',
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

  it('offener Code-Zaun ohne Abschluss endet an der Leerzeile, der Rest bleibt Text (F2-018)', () => {
    const md = ['Vorher.', '', '```diagram', 'ply: 4', 'caption: Nach 2…Sc6', '', 'Danach geht es weiter.', '', '## Fehler', '- eins'].join('\n');
    const b = parseSummary(md);
    expect(b.map(x => x.kind)).toEqual(['paragraph', 'diagram', 'paragraph', 'heading', 'list']);
    expect(b[2]).toEqual({ kind: 'paragraph', parts: [{ text: 'Danach geht es weiter.' }] });
    const code = parseSummary(['```', 'x = 1', '', 'Text danach.'].join('\n'));
    expect(code.map(x => x.kind)).toEqual(['paragraph', 'paragraph']);
    expect(code[1]).toEqual({ kind: 'paragraph', parts: [{ text: 'Text danach.' }] });
    // geschlossene Blöcke wie bisher — auch mit Leerzeile darin
    const closed = parseSummary(['```', 'a', '', 'b', '```', 'Ende.'].join('\n'));
    expect(closed.length).toBe(2);
    expect((closed[0] as { parts: { text: string }[] }).parts[0].text).toBe('a\n\nb');
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

describe('parseSummary: Merksatz und Literatur (Wunsch 2026-11-09)', () => {
  it('Merksatz-Zitat wird Merksatz-Block, anderes Zitat bleibt', () => {
    expect(parseSummary('> **Merksatz:** raum-und-tausch\n\n> Nur ein Zitat')).toEqual([
      { kind: 'maxim', value: 'raum-und-tausch' },
      { kind: 'quote', parts: [{ text: 'Nur ein Zitat' }] },
    ] as never);
  });
  it('book-Block trägt Kennung und Begründung, ohne Kennung fällt er weg', () => {
    expect(parseSummary('```book\nid: 1314\nwhy: Passt: zweimal Raum verschenkt.\n```\n\n```book\nwhy: x\n```'))
      .toEqual([{ kind: 'book', id: 1314, why: 'Passt: zweimal Raum verschenkt.' }]);
  });
});

describe('visibleSummaryBlocks (Literatur je Seite, Wunsch 2026-11-09)', () => {
  const md = '## Was man mitnehmen kann\n\nText\n\n## Empfohlene Literatur\n\n### Für Weiß\n\n```book\nid: 1\nwhy: a\n```\n\n'
    + '### Für Schwarz\n\n```book\nid: 2\nwhy: b\n```\n';
  const headings = (ids: number[]) => visibleSummaryBlocks(parseSummary(md), b => ids.includes(b.id))
    .filter(b => b.kind === 'heading').map(b => b.kind === 'heading' ? b.parts[0].text : '');

  it('blendet nur die leere Seite aus', () => {
    expect(headings([2])).toEqual(['Was man mitnehmen kann', 'Empfohlene Literatur', 'Für Schwarz']);
  });
  it('ohne Bücher verschwindet auch die Hauptüberschrift', () => {
    expect(headings([])).toEqual(['Was man mitnehmen kann']);
  });
  it('alle Bücher da: alles bleibt', () => {
    expect(headings([1, 2]).length).toBe(4);
  });
  it('alte Fassung ohne Unterüberschriften und leere Abschnitte', () => {
    const flat = parseSummary('## Empfohlene Literatur\n\n```book\nid: 1\nwhy: a\n```\n\n## Leer\n');
    expect(visibleSummaryBlocks(flat, () => false).map(b => b.kind)).toEqual(['heading']);
    expect(visibleSummaryBlocks(flat, () => true).length).toBe(3);
  });
});

describe('linkSummaryMoves (klickbare Züge)', () => {
  const pos = positionsOf('1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *');
  const para = (md: string) => {
    const b = linkSummaryMoves(parseSummary(md), pos)[0];
    return b.kind === 'paragraph' ? b.parts : [];
  };

  it('Partiezug vor Variante, auch nach einer genannten Variante', () => {
    const p = para('Statt 3.Lb5 besser 3.Lc4 Sf6 und dann 3...a6 im Spiel.').filter(x => x.line);
    expect(p.map(x => x.text)).toEqual(['3.Lb5', '3.Lc4', 'Sf6', '3...a6']);
    expect(p[0].line!.map(m => m.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5']);
    expect(p[1].line!.map(m => m.san).slice(-3)).toEqual(['Nc6', 'Bc4', 'Nf6']);
    expect(p[3].line!.length).toBe(6);
  });

  it('eine spätere Folge hängt an der zuletzt genannten Variante, die Lücke füllt der zuletzt genannte Zug', () => {
    const p = para('Nach 3.Lb5 a6 ist alles normal. Mit 3.Lc4 dagegen geht 4.Lxf7+ nicht.').filter(x => x.line);
    expect(p.map(x => x.text)).toEqual(['3.Lb5', 'a6', '3.Lc4', '4.Lxf7+']);
    expect(p[3].line!.map(m => m.san).slice(-3)).toEqual(['Bc4', 'a6', 'Bxf7+']);
    expect(p[3].at).toBe(7);
  });

  it('Illegales und Code bleiben Text, englische Buchstaben gehen', () => {
    expect(para('Hier 9.Dh5 und `2.Nf3`.').some(x => x.line)).toBeFalse();
    expect(para('Hier 2.Nf3 stark.').filter(x => x.line).map(x => x.at)).toEqual([3]);
  });
});

/**
 * Partie 243 (Meldung 10.11.2026: „Züge im Abschnitt ‚Was man mitnehmen kann‘ nicht klickbar“): die nummerierten Züge der
 * Liste waren klickbar (auch Abweichungen von der Partie), nur der Zug ohne Nummer bleibt Text. Nicht klickbar war die
 * Begründung der Literatur darunter — „35.axb5 und 45.Lxd5“ dort sind jetzt Züge.
 */
describe('linkSummaryMoves — Partie 243', () => {
  const pgn = '1. d4 d5 2. Nf3 Nf6 3. Bf4 e6 4. e3 c5 5. c3 Nc6 6. Nbd2 cxd4 7. exd4 Bd6 8. Bg3 Qc7 9. Bd3 O-O 10. O-O Bxg3 '
    + '11. hxg3 e5 12. dxe5 Nxe5 13. Nxe5 Qxe5 14. Re1 Qd6 15. Nb3 Bg4 16. Qd2 Rfe8 17. Nd4 Bh5 18. Bb5 Ne4 19. Qf4 Red8 '
    + '20. f3 Nf6 21. Re2 Bg6 22. Rae1 Qc5 23. g4 h6 24. Kh2 a6 25. Ba4 b5 26. Bb3 Qd6 27. Qxd6 Rxd6 28. Re5 Kf8 29. Kg3 Re8 '
    + '30. Rxe8+ Nxe8 31. Rd1 Nc7 32. Ne2 f6 33. Nf4 Bf7 34. a4 Ke7 35. axb5 axb5 36. Ra1 Kd7 37. Ne2 Bg6 38. Nd4 Bd3 '
    + '39. Ra7 Kc8 40. Kf2 Kb8 41. Ra1 Ne6 42. Ke3 Bg6 43. Rd1 Nxd4 44. Kxd4 Kc7 45. Bxd5 Bc2 46. Rd2 Bb3 47. Kc5 g5 1-0';
  const md = [
    '## Was man mitnehmen kann', '',
    '- Gehört einem die einzige offene Linie, ist das Einbruchsfeld das Ziel. e7 war im 21., 23. und 28. Zug frei.',
    '- Ein Damentausch ist nicht automatisch eine Vereinfachung zugunsten des Gegners: 21.Dxd6 hätte den Turm nach e7 gebracht.',
    '- Vor einem Bauerntausch am Flügel prüfen, ob der Vorstoß daneben stärker ist. 35.a5 hätte zwei Schwächen festgelegt, 35.axb5 hat eine davon beseitigt.',
    '- Im gewonnenen Endspiel erst den Turm aktivieren, dann den Bauern nehmen (45.Ta1 vor Lxd5).', '',
    '## Empfohlene Literatur', '',
    '```book', 'id: 7', 'why: Im besseren Endspiel wurde mit 35.axb5 und 45.Lxd5 jeweils sofort geerntet.', '```',
  ].join('\n');
  const blocks = linkSummaryMoves(parseSummary(md), positionsOf(pgn));
  const moves = (parts: InlinePart[]) => parts.filter(p => p.line).map(p => `${p.text}@${p.at}`);

  it('Listenpunkte: nummerierte Züge klickbar (Partiezug und Abweichung), Zugnummern-Text und Zug ohne Nummer bleiben Text', () => {
    const list = blocks[1] as { kind: 'list'; items: InlinePart[][] };
    expect(list.items.map(moves)).toEqual([[], ['21.Dxd6@41'], ['35.a5@69', '35.axb5@69'], ['45.Ta1@89']]);
    const ta1 = list.items[3].find(p => p.text === '45.Ta1')!;
    expect(ta1.line!.slice(0, 88).map(m => m.san)).toEqual(positionsOf(pgn).sans.slice(0, 88));
    expect(ta1.line![88].san).toBe('Ra1');
  });

  it('Begründung eines Buchs: Partiezüge sind klickbar, der Rohtext bleibt erhalten', () => {
    const book = blocks[3] as { kind: 'book'; why: string; whyParts?: InlinePart[] };
    expect(book.why).toContain('35.axb5 und 45.Lxd5');
    expect(moves(book.whyParts!)).toEqual(['35.axb5@69', '45.Lxd5@89']);
    expect(book.whyParts!.map(p => p.text).join('')).toBe(book.why);
  });
});
