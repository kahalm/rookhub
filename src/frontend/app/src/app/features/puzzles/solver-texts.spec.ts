import { epForcedHintTexts, offPathHintTexts, offPathWarningText } from './solver-texts';

describe('solver-texts', () => {
  const t = (k: string, p?: object) => p ? `${k}:${JSON.stringify(p)}` : k;

  it('liefert die drei Anarchy-Hinweise', () => {
    expect(epForcedHintTexts(t)).toEqual(['puzzles.anarchyHint1', 'puzzles.anarchyHint2', 'puzzles.anarchyHint3']);
  });

  it('Off-Path-Tipps: Stufe 3 mit Zugnummer bzw. ohne', () => {
    expect(offPathHintTexts(t, 2)).toEqual(['puzzles.offPath.h1', 'puzzles.offPath.h2', 'puzzles.offPath.h3:{"n":2}']);
    expect(offPathHintTexts(t, null)[2]).toBe('puzzles.offPath.h3any');
  });

  it('Warntext je nach Startstellung, Eval in Klammern', () => {
    expect(offPathWarningText(t, false, '+0.8')).toBe('puzzles.offPathWarning (+0.8)');
    expect(offPathWarningText(t, true, null)).toBe('puzzles.offPathWarningEqual');
  });
});
