import {
  FIDE_DIFF_CAP, OESB_DIFF_CAP, expectedScore, fideChange, fideKFactor, formatChange, oesbChange,
  oesbKFactor,
} from './elo-calc';

describe('elo-calc', () => {
  describe('expectedScore (Tabelle FIDE 8.1.2 / TUWO Anhang 1, 5.3)', () => {
    it('liest die Stufen beidseitig ab', () => {
      expect(expectedScore(1500, 1500, FIDE_DIFF_CAP)).toBe(0.5);
      expect(expectedScore(1503, 1500, FIDE_DIFF_CAP)).toBe(0.5);   // 0–3
      expect(expectedScore(1504, 1500, FIDE_DIFF_CAP)).toBe(0.51);  // 4–10
      expect(expectedScore(1923, 2100, FIDE_DIFF_CAP)).toBe(0.27);  // 177: 171–179 → .73/.27
      expect(expectedScore(2100, 1923, FIDE_DIFF_CAP)).toBe(0.73);
      expect(expectedScore(1700, 1500, FIDE_DIFF_CAP)).toBe(0.76);  // 200: 198–206
      // Druckfehler „201–215" in der TUWO: 207 gehoert zu .77.
      expect(expectedScore(1707, 1500, OESB_DIFF_CAP)).toBe(0.77);
      expect(expectedScore(1715, 1500, OESB_DIFF_CAP)).toBe(0.77);
      expect(expectedScore(1716, 1500, OESB_DIFF_CAP)).toBe(0.78);
    });

    it('deckelt bei FIDE auf 400, beim OeSB auf 450', () => {
      expect(expectedScore(2500, 1900, FIDE_DIFF_CAP)).toBe(0.92);   // 600 → 400
      expect(expectedScore(1900, 2500, FIDE_DIFF_CAP)).toBe(0.08);
      expect(expectedScore(2500, 1900, OESB_DIFF_CAP)).toBe(0.94);   // 600 → 450
      expect(expectedScore(1900, 2500, OESB_DIFF_CAP)).toBe(0.06);
      expect(expectedScore(1920, 1500, OESB_DIFF_CAP)).toBe(0.93);   // 420: 412–432
      expect(expectedScore(1920, 1500, FIDE_DIFF_CAP)).toBe(0.92);   // 420 → 400
    });
  });

  describe('K-Faktoren', () => {
    it('FIDE: 40 unter 18 und unter 2300, 20 unter 2400, sonst 10', () => {
      expect(fideKFactor(1923, null, 2026)).toBe(20);
      expect(fideKFactor(1923, 1982, 2026)).toBe(20);
      expect(fideKFactor(1600, 2010, 2026)).toBe(40);   // wird 2026 erst 16
      expect(fideKFactor(1600, 2008, 2026)).toBe(20);   // wird 2026 18
      expect(fideKFactor(2350, 2010, 2026)).toBe(20);   // jung, aber ueber 2300
      expect(fideKFactor(2400, 2010, 2026)).toBe(10);
      expect(fideKFactor(2650)).toBe(10);
    });

    it('OeSB: (3400 − R)² / 100000, hoechstens 50, ab 2400 konstant 10', () => {
      expect(oesbKFactor(1923)).toBeCloseTo(21.815, 3);   // 1477² / 1e5
      expect(oesbKFactor(2000)).toBeCloseTo(19.6, 6);
      expect(oesbKFactor(2399)).toBeCloseTo(10.02, 2);
      expect(oesbKFactor(2400)).toBe(10);
      expect(oesbKFactor(2700)).toBe(10);
      expect(oesbKFactor(1000)).toBe(50);                 // 57,6 → gedeckelt
      expect(oesbKFactor(1164)).toBeCloseTo(49.997, 3);   // knapp unter dem Deckel
      expect(oesbKFactor(1200)).toBeCloseTo(48.4, 6);
    });
  });

  describe('Aenderung je Partie', () => {
    it('FIDE K 20, 1923 gegen 2100', () => {
      expect(fideChange(1923, 2100, 20, 'win')).toBeCloseTo(14.6, 6);
      expect(fideChange(1923, 2100, 20, 'draw')).toBeCloseTo(4.6, 6);
      expect(fideChange(1923, 2100, 20, 'loss')).toBeCloseTo(-5.4, 6);
    });

    it('OeSB mit eigenem Faktor, 2000 gegen 1800 (We .76)', () => {
      const k = oesbKFactor(2000);   // 19,6
      expect(oesbChange(2000, 1800, k, 'win')).toBeCloseTo(4.704, 6);
      expect(oesbChange(2000, 1800, k, 'draw')).toBeCloseTo(-5.096, 6);
      expect(oesbChange(2000, 1800, k, 'loss')).toBeCloseTo(-14.896, 6);
    });

    it('die Deckelung wirkt auch auf die Aenderung', () => {
      // 2500 gegen 1900: FIDE We .92, OeSB We .94 — ein Sieg bringt bei FIDE mehr.
      expect(fideChange(2500, 1900, 10, 'win')).toBeCloseTo(0.8, 6);
      expect(oesbChange(2500, 1900, 10, 'win')).toBeCloseTo(0.6, 6);
    });
  });

  it('formatChange: eine Nachkommastelle, Vorzeichen, Locale', () => {
    expect(formatChange(14.6, 'de')).toBe('+14,6');
    expect(formatChange(-5.4, 'en')).toBe('−5.4');
    expect(formatChange(0.04, 'de')).toBe('0,0');
    expect(formatChange(4.704, 'de')).toBe('+4,7');
  });
});
