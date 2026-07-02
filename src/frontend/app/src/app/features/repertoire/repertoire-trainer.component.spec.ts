import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto } from './repertoire-training.service';

/** Minimal-PGN mit zwei einfachen Linien für den Line-basierten Trainer. */
const PGN = [
  '[Event "Rep"]',
  '[White "1.e4 e5"]',
  '[Black "Chapter A"]',
  '',
  '1. e4 e5 2. Nf3 Nc6 *',
  '',
  '[Event "Rep"]',
  '[White "1.d4 d5"]',
  '[Black "Chapter B"]',
  '',
  '1. d4 d5 2. c4 e6 *',
  '',
].join('\n');

/** Linie mit einer geduldeten Alternative ([%alt d4]) zum weißen Hauptzug e4. */
const PGN_ALT = [
  '[Event "Rep"]',
  '[White "1.e4"]',
  '[Black "Chapter A"]',
  '',
  '1. e4 {[%alt d4]} e5 2. Nf3 Nc6 *',
  '',
].join('\n');

const KEY_A = lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6']);
const KEY_B = lineKeyFromSans(['d4', 'd5', 'c4', 'e6']);

function state(lineKey: string, dueAtMs: number, extra: Partial<LineStateDto> = {}): LineStateDto {
  return {
    lineKey, level: 1, reps: 1, lapses: 0,
    dueAt: new Date(dueAtMs).toISOString(), lastReviewedAt: null,
    inPool: true, paused: false, ...extra,
  };
}

const PAST = () => Date.now() - 3_600_000;
const FUTURE = () => Date.now() + 3_600_000;

function make(
  color: 'w' | 'b' = 'w',
  queryChapter: string | null = null,
  pgn: string = PGN,
  states: LineStateDto[] = [state(KEY_A, PAST()), state(KEY_B, PAST())],
  reviewSpy?: jasmine.Spy,
): RepertoireTrainerComponent {
  const route: any = {
    snapshot: {
      paramMap: { get: () => '1' },
      queryParamMap: { get: (k: string) => k === 'chapter' ? queryChapter : null },
    },
  };
  const training: any = {
    getPgn: () => of(pgn),
    getLineStates: () => of(states),
    reviewLine: reviewSpy ?? (() => of(state(KEY_A, FUTURE()))),
    promote: () => of({ affected: 1 }),
    makeDue: () => of({ affected: 1 }),
    reset: () => of({ deleted: 0 }),
  };
  const prefs: any = { boardTheme: 'brown', pieceSet: 'cburnett' };
  const translate: any = { instant: (k: string) => k };
  const cdr: any = { markForCheck: () => {} };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const dialog: any = { open: () => ({ afterClosed: () => of(false) }) };
  const c = new RepertoireTrainerComponent(route, training, prefs, translate, cdr, stockfish, dialog);
  c.color = color;
  c.ngOnInit();
  return c;
}

describe('RepertoireTrainerComponent (line mode, due-strict pool)', () => {
  it('builds a queue from all DUE pool lines when no chapter filter is set', () => {
    const c = make('w', null);
    expect(c.queue.length).toBe(2);
    expect(c.phase).toBe('PLAYING');
  });

  it('excludes lines that are NOT in the pool (empty states → EMPTY, nothing in pool)', () => {
    const c = make('w', null, PGN, []);   // keine Zustände = nichts gelernt
    expect(c.phase).toBe('EMPTY');
    expect(c.nextDueAt).toBeNull();
  });

  it('EMPTY with a future next-due when all pool lines are scheduled ahead', () => {
    const c = make('w', null, PGN, [state(KEY_A, FUTURE()), state(KEY_B, FUTURE())]);
    expect(c.phase).toBe('EMPTY');
    expect(c.nextDueAt).not.toBeNull();
  });

  it('excludes paused lines from the pool', () => {
    const c = make('w', null, PGN, [state(KEY_A, PAST(), { paused: true }), state(KEY_B, PAST())]);
    expect(c.queue.length).toBe(1);
  });

  it('filters lines by chapter query param', () => {
    const c = make('w', 'Chapter B');
    expect(c.queue.length).toBe(1);
    expect((c as any).queue[0].headers.Black).toBe('Chapter B');
  });

  it('correct user move advances the ply and plays opponent auto-response', fakeAsync(() => {
    const c = make('w', 'Chapter A');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
    tick(600); tick(400);
    expect(c.phase).toBe('PLAYING');
  }));

  it('a fully correct line reports reviewLine(correct=true) at the end', fakeAsync(() => {
    const spy = jasmine.createSpy('reviewLine').and.returnValue(of(state(KEY_A, FUTURE())));
    const c = make('w', 'Chapter A', PGN, [state(KEY_A, PAST())], spy);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // e4
    tick(600); tick(400);                                 // advance + opp e5
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });   // Nf3
    tick(600); tick(400);                                 // advance + opp Nc6 → finishLine
    expect(spy).toHaveBeenCalled();
    expect(spy.calls.mostRecent().args[1].correct).toBeTrue();
  }));

  it('a wrong move in the line makes the final reviewLine(correct=false)', fakeAsync(() => {
    const spy = jasmine.createSpy('reviewLine').and.returnValue(of(state(KEY_A, FUTURE())));
    const c = make('w', 'Chapter A', PGN, [state(KEY_A, PAST())], spy);
    c.onMove({ orig: 'a2' as any, dest: 'a3' as any });   // falsch (erwartet e4)
    expect(c.outcome).toBe('wrong');
    c.showSolution();          // spielt e4
    c.continueAfterWrong();    // → opp e5
    tick(400);
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });   // Nf3 korrekt
    tick(600); tick(400);      // advance + opp Nc6 → finishLine
    expect(spy.calls.mostRecent().args[1].correct).toBeFalse();
  }));

  it('tolerated move is taken back and stays playable (no auto-play of the main move)', fakeAsync(() => {
    const c = make('w', null, PGN_ALT, [state(KEY_A, PAST())]);
    const startFen = c.fen;
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });   // geduldete Alternative zu e4
    expect(c.outcome).toBe('tolerated');
    tick(1500);
    expect(c.fen).toBe(startFen);
    expect(c.phase).toBe('PLAYING');
  }));

  it('resetProgress clears state and empties the pool', () => {
    spyOn(window, 'confirm').and.returnValue(true);
    const c = make('w', null);
    const resetSpy = jasmine.createSpy('reset').and.returnValue(of({ deleted: 3 }));
    (c as any).training.reset = resetSpy;
    c.resetProgress();
    expect(resetSpy).toHaveBeenCalledWith(1);
    expect(c.phase).toBe('EMPTY');   // nach Reset ist nichts mehr im Pool
  });

  it('promoteAllToPool calls promote with all usable line keys', () => {
    const c = make('w', null, PGN, []);
    const spy = jasmine.createSpy('promote').and.returnValue(of({ affected: 2 }));
    (c as any).training.promote = spy;
    c.promoteAllToPool();
    expect(spy).toHaveBeenCalled();
    expect(spy.calls.mostRecent().args[1].length).toBe(2);   // beide Linien
  });

  it('LOADING failure sets phase to EMPTY', () => {
    const route: any = {
      snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } },
    };
    const training: any = {
      getPgn: () => throwError(() => new Error('nope')),
      getLineStates: () => of([]),
    };
    const c = new RepertoireTrainerComponent(
      route, training, {} as any, { instant: (k: string) => k } as any,
      { markForCheck: () => {} } as any, { init: () => Promise.resolve() } as any, {} as any,
    );
    c.ngOnInit();
    expect(c.phase).toBe('EMPTY');
  });
});
