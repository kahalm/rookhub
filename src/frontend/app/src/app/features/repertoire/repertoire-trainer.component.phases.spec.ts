import { fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { EMPTY, Subject, of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto } from './repertoire-training.service';
import { DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';

/**
 * Charakterisierung der Phasenfolge des Trainers (Zerlegung Z21): die Übergänge der
 * 8-Phasen-Maschine, die `repertoire-trainer.component.spec.ts` noch nicht abdeckt — festgehalten
 * gegen den Stand VOR dem Herauslösen von `TrainerSession`, damit der Umbau sie nicht verschiebt.
 */
const PGN = [
  '[Event "Rep"]', '[White "1.e4 e5"]', '[Black "Chapter A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
  '[Event "Rep"]', '[White "1.d4 d5"]', '[Black "Chapter B"]', '', '1. d4 d5 2. c4 e6 *', '',
].join('\n');
const KEY_A = lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6']);
const KEY_B = lineKeyFromSans(['d4', 'd5', 'c4', 'e6']);
const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function state(lineKey: string, dueAtMs: number): LineStateDto {
  return { lineKey, level: 1, reps: 1, lapses: 0, dueAt: new Date(dueAtMs).toISOString(), lastReviewedAt: null, inPool: true, paused: false };
}
const PAST = () => Date.now() - 3_600_000;
const FUTURE_ISO = '2099-01-01T00:00:00.000Z';

interface Opts {
  color?: 'w' | 'b';
  query?: Record<string, string>;
  pgn?: string;
  states?: LineStateDto[];
  training?: Record<string, unknown>;
  stockfish?: unknown;
  enqueue?: jasmine.Spy;
}

function make(o: Opts = {}): RepertoireTrainerComponent {
  const pgn = o.pgn ?? PGN;
  const chapters: Record<string, 'w' | 'b'> = {};
  for (const m of pgn.matchAll(/\[Black "([^"]*)"\]/g)) chapters[m[1].trim()] = o.color ?? 'w';
  localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify(chapters));
  const query = o.query ?? {};
  const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
  const training: any = {
    getPgn: () => of(pgn),
    getLineStates: () => of(o.states ?? [state(KEY_A, PAST()), state(KEY_B, PAST())]),
    reviewLine: () => of({ ...state(KEY_A, PAST()), dueAt: FUTURE_ISO }),
    promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
    ...o.training,
  };
  const stockfish: any = o.stockfish ?? { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
    { instant: (k: string) => k } as any, { markForCheck: () => {} } as any, stockfish, {} as any,
    { enqueue: o.enqueue ?? (() => {}) } as any,
    { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } as any,
    { warn: () => ({}) } as any, { ask: () => of(true) } as any);
  c.ngOnInit();
  return c;
}

const mv = (orig: string, dest: string) => ({ orig: orig as any, dest: dest as any });

describe('RepertoireTrainerComponent — Phasenfolge (Charakterisierung Z21)', () => {
  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  it('Schwarz-Linie: Start in PLAYING mit Gegnerzug-Fenster (oppMoving, keine Züge), nach 250 ms ist der Nutzer dran', fakeAsync(() => {
    const c = make({ color: 'b', query: { chapter: 'Chapter A' } });
    expect(c.phase).toBe('PLAYING');
    expect(c.oppMoving).toBeTrue();
    expect(c.dests.size).toBe(0);
    expect(c.fen).toBe(START);
    tick(249);
    expect(c.fen).toBe(START);
    tick(1);
    expect(c.oppMoving).toBeFalse();
    expect(c.phase).toBe('PLAYING');
    expect(c.lastMove).toEqual(['e2', 'e4'] as any);
    expect(c.dests.size).toBeGreaterThan(0);
  }));

  it('richtig → FEEDBACK; Tippen (onPlayClick) rückt sofort vor, der geplante Timer feuert nicht mehr', fakeAsync(() => {
    const c = make({ query: { chapter: 'Chapter A' } });
    c.onMove(mv('e2', 'e4'));
    expect(c.phase).toBe('FEEDBACK');
    expect(c.outcome).toBe('correct');
    c.onPlayClick();
    expect(c.phase).toBe('PLAYING');
    expect(c.oppMoving).toBeTrue();
    tick(250);                                  // Gegner e5
    expect(c.oppMoving).toBeFalse();
    expect((c as any).currentPly).toBe(2);
    tick(1000);                                 // kein zweites Vorrücken
    expect((c as any).currentPly).toBe(2);
    expect(c.phase).toBe('PLAYING');
  }));

  it('Leertaste wirkt wie Tippen und verhindert den Standard; in PLAYING tut sie nichts', fakeAsync(() => {
    const c = make({ query: { chapter: 'Chapter A' } });
    const idle = new KeyboardEvent('keydown', { key: ' ', cancelable: true });
    c.onKeyDown(idle);
    expect(idle.defaultPrevented).toBeFalse();
    c.onMove(mv('e2', 'e4'));
    const ev = new KeyboardEvent('keydown', { key: ' ', cancelable: true });
    c.onKeyDown(ev);
    expect(ev.defaultPrevented).toBeTrue();
    expect(c.oppMoving).toBeTrue();
    tick(250);
  }));

  it('geduldet → Tippen nimmt sofort zurück (PLAYING, Ausgangsstellung)', fakeAsync(() => {
    const alt = '[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 {[%alt d4]} e5 2. Nf3 Nc6 *\n';
    const c = make({ pgn: alt, states: [state(KEY_A, PAST())] });
    c.onMove(mv('d2', 'd4'));
    expect(c.outcome).toBe('tolerated');
    c.onPlayClick();
    expect(c.phase).toBe('PLAYING');
    expect(c.fen).toBe(START);
    expect(c.lastMove).toBeUndefined();
    tick(2000);
    expect(c.phase).toBe('PLAYING');
  }));

  it('falsch → erneuter Versuch ohne Mausrutscher: der offene Fehler zählt, die Linie meldet correct=false', fakeAsync(() => {
    const review = jasmine.createSpy('reviewLine').and.returnValue(of({ ...state(KEY_A, PAST()), dueAt: FUTURE_ISO }));
    const c = make({ query: { chapter: 'Chapter A' }, training: { reviewLine: review } });
    c.onMove(mv('a2', 'a3'));
    expect(c.outcome).toBe('wrong');
    expect(c.phase).toBe('FEEDBACK');
    expect(c.isPlayable).toBeTrue();            // sofortiger Neuversuch erlaubt
    expect(c.dests.size).toBeGreaterThan(0);
    expect(c.wrong).toBe(0);                    // noch kein echter Fehler
    c.onMove(mv('e2', 'e4'));
    expect(c.outcome).toBe('correct');
    expect(c.wrong).toBe(1);
    expect(c.correct).toBe(1);
    expect(c.currentStreak).toBe(1);
    tick(300); tick(250);
    c.onMove(mv('g1', 'f3'));
    tick(300); tick(250);
    expect(c.phase).toBe('LINE_DONE');
    expect(review.calls.mostRecent().args[1]).toEqual({ lineKey: KEY_A, label: '1.e4 e5', correct: false });
  }));

  it('Abfragen: LINE_DONE zeigt die Fälligkeit aus der Server-Antwort; „Weiter" nach der letzten Linie → DONE', fakeAsync(() => {
    const c = make({ query: { chapter: 'Chapter A' } });
    c.onMove(mv('e2', 'e4')); tick(300); tick(250);
    c.onMove(mv('g1', 'f3')); tick(300); tick(250);
    expect(c.phase).toBe('LINE_DONE');
    expect(c.pendingRepeat).toBeFalse();
    expect(c.nextRepeatAt).toBe(FUTURE_ISO);
    c.continueLine();
    expect(c.phase).toBe('DONE');
  }));

  it('Abfragen: nach LINE_DONE startet „Weiter" die nächste Linie frisch (Zähler bleiben, Linie zurückgesetzt)', fakeAsync(() => {
    const c = make();
    const first = c.queue[0];
    c.onMove(first.headers['White'] === '1.e4 e5' ? mv('e2', 'e4') : mv('d2', 'd4')); tick(300); tick(250);
    c.onMove(first.headers['White'] === '1.e4 e5' ? mv('g1', 'f3') : mv('c2', 'c4')); tick(300); tick(250);
    expect(c.phase).toBe('LINE_DONE');
    c.onPlayClick();                            // Tippen = „Weiter"
    expect(c.qIndex).toBe(1);
    expect(c.phase).toBe('PLAYING');
    expect(c.fen).toBe(START);
    expect(c.nextRepeatAt).toBeNull();
    expect(c.correct).toBe(2);
  }));

  it('Abbau während des Gegnerzug-Fensters: kein Zug mehr', fakeAsync(() => {
    const c = make({ color: 'b', query: { chapter: 'Chapter A' } });
    c.ngOnDestroy();
    tick(1000);
    expect(c.fen).toBe(START);
    expect((c as any).currentPly).toBe(0);
  }));

  it('Moduswechsel baut neu auf: Lernen nimmt nur Linien außerhalb des Pools, zeigt vor (LEARN_SHOW)', fakeAsync(() => {
    const c = make({ states: [state(KEY_A, PAST())] });
    expect(c.queue.length).toBe(1);
    c.setMode('learn');
    expect(c.mode).toBe('learn');
    expect(c.queue.map(l => l.headers['White'])).toEqual(['1.d4 d5']);
    expect(c.phase).toBe('LEARN_SHOW');
    expect(c.expectedDisplay).toBe('d4');
    tick(1000);
    expect(c.phase).toBe('PLAYING');
    c.setMode('learn');                         // gleicher Modus: nichts passiert
    expect(c.phase).toBe('PLAYING');
  }));

  it('Lernen: Vorzeigen MIT Kommentar hält, Tippen nimmt zurück (PLAYING)', fakeAsync(() => {
    const pgn = '[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 {Zentrum.} e5 2. Nf3 Nc6 *\n';
    const c = make({ pgn, states: [], query: { mode: 'learn' } });
    expect(c.phase).toBe('LEARN_SHOW');
    expect(c.learnComment).toBe('Zentrum.');
    tick(5000);
    expect(c.phase).toBe('LEARN_SHOW');         // hält ohne Timer
    c.onPlayClick();
    expect(c.phase).toBe('PLAYING');
    expect(c.learnComment).toBe('');
    expect(c.fen).toBe(START);
  }));

  it('Lernen: COMMENT-Halt geht auch per Tippen weiter', fakeAsync(() => {
    const pgn = '[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 e5 {Offen.} 2. Nf3 Nc6 *\n';
    const c = make({ pgn, states: [], query: { mode: 'learn' } });
    tick(1000);
    c.onMove(mv('e2', 'e4'));
    tick(250);
    expect(c.phase).toBe('COMMENT');
    c.onPlayClick();
    expect(c.holdComment).toBe('');
    expect(c.phase).toBe('LEARN_SHOW');         // nächster eigener Zug wird vorgezeigt
    tick(1000);
  }));

  it('Lernen: scheitert die Aufnahme in den Pool, landet sie in der Offline-Queue; danach nächste Linie mit Vorzeigen', fakeAsync(() => {
    const enqueue = jasmine.createSpy('enqueue');
    const c = make({ states: [], query: { mode: 'learn' }, enqueue, training: { promote: () => throwError(() => new Error('500')) } });
    for (let pass = 0; pass < 3; pass++) {
      if (pass === 0) tick(1000);
      c.onMove(mv('e2', 'e4'));
      tick(250); tick(400);
      if (pass === 0) tick(1000);
      c.onMove(mv('g1', 'f3'));
      tick(250); tick(400);
      expect(c.phase).toBe('LINE_DONE');
      expect(c.pendingRepeat).toBe(pass < 2);
      if (pass < 2) c.continueLine();
    }
    expect(enqueue).toHaveBeenCalledWith('POST', '/api/repertoires/1/training/promote', { lineKeys: [KEY_A] });
    c.continueLine();
    expect(c.qIndex).toBe(1);
    expect(c.phase).toBe('LEARN_SHOW');
    expect(c.currentLineLabel).toBe('1.d4 d5');
    tick(1000);
  }));

  describe('Bewertungsvergleich nach einem Fehlzug', () => {
    function withEvals(player: string, rep: string) {
      const getEval = jasmine.createSpy('getEval').and.returnValues(Promise.resolve(player), Promise.resolve(rep));
      return { init: () => Promise.resolve(), getEval };
    }

    it('rechnet den Unterschied in Bauern aus Sicht der Trainingsfarbe', fakeAsync(() => {
      const sf = withEvals('0.2', '0.7');
      const c = make({ query: { chapter: 'Chapter A' }, stockfish: sf });
      c.onMove(mv('a2', 'a3'));
      expect(c.evalLoading).toBeTrue();
      flushMicrotasks();
      expect(c.evalLoading).toBeFalse();
      expect(c.evalDeltaPawns).toBeCloseTo(-0.5, 6);
      expect(c.evalMateNote).toBeNull();
      expect(c.evalDeltaAbsDisplay).toBe('0.5');
      expect(sf.getEval.calls.argsFor(0)[1]).toBe(14);
    }));

    it('verpasstes Matt', fakeAsync(() => {
      const c = make({ query: { chapter: 'Chapter A' }, stockfish: withEvals('0.5', '#3') });
      c.onMove(mv('a2', 'a3'));
      flushMicrotasks();
      expect(c.evalMateNote).toBe('missed');
    }));

    it('ein Neuversuch verwirft ein spät eintreffendes Ergebnis', fakeAsync(() => {
      const c = make({ query: { chapter: 'Chapter A' }, stockfish: withEvals('0.2', '0.7') });
      c.onMove(mv('a2', 'a3'));
      c.onMove(mv('e2', 'e4'));                 // Neuversuch → alter Vergleich ungültig
      flushMicrotasks();
      expect(c.evalLoading).toBeFalse();
      expect(c.evalDeltaPawns).toBeNull();
      tick(300); tick(250);
    }));
  });
});

/** Codereview F3-025: das erste Laden wird beim Verlassen abbestellt — keine Explorer-Läufe ohne Abnehmer. */
describe('RepertoireTrainerComponent — Abbau vor der ersten Antwort (F3-025)', () => {
  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem('rookhub_rep_train_freq_order');
  });

  function makeSlow(pgn: Subject<string>, run: jasmine.Spy): RepertoireTrainerComponent {
    localStorage.setItem('rookhub_rep_train_freq_order', '1');   // „Häufigste zuerst" an
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } } };
    const training: any = { getPgn: () => pgn, getLineStates: () => of([state(KEY_A, PAST())]) };
    const c = new RepertoireTrainerComponent(route, training, {} as any, { instant: (k: string) => k } as any,
      { markForCheck: () => {} } as any, { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any,
      {} as any, { enqueue: () => {} } as any,
      { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } as any, { warn: () => ({}) } as any, {} as any);
    c.ngOnInit();
    return c;
  }

  it('Verlassen, bevor das PGN da ist: die Antwort startet keinen Explorer-Lauf', () => {
    const pgn = new Subject<string>();
    const run = jasmine.createSpy('run').and.returnValue(EMPTY);
    const c = makeSlow(pgn, run);
    c.ngOnDestroy();
    expect(pgn.observed).toBeFalse();           // Abruf abbestellt
    pgn.next(PGN); pgn.complete();
    expect(run).not.toHaveBeenCalled();
    expect(c.phase).toBe('LOADING');
  });

  it('ohne Verlassen läuft der Explorer wie bisher', () => {
    const pgn = new Subject<string>();
    const run = jasmine.createSpy('run').and.returnValue(EMPTY);
    makeSlow(pgn, run);
    pgn.next(PGN); pgn.complete();
    expect(run).toHaveBeenCalledTimes(1);
  });
});
