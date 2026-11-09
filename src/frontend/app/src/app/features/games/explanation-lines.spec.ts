import { explanationParts } from './explanation-lines';

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
});
