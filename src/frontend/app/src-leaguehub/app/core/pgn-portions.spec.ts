import { IMPORT_PORTION, partLabel, pgnPortions, portionNote, splitGameBlocks } from './pgn-portions';

describe('pgn-portions', () => {
  it('trennt wie der Server: Kopfzeile nach Zugtext, wiederholte Kopfzeile, nichts in einem offenen Kommentar', () => {
    const pgn = '﻿[Event "A"]\r\n[White "x"]\r\n\r\n1. e4 {ein Kommentar\r\n[White "kein Kopf"]\r\n} e5 *\r\n'
      + '[Event "B"]\n1. d4 *\n'                                      // ohne Leerzeile davor
      + '[Event "leer"]\n[Event "C"]\n\n1. c4 *\n'                     // eine Partie nur aus Kopfzeilen fällt weg
      + '[%evp 1,2]\n';                                                // tag-artig, keine Kopfzeile
    const blocks = splitGameBlocks(pgn);
    expect(blocks.length).toBe(3);
    expect(blocks[0]).toBe('[Event "A"]\n[White "x"]\n\n1. e4 {ein Kommentar\n[White "kein Kopf"]\n} e5 *');
    expect(blocks[1]).toBe('[Event "B"]\n1. d4 *');
    expect(blocks[2]).toBe('[Event "C"]\n\n1. c4 *');
    expect(splitGameBlocks('1. e4 e5\n2. Nf3 *')).toEqual(['1. e4 e5\n2. Nf3 *']);   // ohne Kopfzeilen: eine Partie
  });

  it('eine Liste, die in die Übersicht passt, bleibt unverändert — sonst Pakete zu 500', () => {
    const small = '[White "a"]\n\n1. e4 *\n';
    expect(pgnPortions(small)).toEqual([small]);
    expect(pgnPortions('')).toEqual(['']);
    const pgn = Array.from({ length: 1001 }, (_, i) => `[White "W${i}"]\n\n1. e4 *`).join('\n\n');
    const parts = pgnPortions(pgn);
    expect(IMPORT_PORTION).toBe(500);
    expect(parts.map(p => splitGameBlocks(p).length)).toEqual([500, 500, 1]);
    expect(parts[1].startsWith('[White "W500"]')).toBeTrue();
    expect(parts.join('').match(/\[White /g)!.length).toBe(1001);
  });

  it('teilt auch nach Zeichen — eine große Datei mit wenigen Partien geht in mehreren Paketen', () => {
    const long = (i: number) => `[White "W${i}"]\n\n1. e4 {${'x'.repeat(300)}} *`;
    const pgn = Array.from({ length: 10 }, (_, i) => long(i)).join('\n\n');
    const parts = pgnPortions(pgn, 500, 1000);
    expect(parts.length).toBe(4);
    expect(parts.every(p => p.length <= 1000)).toBeTrue();
    expect(parts.map(p => splitGameBlocks(p).length)).toEqual([3, 3, 3, 1]);
  });

  it('beschriftet die Pakete und sagt, was mit ihnen geschah', () => {
    expect(partLabel('Verein.pgn', 1, 3)).toBe('Verein.pgn (Teil 2 von 3)');
    expect(partLabel(null, 0, 3)).toBe('Teil 1 von 3');
    expect(partLabel('Verein.pgn', 0, 1)).toBe('Verein.pgn');
    expect(portionNote(1, 0)).toBeNull();
    expect(portionNote(3, null)).toContain('werden gerade als offene Listen abgelegt');
    expect(portionNote(3, 2)).toBe('Die Liste ist in 3 Pakete zu höchstens 500 Partien aufgeteilt — das hier ist das erste, '
      + 'die übrigen stehen danach unter „Deine offenen Listen“.');
    expect(portionNote(4, 2)).toContain('1 weiteres konnte nicht abgelegt werden');
  });
});
