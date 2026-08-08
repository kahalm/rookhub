import {
  CALC_GRADE_KEYS, CALC_GRADE_OPTIONS, CalcGrade, applyReviewPatch, emptyReview, formatScore,
  formatSeconds, gradePoints, isNoopPatch, maxPoints, mergeReviewPatch, newSecondsToken,
  normalizeGrade, sumPoints, sumSeconds, toReviewBody,
} from './calc-review.util';

describe('calc-review Stufen', () => {
  it('führt genau fünf Stufen von schlecht nach gut', () => {
    expect(CALC_GRADE_KEYS).toEqual([
      'notSolved', 'someIdeas', 'moveNoMainLine', 'moveNoSideLines', 'solved',
    ]);
    // Die Reihenfolge ist die Aussage: „Hauptfolge nicht gesehen" wiegt schwerer als
    // „Nebenfolgen nicht gesehen".
    expect(CALC_GRADE_OPTIONS.map(o => o.grade)).toEqual([0, 1, 2, 3, 4]);
    expect(CALC_GRADE_OPTIONS.map(o => o.points)).toEqual([0, 1, 2, 3, 4]);
    expect(CALC_GRADE_OPTIONS.map(o => o.labelKey)).toEqual([
      'calc.review.grade.notSolved', 'calc.review.grade.someIdeas',
      'calc.review.grade.moveNoMainLine', 'calc.review.grade.moveNoSideLines',
      'calc.review.grade.solved',
    ]);
  });

  it('leitet die Punkte aus der Stufe ab — „nicht bewertet" zählt als 0', () => {
    expect(gradePoints(0)).toBe(0);
    expect(gradePoints(2)).toBe(2);
    expect(gradePoints(4)).toBe(4);
    expect(gradePoints(null)).toBe(0);
  });
});

describe('calc-review normalizeGrade', () => {
  it('nimmt gültige Stufen an und klemmt Fremdwerte auf die Skala', () => {
    expect(normalizeGrade(3)).toBe(3);
    expect(normalizeGrade('2')).toBe(2);
    expect(normalizeGrade(9)).toBe(4);
    expect(normalizeGrade(-1)).toBe(0);
  });

  it('behandelt leer/unlesbar als „noch nicht bewertet"', () => {
    expect(normalizeGrade('')).toBeNull();
    expect(normalizeGrade(null)).toBeNull();
    expect(normalizeGrade(undefined)).toBeNull();
    expect(normalizeGrade('abc')).toBeNull();
    // Stufe 0 ist eine ECHTE Bewertung („nicht gelöst") und darf nicht zu null werden.
    expect(normalizeGrade(0)).toBe(0);
  });
});

describe('calc-review mergeReviewPatch', () => {
  it('lässt den jüngeren Stand bei Wahl und Stufe gewinnen', () => {
    const merged = mergeReviewPatch(
      { chosenSan: 'Nd5', chosenUci: 'c3d5', grade: 1 },
      { chosenSan: 'Rxf6', chosenUci: 'f1f6', grade: 4 },
    );
    expect(merged.chosenSan).toBe('Rxf6');
    expect(merged.chosenUci).toBe('f1f6');
    expect(merged.grade).toBe(4);
  });

  it('addiert die Zeit — sie ist ein Delta, kein Absolutwert', () => {
    const merged = mergeReviewPatch({ secondsDelta: 30 }, { secondsDelta: 12 });
    expect(merged.secondsDelta).toBe(42);
  });

  it('behält die gemessene Zeit, wenn nur die Wahl nachgereicht wird', () => {
    // Genau der Fall „gescheiterte Anfrage + inzwischen neuer Klick": die Zeit darf nicht
    // verloren gehen, die Wahl nicht zurückspringen.
    const merged = mergeReviewPatch({ secondsDelta: 30, chosenSan: 'Nd5', chosenUci: 'c3d5' },
      { chosenSan: null, chosenUci: null });
    expect(merged.secondsDelta).toBe(30);
    expect(merged.chosenSan).toBeNull();
    expect(merged.chosenUci).toBeNull();
  });

  it('unterscheidet „Bewertung zurücknehmen" von „Bewertung nicht anfassen"', () => {
    expect(mergeReviewPatch({ grade: 2 }, { grade: null }).grade).toBeNull();
    expect(mergeReviewPatch({ grade: 2 }, { secondsDelta: 1 }).grade).toBe(2);
  });

  it('behält die Zeit-Marke des ÄLTEREN Patches (er kann schon beim Server sein)', () => {
    // Der ältere Patch wurde bereits geschickt und ist gescheitert — vielleicht aber nur die
    // ANTWORT. Mit einer frischen Marke würde der Server seine Zeit ein zweites Mal addieren.
    const merged = mergeReviewPatch(
      { secondsDelta: 30, secondsToken: 'alt' },
      { secondsDelta: 12, secondsToken: 'neu' },
    );
    expect(merged.secondsDelta).toBe(42);
    expect(merged.secondsToken).toBe('alt');
  });

  it('übernimmt die Marke des jüngeren Patches, wenn der ältere gar keine Zeit trägt', () => {
    const merged = mergeReviewPatch({ grade: 2 }, { secondsDelta: 12, secondsToken: 'neu' });
    expect(merged.secondsToken).toBe('neu');
  });

  it('wirft die Marke mit der Zeit weg (ohne Delta hat sie keine Bedeutung)', () => {
    const merged = mergeReviewPatch({ secondsDelta: 0, secondsToken: 'alt' }, { grade: 1 });
    expect(merged.secondsDelta).toBeUndefined();
    expect(merged.secondsToken).toBeUndefined();
  });
});

describe('calc-review newSecondsToken', () => {
  it('vergibt je Aufruf eine andere Marke', () => {
    const tokens = new Set(Array.from({ length: 50 }, () => newSecondsToken()));
    expect(tokens.size).toBe(50);
    expect([...tokens].every(t => t.length > 0 && t.length <= 64)).toBeTrue();
  });
});

describe('calc-review isNoopPatch', () => {
  it('erkennt Patches, die nichts ändern', () => {
    expect(isNoopPatch({})).toBeTrue();
    expect(isNoopPatch({ secondsDelta: 0 })).toBeTrue();
    expect(isNoopPatch({ secondsDelta: 3 })).toBeFalse();
    expect(isNoopPatch({ grade: null })).toBeFalse();      // „Bewertung zurücknehmen" ist eine Änderung
    expect(isNoopPatch({ grade: 0 })).toBeFalse();         // Stufe 0 ist eine Bewertung
    expect(isNoopPatch({ chosenSan: null, chosenUci: null })).toBeFalse();
  });
});

describe('calc-review applyReviewPatch', () => {
  it('zeigt den neuen Stand sofort an, ohne auf den Server zu warten', () => {
    const next = applyReviewPatch({ chosenSan: null, chosenUci: null, secondsSpent: 60, grade: null },
      { chosenSan: 'Nd5', chosenUci: 'c3d5', secondsDelta: 30, grade: 3 });
    expect(next).toEqual({ chosenSan: 'Nd5', chosenUci: 'c3d5', secondsSpent: 90, grade: 3 });
  });

  it('lässt Unberührtes unberührt', () => {
    const before = { chosenSan: 'Nd5', chosenUci: 'c3d5', secondsSpent: 10, grade: 2 as CalcGrade };
    expect(applyReviewPatch(before, {})).toEqual(before);
    expect(emptyReview()).toEqual({ chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null });
  });
});

describe('calc-review toReviewBody', () => {
  it('schickt die STUFE, nicht die Punktzahl', () => {
    expect(toReviewBody({ grade: 3 })).toEqual({ grade: 3 });
  });

  it('braucht Schalter fürs Löschen — `null` allein wäre im JSON nicht von „fehlt" zu unterscheiden', () => {
    expect(toReviewBody({ grade: null })).toEqual({ clearGrade: true });
    expect(toReviewBody({ chosenSan: null, chosenUci: null })).toEqual({ clearChoice: true });
  });

  it('überträgt Wahl und Zeit-Delta', () => {
    expect(toReviewBody({ chosenSan: 'Nd5', chosenUci: 'c3d5', secondsDelta: 42 }))
      .toEqual({ chosenSan: 'Nd5', chosenUci: 'c3d5', addSeconds: 42 });
  });

  it('schickt die Zeit-Marke mit dem Delta (sonst wäre das Addieren nicht wiederholungsfest)', () => {
    expect(toReviewBody({ secondsDelta: 42, secondsToken: 't-1' }))
      .toEqual({ addSeconds: 42, secondsToken: 't-1' });
  });

  it('lässt Unberührtes weg (leerer Rumpf = nichts ändern)', () => {
    expect(toReviewBody({})).toEqual({});
    expect(toReviewBody({ secondsDelta: 0 })).toEqual({});
    // Ohne Delta ist die Marke bedeutungslos und hat im Rumpf nichts verloren.
    expect(toReviewBody({ secondsDelta: 0, secondsToken: 't-1' })).toEqual({});
  });
});

describe('calc-review formatSeconds + Summen', () => {
  it('formatiert m:ss bzw. h:mm:ss', () => {
    expect(formatSeconds(0)).toBe('0:00');
    expect(formatSeconds(65)).toBe('1:05');
    expect(formatSeconds(3723)).toBe('1:02:03');
    expect(formatSeconds(-5)).toBe('0:00');
  });

  it('summiert Punkte und Zeit; nicht bewertete Stellungen zählen als 0', () => {
    const rows: { grade: CalcGrade | null; secondsSpent: number }[] = [
      { grade: 4, secondsSpent: 120 },
      { grade: null, secondsSpent: 45 },
      { grade: 2, secondsSpent: 0 },
    ];
    expect(sumPoints(rows)).toBe(6);
    expect(sumSeconds(rows)).toBe(165);
  });

  it('nennt zu jeder Summe ihr Maximum — 4 Punkte je Stellung', () => {
    expect(maxPoints(6)).toBe(24);
    expect(maxPoints(0)).toBe(0);
    expect(formatScore(14, maxPoints(6))).toBe('14 / 24');
    expect(formatScore(0, 0)).toBe('0 / 0');
  });
});
