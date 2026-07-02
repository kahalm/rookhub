import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';

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

function make(color: 'w' | 'b' = 'w', queryChapter: string | null = null, pgn: string = PGN): RepertoireTrainerComponent {
  const route: any = {
    snapshot: {
      paramMap: { get: () => '1' },
      queryParamMap: { get: (k: string) => k === 'chapter' ? queryChapter : null },
    },
  };
  const training: any = {
    getPgn: () => of(pgn),
    getCards: () => of([]),
    review: () => of({ cardKey: '', expectedMove: '', reps: 0, lapses: 0, intervalDays: 0, ease: 2.5, dueAt: '', lastReviewedAt: null }),
    reset: () => of({ deleted: 0 }),
  };
  const prefs: any = { boardTheme: 'brown', pieceSet: 'cburnett' };
  const translate: any = { instant: (k: string) => k };
  const cdr: any = { markForCheck: () => {} };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const c = new RepertoireTrainerComponent(route, training, prefs, translate, cdr, stockfish);
  c.color = color;
  c.ngOnInit();
  return c;
}

describe('RepertoireTrainerComponent (line mode)', () => {
  it('builds a queue from all lines when no chapter filter is set', () => {
    const c = make('w', null);
    expect(c.queue.length).toBe(2);
    expect(c.phase).toBe('PLAYING');   // erste Linie gestartet, Weiß ist am Zug (e2-e4 erwartet)
  });

  it('filters lines by chapter query param', () => {
    const c = make('w', 'Chapter B');
    expect(c.queue.length).toBe(1);
    expect((c as any).queue[0].headers.Black).toBe('Chapter B');
  });

  it('sets phase EMPTY when no line matches the chapter', () => {
    const c = make('w', 'Chapter Z');
    expect(c.phase).toBe('EMPTY');
  });

  it('correct user move advances the ply and plays opponent auto-response', fakeAsync(() => {
    const c = make('w', 'Chapter A');
    // Chapter A hat 1.e4 e5 2.Nf3 Nc6 — weißer 1. Zug erwartet: e4
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
    expect(c.correct).toBe(1);
    tick(600);    // ADVANCE_MS.correct
    tick(400);    // OPP_MOVE_DELAY_MS
    // Nach automatischem Gegnerzug (e7-e5) sollte Weiß wieder am Zug sein (Nf3 erwarten).
    expect(c.phase).toBe('PLAYING');
    expect(c.correct).toBe(1);
  }));

  it('wrong move keeps fen at start and increments only on showSolution', () => {
    const c = make('w', 'Chapter A');
    const startFen = c.fen;
    c.onMove({ orig: 'a2' as any, dest: 'a3' as any });   // falsch (erwartet e4)
    expect(c.outcome).toBe('wrong');
    expect(c.wrong).toBe(0);   // erst nach „Lösung zeigen"
    expect(c.fen).toBe(startFen);   // Zug SOFORT zurückgenommen
    c.showSolution();
    expect(c.wrong).toBe(1);
    expect(c.wrongRevealed).toBeTrue();
    expect(c.fen).not.toBe(startFen);   // erwarteter Zug e2-e4 wird gespielt
  });

  it('a move outside expected + accepted is treated as wrong (no auto-advance)', () => {
    const c = make('w', 'Chapter A');
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });   // d4 ist im PGN nur Kapitel-B-Linie
    expect(c.outcome).toBe('wrong');
    expect(c.phase).toBe('FEEDBACK');
  });

  it('tolerated move is taken back and the same ply stays playable (no auto-play of the main move)', fakeAsync(() => {
    const c = make('w', null, PGN_ALT);
    const startFen = c.fen;
    const plyBefore = (c as any).currentPly;
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });   // geduldete Alternative zu e4
    expect(c.outcome).toBe('tolerated');
    expect(c.fen).not.toBe(startFen);   // Zug bleibt zunächst sichtbar
    tick(1500);   // ADVANCE_MS.tolerated
    // Regression: der erwartete Hauptzug (e4) darf NICHT für den User gespielt werden.
    expect(c.fen).toBe(startFen);        // geduldeter Zug zurückgenommen
    expect(c.phase).toBe('PLAYING');     // dieselbe Stellung wieder spielbar
    expect((c as any).currentPly).toBe(plyBefore);
    // Der User zieht den Hauptzug jetzt selbst → korrekt.
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
    expect(c.correct).toBe(1);
  }));

  it('resetProgress calls the backend, clears state and rebuilds the queue', () => {
    spyOn(window, 'confirm').and.returnValue(true);
    const c = make('w', null);
    const resetSpy = jasmine.createSpy('reset').and.returnValue(of({ deleted: 3 }));
    (c as any).training.reset = resetSpy;
    c.resetProgress();
    expect(resetSpy).toHaveBeenCalledWith(1);
    expect(c.resetting).toBeFalse();
    expect(c.queue.length).toBe(2);   // Queue neu aufgebaut
  });

  it('resetProgress does nothing when user cancels the confirm', () => {
    spyOn(window, 'confirm').and.returnValue(false);
    const c = make('w', null);
    const resetSpy = jasmine.createSpy('reset');
    (c as any).training.reset = resetSpy;
    c.resetProgress();
    expect(resetSpy).not.toHaveBeenCalled();
  });

  it('LOADING failure sets phase to EMPTY', () => {
    const route: any = {
      snapshot: {
        paramMap: { get: () => '1' },
        queryParamMap: { get: () => null },
      },
    };
    const training: any = {
      getPgn: () => throwError(() => new Error('nope')),
      getCards: () => of([]),
    };
    const c = new RepertoireTrainerComponent(
      route, training, {} as any, { instant: (k: string) => k } as any,
      { markForCheck: () => {} } as any, { init: () => Promise.resolve() } as any,
    );
    c.ngOnInit();
    expect(c.phase).toBe('EMPTY');
  });
});
