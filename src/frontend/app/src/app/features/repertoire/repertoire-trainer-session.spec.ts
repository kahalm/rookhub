import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { buildRepertoireGraph } from './repertoire-tree.util';
import { lineKeyFromSans } from './repertoire-line-key.util';
import {
  ADVANCE_MS, LEARN_GAP_MS, LEARN_SHOW_MS, OPP_MOVE_DELAY_MS, TrainerClock, TrainerMode, TrainerPhase, TrainerSession,
} from './repertoire-trainer-session';

/** Von Hand weitergestellte Uhr — die Specs warten nicht, sie stellen die Zeit vor. */
class ManualClock implements TrainerClock {
  private now = 0;
  private seq = 0;
  private timers: { id: number; at: number; fn: () => void }[] = [];
  setTimeout(fn: () => void, ms: number): unknown {
    const id = ++this.seq;
    this.timers.push({ id, at: this.now + ms, fn });
    return id;
  }
  clearTimeout(handle: unknown): void { this.timers = this.timers.filter(t => t.id !== handle); }
  get pending(): number { return this.timers.length; }
  advance(ms: number): void {
    const until = this.now + ms;
    for (;;) {
      const next = [...this.timers].sort((a, b) => a.at - b.at || a.id - b.id)[0];
      if (!next || next.at > until) break;
      this.timers = this.timers.filter(t => t !== next);
      this.now = next.at;
      next.fn();
    }
    this.now = until;
  }
}

const PGN = [
  '[Event "R"]', '[White "1.e4 e5"]', '[Black "A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
  '[Event "R"]', '[White "1.d4 d5"]', '[Black "B"]', '', '1. d4 d5 2. c4 e6 *', '',
].join('\n');
const ALT = '[Event "R"]\n[White "alt"]\n[Black "A"]\n\n1. e4 {[%alt d4]} e5 2. Nf3 Nc6 *\n';
const SHOW_COMMENT = '[Event "R"]\n[White "c"]\n[Black "A"]\n\n1. e4 {Zentrum.} e5 2. Nf3 Nc6 *\n';
const OPP_COMMENT = '[Event "R"]\n[White "c"]\n[Black "A"]\n\n1. e4 e5 {Offen.} 2. Nf3 Nc6 *\n';
const KEY_A = lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6']);

type Step =
  | ['move', string, string] | ['tick', number] | ['tap'] | ['slip'] | ['solve'] | ['afterWrong']
  | ['continue'] | ['comment'];

interface Row {
  name: string;
  pgn?: string;
  color?: 'w' | 'b';
  mode?: TrainerMode;
  /** Wie viele Linien in die Warteschlange (Vorgabe: alle). */
  lines?: number;
  /** Phase nach `start`, dann nach jedem Schritt. */
  steps: [Step, TrainerPhase][];
  start: TrainerPhase;
}

function setup(row: Pick<Row, 'pgn' | 'color' | 'mode' | 'lines'>) {
  const pgn = row.pgn ?? PGN;
  const clock = new ManualClock();
  const calls = { learned: [] as string[], reviewed: [] as { lineKey: string; correct: boolean }[], wrong: 0, retry: 0, changed: 0 };
  const s = new TrainerSession({
    changed: () => calls.changed++,
    colorOf: () => row.color ?? 'w',
    lineLearned: k => calls.learned.push(k),
    lineReviewed: r => calls.reviewed.push({ lineKey: r.lineKey, correct: r.correct }),
    wrongMove: () => calls.wrong++,
    retryAfterWrong: () => calls.retry++,
  }, clock);
  s.mode = row.mode ?? 'quiz';
  s.graph = buildRepertoireGraph(pgn);
  const lines = parsePgnText(pgn);
  return { s, clock, calls, lines: row.lines !== undefined ? lines.slice(0, row.lines) : lines };
}

function run(s: TrainerSession, clock: ManualClock, step: Step): void {
  switch (step[0]) {
    case 'move': s.onMove({ orig: step[1] as any, dest: step[2] as any }); break;
    case 'tick': clock.advance(step[1]); break;
    case 'tap': s.tap(); break;
    case 'slip': s.mouseslip(); break;
    case 'solve': s.showSolution(); break;
    case 'afterWrong': s.continueAfterWrong(); break;
    case 'continue': s.continueLine(); break;
    case 'comment': s.continueFromComment(); break;
  }
}

/** Alle Phasenübergänge der Maschine als Tabelle (Zerlegung Z21): Schritt → erwartete Phase. */
const ROWS: Row[] = [
  { name: 'leere Warteschlange → EMPTY', lines: 0, start: 'EMPTY', steps: [] },
  {
    name: 'Abfragen Weiß: richtig → FEEDBACK → Timer → Gegnerzug-Fenster → PLAYING → … → LINE_DONE → DONE',
    lines: 1, start: 'PLAYING',
    steps: [
      [['move', 'e2', 'e4'], 'FEEDBACK'], [['tick', ADVANCE_MS.correct], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'PLAYING'],
      [['move', 'g1', 'f3'], 'FEEDBACK'], [['tick', ADVANCE_MS.correct], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'LINE_DONE'],
      [['continue'], 'DONE'],
    ],
  },
  {
    name: 'Abfragen: Tippen überspringt das Feedback; Tippen in PLAYING tut nichts',
    lines: 1, start: 'PLAYING',
    steps: [[['tap'], 'PLAYING'], [['move', 'e2', 'e4'], 'FEEDBACK'], [['tap'], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'PLAYING']],
  },
  {
    name: 'Abfragen: falsch → Mausrutscher → PLAYING',
    lines: 1, start: 'PLAYING',
    steps: [[['move', 'a2', 'a3'], 'FEEDBACK'], [['tap'], 'FEEDBACK'], [['slip'], 'PLAYING']],
  },
  {
    name: 'Abfragen: falsch → Lösung → Weiter → Gegnerzug → PLAYING',
    lines: 1, start: 'PLAYING',
    steps: [[['move', 'a2', 'a3'], 'FEEDBACK'], [['solve'], 'FEEDBACK'], [['afterWrong'], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'PLAYING']],
  },
  {
    name: 'Abfragen: falsch → sofortiger Neuversuch richtig → FEEDBACK',
    lines: 1, start: 'PLAYING',
    steps: [[['move', 'a2', 'a3'], 'FEEDBACK'], [['move', 'e2', 'e4'], 'FEEDBACK'], [['tick', ADVANCE_MS.correct], 'PLAYING']],
  },
  {
    name: 'Abfragen: geduldet → Timer nimmt zurück → PLAYING',
    pgn: ALT, start: 'PLAYING',
    steps: [[['move', 'd2', 'd4'], 'FEEDBACK'], [['tick', ADVANCE_MS.tolerated - 1], 'FEEDBACK'], [['tick', 1], 'PLAYING']],
  },
  {
    name: 'Abfragen Schwarz: Start im Gegnerzug-Fenster',
    color: 'b', lines: 1, start: 'PLAYING',
    steps: [[['tick', OPP_MOVE_DELAY_MS], 'PLAYING'], [['move', 'e7', 'e5'], 'FEEDBACK']],
  },
  {
    name: 'Abfragen: zwei Linien — LINE_DONE → Weiter → nächste Linie PLAYING',
    start: 'PLAYING',
    steps: [
      [['move', 'e2', 'e4'], 'FEEDBACK'], [['tap'], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'PLAYING'],
      [['move', 'g1', 'f3'], 'FEEDBACK'], [['tap'], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'LINE_DONE'],
      [['tap'], 'PLAYING'], [['move', 'd2', 'd4'], 'FEEDBACK'],
    ],
  },
  {
    name: 'Lernen: LEARN_SHOW → Timer → PLAYING → Gegnerzug → Pause → LEARN_SHOW',
    mode: 'learn', lines: 1, start: 'LEARN_SHOW',
    steps: [
      [['tick', LEARN_SHOW_MS], 'PLAYING'], [['move', 'e2', 'e4'], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'PLAYING'],
      [['tick', LEARN_GAP_MS], 'LEARN_SHOW'],
    ],
  },
  {
    name: 'Lernen: falscher Zug zeigt erneut vor (LEARN_SHOW)',
    mode: 'learn', lines: 1, start: 'LEARN_SHOW',
    steps: [[['tick', LEARN_SHOW_MS], 'PLAYING'], [['move', 'd2', 'd4'], 'LEARN_SHOW'], [['tick', LEARN_SHOW_MS], 'PLAYING']],
  },
  {
    name: 'Lernen: Vorzeigen mit Kommentar hält bis Tippen',
    mode: 'learn', pgn: SHOW_COMMENT, start: 'LEARN_SHOW',
    steps: [[['tick', 60_000], 'LEARN_SHOW'], [['tap'], 'PLAYING']],
  },
  {
    name: 'Lernen: Kommentar am Gegnerzug → COMMENT → Weiter → LEARN_SHOW',
    mode: 'learn', pgn: OPP_COMMENT, start: 'LEARN_SHOW',
    steps: [
      [['tick', LEARN_SHOW_MS], 'PLAYING'], [['move', 'e2', 'e4'], 'PLAYING'], [['tick', OPP_MOVE_DELAY_MS], 'COMMENT'],
      [['tick', 60_000], 'COMMENT'], [['comment'], 'LEARN_SHOW'],
    ],
  },
];

describe('TrainerSession — Phasenübergänge (Tabelle)', () => {
  for (const row of ROWS) {
    it(row.name, () => {
      const { s, clock, lines } = setup(row);
      s.start(lines);
      expect(s.phase).withContext('nach start').toBe(row.start);
      row.steps.forEach(([step, phase], i) => {
        run(s, clock, step);
        expect(s.phase).withContext(`Schritt ${i + 1}: ${step.join(' ')}`).toBe(phase);
      });
    });
  }
});

describe('TrainerSession — Zähler, Urteile und Meldungen', () => {
  function playLineA(s: TrainerSession, clock: ManualClock): void {
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any }); clock.advance(ADVANCE_MS.correct + OPP_MOVE_DELAY_MS);
    s.onMove({ orig: 'g1' as any, dest: 'f3' as any }); clock.advance(ADVANCE_MS.correct + OPP_MOVE_DELAY_MS);
  }

  it('fehlerfreie Linie: Urteil correct=true, Zähler und Serie, Fortschritt', () => {
    const { s, clock, calls, lines } = setup({ lines: 1 });
    s.start(lines);
    expect(s.sessionUserMoves).toBe(2);
    playLineA(s, clock);
    expect(calls.reviewed).toEqual([{ lineKey: KEY_A, correct: true }]);
    expect(s.correct).toBe(2);
    expect(s.currentStreak).toBe(2);
    expect(s.progressPct).toBe(100);
  });

  it('nicht verziehener Fehler: Urteil correct=false, Serie bricht, Vergleich gestartet und beim Neuversuch verworfen', () => {
    const { s, clock, calls, lines } = setup({ lines: 1 });
    s.start(lines);
    s.onMove({ orig: 'a2' as any, dest: 'a3' as any });
    expect(calls.wrong).toBe(1);
    expect(s.isPlayable).toBeTrue();
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(calls.retry).toBe(1);
    expect(s.wrong).toBe(1);
    clock.advance(ADVANCE_MS.correct + OPP_MOVE_DELAY_MS);
    s.onMove({ orig: 'g1' as any, dest: 'f3' as any }); clock.advance(ADVANCE_MS.correct + OPP_MOVE_DELAY_MS);
    expect(calls.reviewed[0].correct).toBeFalse();
  });

  it('Mausrutscher: Linie bleibt fehlerfrei', () => {
    const { s, clock, calls, lines } = setup({ lines: 1 });
    s.start(lines);
    s.onMove({ orig: 'a2' as any, dest: 'a3' as any });
    s.mouseslip();
    playLineA(s, clock);
    expect(calls.reviewed[0].correct).toBeTrue();
    expect(s.wrong).toBe(0);
  });

  it('showNextRepeat nur in LINE_DONE; die nächste Linie startet ohne', () => {
    const { s, clock, lines } = setup({});
    s.start(lines);
    s.showNextRepeat('2099-01-01T00:00:00Z');
    expect(s.nextRepeatAt).toBeNull();
    playLineA(s, clock);
    s.showNextRepeat('2099-01-01T00:00:00Z');
    expect(s.nextRepeatAt).toBe('2099-01-01T00:00:00Z');
    s.continueLine();
    expect(s.nextRepeatAt).toBeNull();
  });

  it('Lernen: drei Durchläufe, erst dann lineLearned; danach nächste Linie mit Vorzeigen', () => {
    const { s, clock, calls, lines } = setup({ mode: 'learn' });
    s.start(lines);
    for (let pass = 0; pass < 3; pass++) {
      if (pass === 0) clock.advance(LEARN_SHOW_MS);
      s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
      clock.advance(OPP_MOVE_DELAY_MS + LEARN_GAP_MS);
      if (pass === 0) clock.advance(LEARN_SHOW_MS);
      s.onMove({ orig: 'g1' as any, dest: 'f3' as any });
      clock.advance(OPP_MOVE_DELAY_MS + LEARN_GAP_MS);
      expect(s.phase).toBe('LINE_DONE');
      expect(s.pendingRepeat).toBe(pass < 2);
      expect(calls.learned.length).toBe(pass < 2 ? 0 : 1);
      s.continueLine();
    }
    expect(calls.learned).toEqual([KEY_A]);
    expect(s.qIndex).toBe(1);
    expect(s.learnPass).toBe(0);
    expect(s.phase).toBe('LEARN_SHOW');
    expect(calls.reviewed).toEqual([]);
  });

  it('extend hängt an, ohne die laufende Linie zu stören', () => {
    const { s, clock, lines } = setup({});
    s.start([lines[0]]);
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    s.extend([lines[0], lines[1]]);
    expect(s.sessionUserMoves).toBe(4);
    expect(s.phase).toBe('FEEDBACK');
    clock.advance(ADVANCE_MS.correct);
    expect(s.currentPly).toBe(1);
  });

  it('clearTimers räumt alle Timer (Abbau)', () => {
    const { s, clock, lines } = setup({ color: 'b', lines: 1 });
    s.start(lines);
    expect(clock.pending).toBe(1);
    s.clearTimers();
    expect(clock.pending).toBe(0);
    clock.advance(10_000);
    expect(s.currentPly).toBe(0);
  });

  it('Premove nur im Gegnerzug-Fenster bzw. nach richtigem Zug (Abfragen)', () => {
    const { s, lines } = setup({ lines: 1 });
    s.start(lines);
    expect(s.premovable).toBeFalse();
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(s.premovable).toBeTrue();
  });

  it('Zugliste verrät im Abfragen-Modus den gesuchten Zug nicht', () => {
    const { s, lines } = setup({ lines: 1 });
    s.start(lines);
    expect(s.movesInLine.length).toBe(0);
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(s.movesInLine.map(m => m.san)).toEqual(['e4']);
    expect(s.currentLineLabel).toBe('1.e4 e5');
  });
});
