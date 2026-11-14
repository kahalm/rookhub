import { GameTextPart, mergeGameSection } from './summary-game-text.util';
import { SummaryBlock, parseSummary, positionsOf } from './summary-markdown.util';

type GameText = SummaryBlock & { kind: 'gameText' };
const PGN = '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 1-0';
const TEXT = 'Die Partie begann spanisch: 1.e4 e5 2.Sf3 Sc6 3.Lb5 Schwarz fragt den Läufer (statt 3...Sf6 4.0-0 Sxe4 mit der '
  + 'Berliner Mauer): 3...a6! 4.La4 Sf6 5.0-0 Le7?? Schluss (0,0).';

function merge(md: string, pgn = PGN) {
  return mergeGameSection(parseSummary(md), positionsOf(pgn));
}
function texts(blocks: SummaryBlock[]): GameText[] {
  return blocks.filter((b): b is GameText => b.kind === 'gameText');
}
function moves(blocks: SummaryBlock[]): GameTextPart[] {
  return texts(blocks).flatMap(b => b.segments.flatMap(s => s.parts.filter(p => p.move)));
}

describe('summary-game-text.util (verschmolzener Abschnitt „Die Partie“, Wunsch 2026-11-14)', () => {
  const md = `# A – B\n\nKopf mit 1.e4.\n\n## Die Partie\n\n${TEXT}\n\nZweiter Absatz ohne Züge.\n\n## Schlüsselstellungen\n\nText 3.Lb5.`;

  it('Abschnitt wird zu gameText-Absätzen; alles davor und danach bleibt, wie es war', () => {
    const { blocks, merged } = merge(md);
    expect(merged).toBeTrue();
    expect(blocks.map(b => b.kind)).toEqual(['heading', 'paragraph', 'heading', 'gameText', 'gameText', 'heading', 'paragraph']);
  });

  it('Partiezüge tragen ihren Halbzug, Variantenzüge keinen; die Variante läuft ab der Partiestellung', () => {
    const m = moves(merge(md).blocks);
    expect(m.map(p => p.text + (p.glyph ?? ''))).toEqual(
      ['1.e4', 'e5', '2.Sf3', 'Sc6', '3.Lb5', '3...Sf6', '4.0-0', 'Sxe4', '3...a6!', '4.La4', 'Sf6', '5.0-0', 'Le7??']);
    expect(m.map(p => p.move!.ply)).toEqual([1, 2, 3, 4, 5, undefined, undefined, undefined, 6, 7, 8, 9, 10]);
    const sxe4 = m[7].move!;
    expect(sxe4.at).toBe(8);
    expect(sxe4.line.map(x => x.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'Nf6', 'O-O', 'Nxe4']);
  });

  it('Klammer mit Zügen ist ein Varianten-Lauf, Klammer ohne Zug bleibt Text; Zugzeichen und Ergebnis-Marke', () => {
    const [first] = texts(merge(md).blocks);
    expect(first.segments.map(s => s.variation)).toEqual([false, true, false]);
    expect(first.segments[1].parts.map(p => p.text).join('')).toBe('(statt 3...Sf6 4.0-0 Sxe4 mit der Berliner Mauer)');
    const last = first.segments[2].parts;
    expect(last.map(p => p.text).join('')).toBe(': 3...a6 4.La4 Sf6 5.0-0 Le7 Schluss (0,0).');
    expect(last.find(p => p.text === '3...a6')).toEqual(jasmine.objectContaining({ glyph: '!', mark: 'good' }));
    const le7 = last.findIndex(p => p.text === 'Le7');
    expect(last[le7]).toEqual(jasmine.objectContaining({ glyph: '??', mark: 'bad' }));
    expect(last[le7 + 1]).toEqual({ text: '', result: true });
  });

  it('Variante setzt in derselben Klammer fort und zweigt in ihr ab; nach der Klammer gilt wieder die Partie', () => {
    const m = moves(merge(md.replace('4.0-0 Sxe4 mit', '4.0-0 Sxe4 5.d4 Sd6, oder 5.Te1 Sd6 mit')).blocks);
    const te1 = m.find(p => p.text === '5.Te1')!.move!;
    expect(te1.line.slice(0, te1.at).map(x => x.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'Nf6', 'O-O', 'Nxe4', 'Re1']);
    expect(m.find(p => p.text === '3...a6')!.move!.ply).toBe(6);
  });

  it('Tippfehler: ein Zug, der nirgends legal ist, bleibt Text; der Rest bleibt klickbar', () => {
    const { blocks, merged } = merge(md.replace('4.La4 Sf6', '4.Lh6 Sf6'));
    expect(merged).toBeTrue();
    const m = moves(blocks);
    expect(m.some(p => p.text.includes('Lh6'))).toBeFalse();
    expect(m.map(p => p.move!.ply).filter(p => p !== undefined)).toEqual([1, 2, 3, 4, 5, 6, 9, 10]);
  });

  it('Variante außerhalb der Klammer wird trotzdem als Variante erkannt (PGN entscheidet, nicht die Schreibweise)', () => {
    const m = moves(merge(md.replace('3...a6! 4.La4', '3...a6! 4.Lxc6 dxc6 und 4.La4')).blocks);
    expect(m.find(p => p.text === '4.Lxc6')!.move!.ply).toBeUndefined();
    expect(m.find(p => p.text === 'dxc6')!.move!.ply).toBeUndefined();
    expect(m.find(p => p.text === '4.La4')!.move!.ply).toBe(7);
  });

  it('fette Züge bleiben fett; „!?“ ohne Farbe', () => {
    const m = moves(merge(md.replace('3...a6!', '**3…a6!?**')).blocks);
    expect(m.find(p => p.text === '3…a6')).toEqual(jasmine.objectContaining({ bold: true, glyph: '!?' }));
    expect(m.find(p => p.text === '3…a6')!.mark).toBeUndefined();
  });

  it('alte Zusammenfassung (kein Abschnitt „Die Partie“) bleibt unverändert', () => {
    const blocks = parseSummary('# A\n\n## Zusammenfassung\n\nNach 1.e4 e5 2.Sf3 stand es gleich.\n\n## Schlüsselstellungen\n\nx');
    const r = mergeGameSection(blocks, positionsOf(PGN));
    expect(r.merged).toBeFalse();
    expect(r.blocks).toBe(blocks);
  });

  it('Abschnitt „Die Partie“ mit weniger als der Hälfte der Züge gilt nicht als verschmolzen', () => {
    const r = merge('## Die Partie\n\nEs begann mit 1.e4 e5 und endete bald.\n\n## Fazit\n\nx');
    expect(r.merged).toBeFalse();
    expect(r.blocks.some(b => b.kind === 'gameText')).toBeFalse();
  });

  it('ohne Züge im PGN: nichts zu verschmelzen', () => {
    expect(merge(md, '').merged).toBeFalse();
  });

  it('Vereinspartie 244 (Vorlage des Mockups): alle 120 Halbzüge der Reihe nach, 58 Variantenzüge, fünf Absätze', () => {
    const { blocks, merged } = merge(G244_TEXT, G244_PGN);
    expect(merged).toBeTrue();
    expect(texts(blocks).length).toBe(5);
    const m = moves(blocks);
    const plies = m.map(p => p.move!.ply).filter((p): p is number => p !== undefined);
    expect(plies).toEqual(Array.from({ length: 120 }, (_, i) => i + 1));
    expect(m.filter(p => p.move!.ply === undefined).length).toBe(58);
    expect(m.filter(p => p.mark === 'good').map(p => p.text + p.glyph)).toEqual(['Dxc5!', 'Sc3!!', '22.Sd4!', '23...Dg4!', '25.e4!', 'Dg5!', '38...Td8!', 'Lc4!']);
    expect(m.filter(p => p.mark === 'bad').map(p => p.text + p.glyph)).toEqual(['23.Se2??', '24...Lc6??', '38.Sd3??', '40...f6??']);
    // jede Klammer mit Zügen ist ein Varianten-Lauf, „(−3,9)“ und „(0,0)“ nicht
    expect(texts(blocks).flatMap(b => b.segments).filter(s => s.variation).length).toBe(13);
  });
});

const G244_PGN = "1. Nf3 d5 2. d4 Nf6 3. Bf4 e6 4. c3 Bd6 5. e3 c5 6. Nbd2 Qc7 7. Bxd6 Qxd6 8. dxc5 Qxc5 9. Nb3 Qc7 10. Rc1 O-O 11. c4 Qe7 12. cxd5 Nxd5 13. a3 Rd8 14. Qc2 Nc6 15. Bb5 Bd7 16. Qc5 Qf6 17. Bxc6 Bxc6 18. Qd4 Qg6 19. O-O Nc3 20. Qxc3 Bxf3 21. g3 h5 22. Nd4 Bd5 23. Ne2 Qg4 24. Nf4 Bc6 25. e4 Bb5 26. f3 Qg5 27. Rfd1 h4 28. Nh3 Rxd1+ 29. Rxd1 Qe7 30. g4 b6 31. Kg2 Ba6 32. Qd4 Rc8 33. Rd2 Qc7 34. Nf2 e5 35. Qa4 Qc4 36. Qxc4 Bxc4 37. g5 Be6 38. Nd3 Rd8 39. Kf2 Bc4 40. Ke3 f6 41. g6 Bb5 42. Ne1 Rxd2 43. Kxd2 Be8 44. Ng2 h3 45. Nh4 Kf8 46. Kc3 Ke7 47. Kc4 Kd6 48. b4 a6 49. Nf5+ Kd7 50. Nh4 Kc6 51. Nf5 a5 52. b5+ Kc7 53. Nh4 Kd6 54. a4 Bd7 55. Nf5+ Bxf5 56. exf5 Ke7 57. Kc3 Kd7 58. Kd3 Kc7 59. Kc3 Kd6 60. Kc4 Kc7 1/2-1/2";
const G244_TEXT = "## Die Partie\n\nDie Partie begann als Londoner System: 1.Sf3 d5 2.d4 Sf6 3.Lf4 Schwarz baute sich mit e6, Ld6 und c5 auf und bot gleich den Tausch der schwarzfeldrigen Läufer an: 3...e6 4.c3 Ld6 5.e3 c5 6.Sbd2 Dc7 Eine kleine Ungenauigkeit ohne Folgen (die Engine zieht 6...cxd4 vor). 7.Lxd6 Dxd6 8.dxc5 Dxc5! holt den Bauern sofort zurück (nach 8...Dc7 9.b4 könnte Weiß ihn stützen). 9.Sb3 Dc7 10.Tc1 0-0 11.c4 De7 12.cxd5 Sxd5 Nach dem Tausch der c- und d-Bauern stand es praktisch gleich. 13.a3 Td8 14.Dc2 Sc6 15.Lb5 (genauer 15.Ld3) 15...Ld7 16.Dc5 Df6 17.Lxc6 Lxc6\n\nDann stellte 18.Dd4 die Dame dem Turm d8 gegenüber, dazwischen steht nur der Springer d5. (Besser 18.Sbd4 La4 19.Da5 Ld7 20.0-0 b6 21.Da6 mit etwa gleicher Stellung, −0,2) Schwarz wich dem Tausch aus und griff zugleich g2 an: 18...Dg6 19.0-0 Sc3!! Der Abzug öffnet die d-Linie und die Diagonale c6–f3 zugleich. Schlägt Weiß mit Turm oder Bauer, kostet das die Dame (20.Txc3 Txd4), zieht die Dame weg, die Qualität (20.Dh4 Se2+ 21.Kh1 Sxc1). Also 20.Dxc3 Lxf3 21.g3 – zurückschlagen ging nicht, der Bauer g2 ist durch die Dame gefesselt. Schwarz hat Läufer gegen Springer und Löcher auf f3 und h3 vor dem weißen König. 21...h5 (schärfer 21...Dh5 22.Sd4 Lg4 23.e4 Lh3 mit Mattdrohung auf g2) 22.Sd4! Ld5 23.Se2?? Der Springer verlässt sein gedecktes Feld in der d-Linie. (Besser 23.Dc2 Df6 24.e4 Dxd4 25.exd5 Txd5 26.Tfd1 De5 und Schwarz steht nur wenig besser, −0,5) 23...Dg4! 24.Sf4 Schwarz steht auf Gewinn (−3,9), die Dame droht über h3 das Matt auf g2. Jetzt musste der Läufer ziehen: 24...Lc6?? (Richtig war 24...Le4 25.f3 Lxf3 26.De1 Lc6 – der Läufer nimmt dem e-Bauern das Feld, −3,7) Denn so kam der eine Befreiungszug: 25.e4! Lb5 (nicht 25...Lxe4 26.f3 mit Gabel auf Dame und Läufer) 26.f3 Dg5! 27.Tfd1 Der Vorteil war weg (0,0).\n\nEs folgte ein ruhiger Übergang ins Endspiel: 27...h4 28.Sh3 Txd1+ 29.Txd1 De7 30.g4 b6 31.Kg2 La6 32.Dd4 Tc8 Schwarz besetzte die c-Linie und tauschte die Damen: 33.Td2 Dc7 34.Sf2 e5 35.Da4 Dc4 36.Dxc4 Lxc4 37.g5 Le6 Bis hierher war alles ausgeglichen. Dann wiederholte sich das Muster: 38.Sd3?? Der Springer stellt sich vor den eigenen, ungedeckten Turm. (Richtig war 38.Sg4 Lxg4 39.fxg4 Tc4 40.Te2 mit Gleichstand) 38...Td8! 39.Kf2 Lc4! 40.Ke3 Der Springer ist gefesselt und zweimal angegriffen, Schwarz steht erneut auf Gewinn (−6,3). 40...f6?? Ein ruhiger Zug, wo ein konkreter nötig war: (40...Txd3+ 41.Txd3 Lxd3 42.Kxd3 Kh7 43.Kc4 Kg6 44.Kd5 Kxg5 45.Kxe5 f6+ wickelt in ein gewonnenes Bauernendspiel ab, −6,6) 41.g6 (auch 41.gxf6 gxf6 42.Se1 Txd2 43.Kxd2 hält) Der Bauer nimmt dem schwarzen König f7 und h7, und Weiß löste die Fesselung auf: 41...Lb5 42.Se1 Txd2 43.Kxd2 Le8\n\nDas Endspiel Läufer gegen Springer war nicht mehr zu gewinnen: 44.Sg2 h3 45.Sh4 Kf8 46.Kc3 Ke7 47.Kc4 Kd6 48.b4 a6 49.Sf5+ Kd7 50.Sh4 Kc6 51.Sf5 a5 52.b5+ Kc7 Schwarz suchte mit König und a-Bauer noch einen Weg am Damenflügel, Weiß hielt mit dem Springer zwischen h4 und f5 alles zusammen: 53.Sh4 Kd6 54.a4 Ld7 55.Sf5+ Lxf5 56.exf5 Das Bauernendspiel bewertet die Engine durchgehend mit 0,0: 56...Ke7 57.Kc3 Kd7 58.Kd3 Kc7 59.Kc3 Kd6 60.Kc4 Kc7\n\nDer rote Faden: Zweimal erspielte sich Schwarz mit genauen Zügen eine Gewinnstellung, zweimal folgte darauf ein ruhiger Zug, wo ein konkreter nötig war.";
