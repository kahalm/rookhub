import { coarseRuleFor } from '../../testing/coarse-pointer-rules';

// Codereview UX-007: „Tipp", Auge und „Tipps melden" unter dem Brett waren am Handy nur ~26 px hoch — ein Fehltreffer
// auf „Tipp" kostet eine Tipp-Stufe (zählt in die Wertung). Die Regel steht global (styles.scss) für alle drei Löser.
describe('Touch-Ziele der Hinweiszeile unter dem Brett (UX-007)', () => {
  for (const cls of ['.board-hint-tip', '.board-hint-eye', '.board-hint-flag']) {
    it(`${cls} ist bei grobem Zeiger mindestens 44 × 44 px groß`, () => {
      const rule = coarseRuleFor(cls);
      expect(rule).withContext(cls).toBeDefined();
      expect(rule!.style.minHeight).toBe('44px');
      expect(rule!.style.minWidth).toBe('44px');
    });
  }
});
