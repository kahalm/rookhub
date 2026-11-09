import { explanationParts, sanLine } from './explanation-lines';

// Nach 1.e4 e5 2.Sf3 — Schwarz am Zug, Zug 2.
const ROOT = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2';

describe('explanationParts', () => {
  it('macht Zugfolgen klickbar und setzt jede an der zuletzt genannten passenden Stellung an', () => {
    const text = '2...Sc6 ist besser als 2...d6. Nach 2...Sc6 3.Lb5 a6 (+0,3) spielt Weiß 4.La4. Auf 2...d6 folgt 3.d4.';
    const parts = explanationParts(text, ROOT);
    // Der Text bleibt Zeichen für Zeichen erhalten.
    expect(parts.map(p => p.text).join('')).toBe(text);
    const moves = parts.filter(p => p.line);
    expect(moves.map(p => p.text)).toEqual(['2...Sc6', '2...d6', '2...Sc6', '3.Lb5', 'a6', '4.La4', '2...d6', '3.d4']);
    const ba4 = moves[5];
    expect(ba4.line!.map(m => m.san)).toEqual(['Nc6', 'Bb5', 'a6', 'Ba4']);
    expect(ba4.at).toBe(4);
    const a6 = moves[4];
    expect(a6.line!.slice(0, a6.at).map(m => m.san)).toEqual(['Nc6', 'Bb5', 'a6']);
    // „3.d4" gehört zur zuletzt genannten Stellung mit Weiß am Zug 3 — nach 2...d6.
    expect(moves[7].line!.map(m => m.uci)).toEqual(['d7d6', 'd2d4']);
    // Der Satzpunkt nach dem letzten Zug bleibt Text.
    expect(parts[parts.length - 1].text).toBe('.');
  });

  it('lässt Nummern ohne legalen Zug und Bewertungen als Text', () => {
    const text = 'Zug 2. Danach 5.Dh5 (+1,01) und 2...Ke7?! ist schwach.';
    const parts = explanationParts(text, ROOT);
    expect(parts.map(p => p.text).join('')).toBe(text);
    const moves = parts.filter(p => p.line);
    expect(moves.map(p => p.text)).toEqual(['2...Ke7?!']);
  });

  it('versteht englische Figurenbuchstaben und den Auslassungspunkt', () => {
    const parts = explanationParts('After 2…Nc6 3.Bb5 the bishop pins.', ROOT);
    expect(parts.filter(p => p.line).map(p => p.line![p.at! - 1].san)).toEqual(['Nc6', 'Bb5']);
  });

  // Fehlermeldung 2026-11-09 (Vereinspartie 241, 17.Dc2 statt 17.Dh4): eine Stellung, in der nach 17.Dc2 Sf6 UND nach
  // 17.Dh4 Sf6 die ganze Folge 18.d5 exd5 19.Lxf6 gxf6 legal ist — nur der Textfluss entscheidet, welche Variante gemeint ist.
  const FEHLER = 'r6k/pp1n1ppp/4p3/6B1/3P4/8/PP3QPP/R5K1 w - - 0 17';

  it('hängt eine spätere Folge an die zuletzt genannte Variante und füllt die Lücke mit dem zuletzt genannten Zug', () => {
    const text = 'Nach 17.Dc2 kann dein Gegner mit 17...Sf6 die Stellung stabilisieren, was nur zu einem klaren Vorteil von +2.2 führt. '
      + 'Gegen 17.Dh4 funktioniert das nicht, da du mit 18.d5 exd5 19.Lxf6 gxf6 antwortest und einen Springer sowie zwei Bauern gewinnst.';
    const parts = explanationParts(text, FEHLER);
    expect(parts.map(p => p.text).join('')).toBe(text);
    const moves = parts.filter(p => p.line);
    expect(moves.map(p => p.text)).toEqual(['17.Dc2', '17...Sf6', '17.Dh4', '18.d5', 'exd5', '19.Lxf6', 'gxf6']);
    expect(moves[1].line!.map(m => m.san)).toEqual(['Qc2', 'Nf6']);
    // „18.d5" gehört zu 17.Dh4 (zuletzt genannt), der fehlende Zug 17...Sf6 kommt aus der früheren Nennung.
    const d5 = moves[3];
    expect(d5.line!.map(m => m.san)).toEqual(['Qh4', 'Nf6', 'd5', 'exd5', 'Bxf6', 'gxf6']);
    expect(d5.at).toBe(3);
    expect(moves[6].at).toBe(6);
    expect(moves[6].line).toBe(d5.line);
  });

  it('füllt ungenannte Zwischenzüge aus den Kandidatenlinien auf, und zwar aus der, mit der die Folge aufgeht', () => {
    const candidates = [sanLine(FEHLER, ['Qc2', 'Nf6', 'd5']), sanLine(FEHLER, ['Qh4', 'Nf6', 'd5'])];
    // 18.Dh6 geht nur nach 17.Dh4 (von c2 aus nicht) — die erste Kandidatenlinie geht nicht auf, die zweite schon.
    const parts = explanationParts('Danach kommt 18.Dh6 mit Mattdrohung.', FEHLER, undefined, undefined, candidates);
    const moves = parts.filter(p => p.line);
    expect(moves.map(p => p.text)).toEqual(['18.Dh6']);
    expect(moves[0].line!.map(m => m.san)).toEqual(['Qh4', 'Nf6', 'Qh6']);
    expect(moves[0].at).toBe(3);
  });

  it('lässt eine Folge als Text, die auch mit Auffüllen nicht spielbar ist', () => {
    const candidates = [sanLine(FEHLER, ['Qc2', 'Nf6']), sanLine(FEHLER, ['Qh4', 'Nf6'])];
    // Ohne Kandidaten fehlt jede Nennung zu Zug 17; mit ihnen bleibt 18.Txa7 illegal (a2 versperrt die a-Linie).
    expect(explanationParts('Danach kommt 18.Dh6.', FEHLER).some(p => p.line)).toBeFalse();
    const text = 'Danach 18.Txa7 gewinnt nichts.';
    const parts = explanationParts(text, FEHLER, undefined, undefined, candidates);
    expect(parts.some(p => p.line)).toBeFalse();
    expect(parts.map(p => p.text).join('')).toBe(text);
  });
});
