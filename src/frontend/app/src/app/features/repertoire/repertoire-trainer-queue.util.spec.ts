import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import {
  LineOrderRule, TrainerLineSet, UNRANKED, byRank, continueQueue, countUserMoves, extendQueue, hasUserMove, lineKeyOf, opponentLines,
  orderLines, playableLines, storedOpponentLines,
} from './repertoire-trainer-queue.util';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LINE_ORDERS, LineOrder } from './repertoire-line-order';
import { DEFAULT_SR_LEVELS, hoursUntilReturnIfSolved } from './repertoire-sr.util';
import type { SrLevel } from './repertoire-training.service';

const PGN = [
  '[Event "R"]', '[White "A"]', '[Black "W1"]', '', '1. e4 e5 2. Nf3 *', '',
  '[Event "R"]', '[White "B"]', '[Black "W1"]', '', '1. d4 d5 2. c4 *', '',
  '[Event "R"]', '[White "C"]', '[Black "S1"]', '', '1. e4 c5 2. Nf3 d6 *', '',
  '[Event "R"]', '[White "D"]', '[Black "S1"]', '', '1. e4 *', '',        // für Schwarz nichts zu üben
].join('\n');
const LINES = parsePgnText(PGN);
const names = (ls: { headers: Record<string, string> }[]) => ls.map(l => l.headers['White']);
const set = (chapter: string | null = null): TrainerLineSet => ({
  lines: LINES, chapter, colorOf: l => (l.headers['Black'] === 'S1' ? 'b' : 'w'),
});
const KEY = (n: string) => lineKeyOf(LINES.find(l => l.headers['White'] === n)!);

describe('repertoire-trainer-queue.util', () => {
  it('lineKeyOf = Schlüssel der Linienliste', () => {
    expect(lineKeyOf(LINES[0])).toBe(lineKeyFromSans(['e4', 'e5', 'Nf3']));
  });

  it('playableLines: Kapitel-Filter, Einzellinie, nur Linien mit eigenem Zug', () => {
    expect(names(playableLines(set()))).toEqual(['A', 'B', 'C']);
    expect(names(playableLines(set('S1')))).toEqual(['C']);
    expect(names(playableLines(set(), KEY('B')))).toEqual(['B']);
    expect(hasUserMove(set(), LINES[3])).toBeFalse();
  });

  it('countUserMoves zählt je Linie die Züge ihrer Farbe', () => {
    expect(countUserMoves(set(), LINES.slice(0, 3))).toBe(2 + 2 + 2);
  });

  it('byRank: bekannte nach Platz, unbekannte dahinter in alter Reihenfolge', () => {
    const rank = new Map([[KEY('C'), 0], [KEY('A'), 1]]);
    expect(names(byRank(LINES.slice(0, 3), rank))).toEqual(['C', 'A', 'B']);
  });

  it('opponentLines: Farbe, Reihung und Einstieg', () => {
    const rank = new Map([[KEY('B'), 0], [KEY('A'), 1]]);
    expect(names(opponentLines(set(), { color: 'w', rank, start: null }))).toEqual(['B', 'A']);
    expect(names(opponentLines(set(), { color: 'w', rank, start: KEY('A') }))).toEqual(['A']);
    expect(names(opponentLines(set(), { color: null, rank: null, start: KEY('C') }))).toEqual(['C']);
    expect(names(opponentLines(set(), { color: null, rank: null, start: null }))).toEqual(['A', 'B', 'C']);
    expect(names(opponentLines(set(), { color: 'w', rank, start: 'unbekannt' }))).toEqual(['B', 'A']);
  });

  it('extendQueue: laufende Linie bleibt, danach die nach ihr in der Reihung', () => {
    const [a, b, c] = LINES;
    expect(extendQueue([b], 0, [a, b, c])).toEqual([b, c]);
    expect(extendQueue([b], 0, [a, c])).toEqual([b, a, c]);
    expect(extendQueue([], 0, [a, c])).toEqual([a, c]);
  });

  it('opponentLines: pausierte Linien fallen weg — außer der ausdrücklich gewählten Einstiegslinie', () => {
    const isPaused = (k: string) => k === KEY('A');
    expect(names(opponentLines(set(), { color: 'w', rank: null, start: null, isPaused }))).toEqual(['B']);
    const rank = new Map([[KEY('A'), 0], [KEY('B'), 1]]);
    expect(names(opponentLines(set(), { color: 'w', rank, start: KEY('A'), isPaused }))).toEqual(['A', 'B']);
  });
});

/**
 * Reihenfolge der Linien im Trainer (0.996.0). Eine „Linie" ist hier nur Name + Stufe + Häufigkeit — die Regel kennt
 * weder PGN noch Angular. Der Zufall ist festgenagelt: `REVERSE` „mischt" durch Umdrehen, so ist sichtbar, wo gemischt
 * wurde (und wo nicht).
 */
describe('orderLines', () => {
  interface L { name: string; level?: number; freq?: number }
  const REVERSE = <T>(a: readonly T[]): T[] => [...a].reverse();
  const names = (ls: L[]) => ls.map(l => l.name).join('');

  function rule(order: LineOrder, mode: 'quiz' | 'learn', extra: Partial<LineOrderRule<L>> = {}, levels: SrLevel[] = DEFAULT_SR_LEVELS): LineOrderRule<L> {
    return {
      order, mode, shuffle: REVERSE,
      // wie im Trainer: Wiederkehr bei richtiger Lösung aus Stufe + effektiven Intervallen; ohne Stufe = kein Zustand
      returnHours: l => hoursUntilReturnIfSolved(l.level === undefined ? undefined
        : { lineKey: l.name, level: l.level, reps: 0, lapses: 0, dueAt: '', lastReviewedAt: null, inPool: true, paused: false }, levels),
      frequency: l => l.freq ?? -1,
      ...extra,
    };
  }

  /** Repertoire-Reihenfolge A…F, Stufen gemischt. */
  const MIXED: L[] = [
    { name: 'A', level: 2 }, { name: 'B', level: 0 }, { name: 'C', level: 1 },
    { name: 'D', level: 0 }, { name: 'E', level: 2 }, { name: 'F', level: 1 },
  ];

  describe('leastTrained (Vorgabe): kürzeste Wiederkehr zuerst', () => {
    it('Abfragen: Stufe 0 vor 1 vor 2; innerhalb der Stufe bleibt die GEMISCHTE Reihenfolge (stabil)', () => {
      // gemischt (umgedreht): F1 E2 D0 C1 B0 A2 → nach Stufe: D B | F C | E A
      expect(names(orderLines(MIXED, rule('leastTrained', 'quiz')))).toBe('DBFCEA');
    });

    it('Lernen: Gleichstand in Repertoire-Reihenfolge — nichts wird gemischt', () => {
      const shuffle = jasmine.createSpy('shuffle').and.callFake(REVERSE);
      expect(names(orderLines(MIXED, rule('leastTrained', 'learn', { shuffle })))).toBe('BDCFAE');
      expect(shuffle).not.toHaveBeenCalled();
    });

    it('gleiche Stufe überall: Abfragen = die gemischte Reihenfolge unverändert, Lernen = Repertoire-Reihenfolge', () => {
      const same: L[] = [{ name: 'A', level: 3 }, { name: 'B', level: 3 }, { name: 'C', level: 3 }];
      expect(names(orderLines(same, rule('leastTrained', 'quiz')))).toBe('CBA');
      expect(names(orderLines(same, rule('leastTrained', 'learn')))).toBe('ABC');
    });

    it('Stufe 9 und darüber gleichauf am Ende; fehlender Zustand wie Stufe 0 vorn', () => {
      const ls: L[] = [{ name: 'A', level: 9 }, { name: 'B', level: 12 }, { name: 'C', level: 8 }, { name: 'D' }, { name: 'E', level: 0 }];
      // Stufe 8 → Intervall der Stufe 9, also gleichauf mit 9 und 12; D (kein Zustand) und E (Stufe 0) gleichauf vorn
      expect(names(orderLines(ls, rule('leastTrained', 'learn')))).toBe('DEABC');
    });

    it('eigene Intervalle: die Zeit entscheidet, nicht die Stufennummer', () => {
      // Stufe 1 = 2 Tage, Stufe 2 = 6 Stunden: eine Linie auf Stufe 1 (→ 6 h) kommt VOR einer auf Stufe 0 (→ 48 h)
      const own: SrLevel[] = [{ value: 2, unit: 'd' }, { value: 6, unit: 'h' }, { value: 1, unit: 'w' }, ...DEFAULT_SR_LEVELS.slice(3)];
      const ls: L[] = [{ name: 'A', level: 0 }, { name: 'B', level: 1 }, { name: 'C', level: 2 }];
      expect(names(orderLines(ls, rule('leastTrained', 'learn', {}, own)))).toBe('BAC');
      expect(names(orderLines(ls, rule('leastTrained', 'learn')))).toBe('ABC');   // Vorgaben: nach Stufe
    });
  });

  describe('mostTrained: die Umkehrung', () => {
    it('Abfragen: Stufe 2 vor 1 vor 0, Gleichstand gemischt', () => {
      expect(names(orderLines(MIXED, rule('mostTrained', 'quiz')))).toBe('EAFCDB');
    });

    it('Lernen: Gleichstand in Repertoire-Reihenfolge', () => {
      expect(names(orderLines(MIXED, rule('mostTrained', 'learn')))).toBe('AECFBD');
    });
  });

  describe('random', () => {
    it('gemischt — in BEIDEN Modi, ohne Blick auf Stufe oder Häufigkeit', () => {
      expect(names(orderLines(MIXED, rule('random', 'quiz')))).toBe('FEDCBA');
      expect(names(orderLines(MIXED, rule('random', 'learn')))).toBe('FEDCBA');
    });

    it('ohne eigenes Mischen: der echte Zufall, dieselben Linien', () => {
      spyOn(Math, 'random').and.returnValue(0);
      const out = orderLines(MIXED, { ...rule('random', 'quiz'), shuffle: undefined });
      expect(names(out)).not.toBe('ABCDEF');
      expect([...out].sort((a, b) => a.name.localeCompare(b.name))).toEqual(MIXED);
    });
  });

  describe('repertoire', () => {
    it('wie im Repertoire — in beiden Modi, nie gemischt', () => {
      const shuffle = jasmine.createSpy('shuffle').and.callFake(REVERSE);
      expect(names(orderLines(MIXED, rule('repertoire', 'quiz', { shuffle })))).toBe('ABCDEF');
      expect(names(orderLines(MIXED, rule('repertoire', 'learn', { shuffle })))).toBe('ABCDEF');
      expect(shuffle).not.toHaveBeenCalled();
    });
  });

  describe('frequency („Häufigste zuerst“)', () => {
    const FREQ: L[] = [
      { name: 'A', level: 0, freq: 0.1 }, { name: 'B', level: 5 }, { name: 'C', level: 9, freq: 0.4 },
      { name: 'D', level: 0, freq: 0.4 }, { name: 'E', level: 1, freq: 0 },
    ];

    it('Abfragen: häufigste zuerst, ohne Wert zuletzt, Gleichstand gemischt — die Stufe zählt nicht', () => {
      // gemischt: E D C B A → 0.4: D C | 0.1: A | 0: E | ohne Wert: B
      expect(names(orderLines(FREQ, rule('frequency', 'quiz')))).toBe('DCAEB');
    });

    it('Lernen: Gleichstand in Repertoire-Reihenfolge', () => {
      expect(names(orderLines(FREQ, rule('frequency', 'learn')))).toBe('CDAEB');
    });

    it('ohne Häufigkeiten (offline, Abruf gescheitert): die Vorgabe „Wenig trainierte zuerst“', () => {
      expect(names(orderLines(FREQ, rule('frequency', 'learn', { frequency: null })))).toBe('ADEBC');
      expect(names(orderLines(FREQ, rule('frequency', 'quiz', { frequency: null }))))
        .toBe(names(orderLines(FREQ, rule('leastTrained', 'quiz'))));
    });
  });

  it('jede Reihenfolge liefert dieselben Linien, lässt die Eingabe unberührt und kommt mit leer/einer klar', () => {
    for (const order of LINE_ORDERS) {
      for (const mode of ['quiz', 'learn'] as const) {
        const input = MIXED.map(l => ({ ...l, freq: l.level }));
        const before = names(input);
        const out = orderLines(input, rule(order, mode));
        expect(names(input)).withContext(`${order}/${mode}: Eingabe verändert`).toBe(before);
        expect(out).withContext(`${order}/${mode}: neue Liste`).not.toBe(input);
        expect([...out].sort((a, b) => a.name.localeCompare(b.name))).withContext(`${order}/${mode}`).toEqual(input);
        expect(orderLines([], rule(order, mode))).toEqual([]);
        expect(orderLines([MIXED[0]], rule(order, mode))).toEqual([MIXED[0]]);
      }
    }
  });
});

/**
 * Reihenfolge gegen den GESPEICHERTEN Gegner eines „Prep: …"-Repertoires (0.999.3): der Rang des Gegners ist ein eigener
 * Wert („Nach dem Gegner“) und entscheidet bei „Wenig/Viel trainierte zuerst“ den Gleichstand — statt Zufall bzw. PGN.
 */
describe('orderLines gegen den gespeicherten Gegner', () => {
  interface L { name: string; level?: number; freq?: number; rank?: number }
  const REVERSE = <T>(a: readonly T[]): T[] => [...a].reverse();
  const names = (ls: L[]) => ls.map(l => l.name).join('');
  const RANK = (l: L) => l.rank ?? UNRANKED;

  function rule(order: LineOrder, mode: 'quiz' | 'learn', extra: Partial<LineOrderRule<L>> = {}): LineOrderRule<L> {
    return {
      order, mode, shuffle: REVERSE,
      returnHours: l => hoursUntilReturnIfSolved(l.level === undefined ? undefined
        : { lineKey: l.name, level: l.level, reps: 0, lapses: 0, dueAt: '', lastReviewedAt: null, inPool: true, paused: false }, DEFAULT_SR_LEVELS),
      frequency: l => l.freq ?? -1,
      opponent: { rank: RANK },
      ...extra,
    };
  }

  /** Repertoire-Reihenfolge A…H. Gegner spielt am ehesten G, dann C, A, E, B, H; D und F kennt er nicht. */
  const PREP: L[] = [
    { name: 'A', level: 2, rank: 2 }, { name: 'B', rank: 4 }, { name: 'C', level: 1, rank: 1 }, { name: 'D', level: 1 },
    { name: 'E', rank: 3 }, { name: 'F', level: 3 }, { name: 'G', level: 1, rank: 0 }, { name: 'H', level: 0, rank: 5 },
  ];

  it('„Nach dem Gegner“: seine Reihenfolge, was er nicht kennt dahinter in Repertoire-Reihenfolge — in beiden Modi, nie gemischt', () => {
    const shuffle = jasmine.createSpy('shuffle').and.callFake(REVERSE);
    expect(names(orderLines(PREP, rule('opponent', 'quiz', { shuffle })))).toBe('GCAEBHDF');
    expect(names(orderLines(PREP, rule('opponent', 'learn', { shuffle })))).toBe('GCAEBHDF');
    expect(shuffle).not.toHaveBeenCalled();
  });

  it('„Wenig trainierte zuerst“ (Vorgabe): untrainierte zuerst, darin die wahrscheinlichsten zuerst; dann Stufe 1 nach Wahrscheinlichkeit …', () => {
    // untrainiert/Stufe 0: E(3) B(4) H(5) · Stufe 1: G(0) C(1) D(–) · Stufe 2: A · Stufe 3: F
    const shuffle = jasmine.createSpy('shuffle').and.callFake(REVERSE);
    expect(names(orderLines(PREP, rule('leastTrained', 'quiz', { shuffle })))).toBe('EBHGCDAF');
    expect(names(orderLines(PREP, rule('leastTrained', 'learn', { shuffle })))).toBe('EBHGCDAF');
    expect(shuffle).withContext('Gleichstand entscheidet der Gegner, nicht der Zufall').not.toHaveBeenCalled();
  });

  it('„Viel trainierte zuerst“: die Umkehrung, Gleichstand ebenfalls nach dem Gegner', () => {
    expect(names(orderLines(PREP, rule('mostTrained', 'quiz')))).toBe('FAGCDEBH');
  });

  it('frisches Prep-Repertoire (nichts trainiert): die Vorgabe ergibt genau die Reihenfolge des Gegners — wie bisher', () => {
    const fresh: L[] = PREP.map(({ name, rank }) => ({ name, rank }));
    expect(names(orderLines(fresh, rule('leastTrained', 'quiz')))).toBe(names(orderLines(fresh, rule('opponent', 'quiz'))));
    expect(names(orderLines(fresh, rule('leastTrained', 'quiz')))).toBe('GCAEBHDF');
  });

  it('die Reihung fehlt noch (lädt, Abruf gescheitert): Gleichstand in Repertoire-Reihenfolge, „Nach dem Gegner“ = Repertoire-Reihenfolge', () => {
    const noRank = { opponent: { rank: null } };
    expect(names(orderLines(PREP, rule('leastTrained', 'quiz', noRank)))).toBe('BEHCDGAF');
    expect(names(orderLines(PREP, rule('mostTrained', 'quiz', noRank)))).toBe('FACDGBEH');
    expect(names(orderLines(PREP, rule('opponent', 'quiz', noRank)))).toBe('ABCDEFGH');
  });

  it('„Zufällig“, „Reihenfolge im Repertoire“ und „Häufigste zuerst“ bleiben wie ohne Gegner', () => {
    const withFreq = PREP.map((l, i) => ({ ...l, freq: i % 2 ? 0.2 : 0.1 }));
    for (const mode of ['quiz', 'learn'] as const) {
      for (const order of ['random', 'repertoire', 'frequency'] as const) {
        const plain = rule(order, mode); delete plain.opponent;
        expect(names(orderLines(withFreq, rule(order, mode)))).withContext(`${order}/${mode}`).toBe(names(orderLines(withFreq, plain)));
      }
    }
    expect(names(orderLines(PREP, rule('random', 'learn')))).toBe('HGFEDCBA');
    expect(names(orderLines(PREP, rule('repertoire', 'quiz')))).toBe('ABCDEFGH');
  });

  it('„Häufigste zuerst“ ohne Häufigkeiten: die Vorgabe — und gegen den Gegner mit seinem Gleichstand', () => {
    expect(names(orderLines(PREP, rule('frequency', 'quiz', { frequency: null })))).toBe('EBHGCDAF');
  });

  it('OHNE Gegner-Sitzung fällt ein gewähltes „Nach dem Gegner“ auf die Vorgabe (Gleichstand wie sonst: Abfragen gemischt, Lernen wie im Repertoire)', () => {
    const plainQuiz = rule('opponent', 'quiz'); delete plainQuiz.opponent;
    const plainLearn = rule('opponent', 'learn'); delete plainLearn.opponent;
    const leastQuiz = rule('leastTrained', 'quiz'); delete leastQuiz.opponent;
    expect(names(orderLines(PREP, plainQuiz))).toBe(names(orderLines(PREP, leastQuiz)));
    expect(names(orderLines(PREP, plainLearn))).toBe('BEHCDGAF');
  });

  it('lässt die Eingabe unberührt und liefert in jeder Reihenfolge dieselben Linien', () => {
    for (const order of LINE_ORDERS) {
      const input = PREP.map(l => ({ ...l }));
      const out = orderLines(input, rule(order, 'quiz'));
      expect(names(input)).toBe('ABCDEFGH');
      expect(out).not.toBe(input);
      expect([...out].sort((a, b) => a.name.localeCompare(b.name))).toEqual(input);
    }
  });
});

describe('storedOpponentLines (Sitzung gegen den gespeicherten Gegner, 0.999.3)', () => {
  // A, B im Weiß-Kapitel W1; C im Schwarz-Kapitel S1; D hat für Schwarz nichts zu üben
  const identity = (ls: typeof LINES) => ls;
  const reversed = (ls: typeof LINES) => [...ls].reverse();
  const base = { color: null, start: null, isPaused: () => false, keep: null, order: identity };

  it('ohne Filter (Lernen, ausdrückliche Wiederholung): alle übbaren Linien, in der gewählten Reihenfolge', () => {
    expect(names(storedOpponentLines(set(), base))).toEqual(['A', 'B', 'C']);
    expect(names(storedOpponentLines(set(), { ...base, order: reversed }))).toEqual(['C', 'B', 'A']);
  });

  it('nur die Kapitel der Farbe gegen ihn; der Kapitel-Filter der Sitzung gilt', () => {
    expect(names(storedOpponentLines(set(), { ...base, color: 'w' }))).toEqual(['A', 'B']);
    expect(names(storedOpponentLines(set(), { ...base, color: 'b' }))).toEqual(['C']);
    expect(names(storedOpponentLines(set('S1'), base))).toEqual(['C']);
  });

  it('Abfragen: nur, was der Filter durchlässt (fällig oder untrainiert) — pausierte nie', () => {
    const keep = (l: (typeof LINES)[number]) => l.headers['White'] !== 'B';
    expect(names(storedOpponentLines(set(), { ...base, keep }))).toEqual(['A', 'C']);
    const isPaused = (k: string) => k === KEY('A');
    expect(names(storedOpponentLines(set(), { ...base, keep, isPaused }))).toEqual(['C']);
    expect(names(storedOpponentLines(set(), { ...base, isPaused }))).toEqual(['B', 'C']);
  });

  it('die Einstiegslinie kommt IMMER und zuerst — auch pausiert oder nicht fällig; danach ALLE übrigen der Regel, nichts wird abgeschnitten', () => {
    const keep = (l: (typeof LINES)[number]) => l.headers['White'] !== 'B';
    const isPaused = (k: string) => k === KEY('B');
    expect(names(storedOpponentLines(set(), { ...base, start: KEY('B'), keep, isPaused }))).toEqual(['B', 'A', 'C']);
    // die Reihenfolge gilt für den Rest; die Einstiegslinie steht nicht doppelt da
    expect(names(storedOpponentLines(set(), { ...base, start: KEY('B'), order: reversed }))).toEqual(['B', 'C', 'A']);
    expect(names(storedOpponentLines(set(), { ...base, start: KEY('C'), order: reversed }))).toEqual(['C', 'B', 'A']);
  });

  it('Einstiegslinie einer anderen Farbe oder unbekannt: keine Sonderrolle', () => {
    expect(names(storedOpponentLines(set(), { ...base, color: 'w', start: KEY('C') }))).toEqual(['A', 'B']);
    expect(names(storedOpponentLines(set(), { ...base, start: 'unbekannt' }))).toEqual(['A', 'B', 'C']);
  });

  it('nichts übrig: leer (der Trainer zeigt dann den Leerzustand)', () => {
    expect(storedOpponentLines(set(), { ...base, keep: () => false })).toEqual([]);
  });
});

describe('continueQueue (Warteschlange einer laufenden Sitzung fortschreiben)', () => {
  const [a, b, c] = LINES;

  it('alles bis zur laufenden Linie bleibt, danach die übrigen in der NEUEN Reihenfolge', () => {
    expect(continueQueue([b, a, c], 0, [c, b, a])).toEqual([b, c, a]);
    expect(continueQueue([b], 0, [a, b, c])).toEqual([b, a, c]);
  });

  it('schon gespielte Linien kommen nicht noch einmal — auch wenn sie in der neuen Reihenfolge hinter der laufenden stünden', () => {
    expect(continueQueue([a, b, c], 1, [b, c, a])).toEqual([a, b, c]);
    expect(continueQueue([a, b], 1, [c, b, a])).toEqual([a, b, c]);
  });

  it('nichts geht verloren, was in der neuen Reihenfolge VOR der laufenden steht (anders als extendQueue)', () => {
    expect(continueQueue([b], 0, [a, c, b])).toEqual([b, a, c]);
    expect(extendQueue([b], 0, [a, c, b])).toEqual([b]);          // die ausdrückliche Gegner-Sitzung schneidet ab der Einstiegslinie
  });

  it('die laufende Linie fehlt in der neuen Liste (z. B. nicht mehr fällig): sie läuft weiter, der Rest folgt', () => {
    expect(continueQueue([b], 0, [a, c])).toEqual([b, a, c]);
  });

  it('leere Warteschlange: einfach die neue Liste', () => {
    expect(continueQueue([], 0, [a, c])).toEqual([a, c]);
  });
});
