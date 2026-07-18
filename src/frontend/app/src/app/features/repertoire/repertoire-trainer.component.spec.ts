import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto } from './repertoire-training.service';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';

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
  forceColor = true,
  offlineQueue?: any,
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
  // Trainingsfarbe ist jetzt pro Kapitel (Auto-Erkennung + Override). Für deterministische Tests
  // jedes im PGN vorkommende Kapitel per localStorage-Override auf die gewünschte Farbe zwingen
  // (außer die Auto-Erkennung selbst wird getestet → forceColor=false).
  if (forceColor) {
    const chapters: Record<string, 'w' | 'b'> = {};
    for (const m of pgn.matchAll(/\[Black "([^"]*)"\]/g)) chapters[m[1].trim()] = color;
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify(chapters));
  }
  // forceColor=false → localStorage NICHT anfassen (Test setzt Overrides/Auto-Erkennung selbst).
  const c = new RepertoireTrainerComponent(route, training, prefs, translate, cdr, stockfish, dialog, offlineQueue ?? ({ enqueue: () => {} } as any));
  c.ngOnInit();
  return c;
}

describe('RepertoireTrainerComponent (line mode, due-strict pool)', () => {
  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_color_1');
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
  });
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

  it('auto-detects the trained color per chapter in a color-mixed repertoire (no override)', () => {
    // „White rep": Linie endet auf Weiß-Zug (Nf3) → Weiß trainiert. „Black rep": endet auf Schwarz
    // (d5) → Schwarz trainiert. Ohne globalen Toggle wird jede Linie aus ihrer eigenen Seite gespielt.
    const MIXED = [
      '[Event "R"]', '[White "Sicilian"]', '[Black "White rep"]', '', '1. e4 c5 2. Nf3 *', '',
      '[Event "R"]', '[White "French"]', '[Black "Black rep"]', '', '1. e4 e6 2. d4 d5 *', '',
    ].join('\n');
    const kW = lineKeyFromSans(['e4', 'c5', 'Nf3']);
    const kB = lineKeyFromSans(['e4', 'e6', 'd4', 'd5']);
    const st = [state(kW, PAST()), state(kB, PAST())];
    const cW = make('w', 'White rep', MIXED, st, undefined, false);
    expect(cW.color).toBe('w');   // Weiß am Zug (spielt e4 selbst)
    const cB = make('w', 'Black rep', MIXED, st, undefined, false);
    expect(cB.color).toBe('b');   // Schwarz: Gegner-e4 wird automatisch gespielt
  });

  it('a per-chapter override beats the auto-detection', () => {
    const MIXED = [
      '[Event "R"]', '[White "Sicilian"]', '[Black "White rep"]', '', '1. e4 c5 2. Nf3 *', '',
    ].join('\n');
    const kW = lineKeyFromSans(['e4', 'c5', 'Nf3']);
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'White rep': 'b' }));
    const c = make('w', 'White rep', MIXED, [state(kW, PAST())], undefined, false);
    expect(c.color).toBe('b');   // Override 'b' schlägt Auto 'w'
  });

  it('correct user move advances the ply and plays opponent auto-response', fakeAsync(() => {
    const c = make('w', 'Chapter A');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
    tick(3000); tick(400);   // correct-Feedback bleibt 3 s stehen, dann Gegnerzug
    expect(c.phase).toBe('PLAYING');
  }));

  it('a fully correct line reports reviewLine(correct=true) at the end', fakeAsync(() => {
    const spy = jasmine.createSpy('reviewLine').and.returnValue(of(state(KEY_A, FUTURE())));
    const c = make('w', 'Chapter A', PGN, [state(KEY_A, PAST())], spy);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // e4
    tick(3000); tick(400);                                 // advance + opp e5
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });   // Nf3
    tick(3000); tick(400);                                 // advance + opp Nc6 → finishLine
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
    tick(3000); tick(400);      // advance + opp Nc6 → finishLine
    expect(spy.calls.mostRecent().args[1].correct).toBeFalse();
  }));

  it('mouseslip forgives a wrong move: the line still reports correct', fakeAsync(() => {
    const spy = jasmine.createSpy('reviewLine').and.returnValue(of(state(KEY_A, FUTURE())));
    const c = make('w', 'Chapter A', PGN, [state(KEY_A, PAST())], spy);
    c.onMove({ orig: 'a2' as any, dest: 'a3' as any });   // falsch
    expect(c.outcome).toBe('wrong');
    c.mouseslip();                                        // verzeihen → kein Fehler
    expect(c.phase).toBe('PLAYING');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // korrekt
    tick(3000); tick(400);
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });   // Nf3
    tick(3000); tick(400);                                 // → finishLine
    expect(spy.calls.mostRecent().args[1].correct).toBeTrue();
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

  it('learn mode: line must be played 3× (1 learn + 2 replays) before it is promoted to the pool', fakeAsync(() => {
    const promote = jasmine.createSpy('promote').and.returnValue(of({ affected: 1 }));
    const route: any = {
      snapshot: {
        paramMap: { get: () => '1' },
        queryParamMap: { get: (k: string) => k === 'mode' ? 'learn' : null },
      },
    };
    const training: any = {
      getPgn: () => of(PGN),
      getLineStates: () => of([]),               // nichts im Pool → alle Linien lernbar
      reviewLine: () => of(state(KEY_A, FUTURE())),
      promote, makeDue: () => of({ affected: 0 }), reset: () => of({ deleted: 0 }),
    };
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));   // beide Kapitel als Weiß trainieren
    const c = new RepertoireTrainerComponent(
      route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any,
    );
    c.ngOnInit();
    expect(c.mode).toBe('learn');

    // Eine Linie (Chapter A: e4/Nf3) muss LEARN_REPEATS=3 mal durchgespielt werden.
    // Pass 0 zeigt die Züge vor (LEARN_SHOW → retract → PLAYING); Pass 1/2 verlangen sie aus dem
    // Gedächtnis (direkt PLAYING, kein LEARN_SHOW).
    for (let pass = 0; pass < 3; pass++) {
      if (pass === 0) {
        expect(c.phase).toBe('LEARN_SHOW');
        tick(1000);                                       // ohne Kommentar → nach LEARN_SHOW_MS zurücknehmen
      }
      expect(c.phase).toBe('PLAYING');
      c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
      tick(400); tick(800);                               // Gegner e5 + Pause → nächster Halbzug
      if (pass === 0) tick(1000);                         // Show Nf3 → retract (nur Pass 0)
      expect(c.phase).toBe('PLAYING');
      c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
      tick(400); tick(800);                               // Gegner Nc6 → finishLine → LINE_DONE
      expect(c.phase).toBe('LINE_DONE');
      if (pass < 2) {
        expect(promote).not.toHaveBeenCalled();           // erst nach dem 3. Durchlauf
        c.continueLine();                                 // „Weiter" → dieselbe Linie erneut
      }
    }
    expect(promote).toHaveBeenCalled();
  }));

  it('learn mode: comment on an opponent move holds (COMMENT phase) until confirmed', fakeAsync(() => {
    const withOppComment = [
      '[Event "Rep"]',
      '[White "1.e4 e5"]',
      '[Black "Chapter A"]',
      '',
      '1. e4 e5 {Open game — Black contests the centre.} 2. Nf3 Nc6 *',
      '',
    ].join('\n');
    const route: any = {
      snapshot: {
        paramMap: { get: () => '1' },
        queryParamMap: { get: (k: string) => k === 'mode' ? 'learn' : null },
      },
    };
    const training: any = {
      getPgn: () => of(withOppComment),
      getLineStates: () => of([]),
      reviewLine: () => of(state(KEY_A, FUTURE())),
      promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 0 }), reset: () => of({ deleted: 0 }),
    };
    // Farbe wird pro Kapitel erkannt (Seite des letzten Zugs); diese Linie endet auf Schwarz →
    // per Override auf Weiß zwingen, damit Schwarz (e5) der GEGNERzug mit Kommentar ist.
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    const c = new RepertoireTrainerComponent(
      route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any,
    );
    c.ngOnInit();
    expect(c.phase).toBe('LEARN_SHOW');                   // e4 vorgezeigt (kein Kommentar)
    tick(1000);
    expect(c.phase).toBe('PLAYING');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // eigener Zug e4
    tick(400);                                            // Gegner spielt e5 (mit Kommentar)
    expect(c.phase).toBe('COMMENT');                      // → hält an statt kurz aufzublitzen
    expect(c.holdComment).toContain('Open game');
    c.continueFromComment();                              // „Weiter"
    expect(c.phase).not.toBe('COMMENT');
  }));

  it('learn repeat pass: wrong move reveals the expected move as a reminder (LEARN_SHOW)', fakeAsync(() => {
    const route: any = {
      snapshot: {
        paramMap: { get: () => '1' },
        queryParamMap: { get: (k: string) => k === 'mode' ? 'learn' : null },
      },
    };
    const training: any = {
      getPgn: () => of(PGN),
      getLineStates: () => of([]),
      reviewLine: () => of(state(KEY_A, FUTURE())),
      promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 0 }), reset: () => of({ deleted: 0 }),
    };
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));   // beide Kapitel als Weiß trainieren
    const c = new RepertoireTrainerComponent(
      route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any,
    );
    c.ngOnInit();
    // In einen Wiederholungs-Durchlauf versetzen: der 2. Durchlauf zeigt NICHT vor → direkt PLAYING.
    (c as any).learnPass = 1;
    (c as any).startCurrentLine();
    expect(c.phase).toBe('PLAYING');                      // kein LEARN_SHOW im Wiederholungs-Durchlauf
    c.onMove({ orig: 'e2' as any, dest: 'e3' as any });   // falscher Zug (erwartet e4)
    expect(c.phase).toBe('LEARN_SHOW');                   // → Zug wird als Erinnerung eingeblendet
    tick(1000);
    expect(c.phase).toBe('PLAYING');                      // danach wieder spielbar
  }));

  it('continueLine only advances from LINE_DONE (manual continue, no auto-advance)', () => {
    const c = make('w', 'Chapter A');
    // In PLAYING tut continueLine nichts (kein versehentliches Vorrücken).
    expect(c.phase).toBe('PLAYING');
    c.continueLine();
    expect(c.phase).toBe('PLAYING');
    // Aus LINE_DONE rückt continueLine zur nächsten Linie vor.
    (c as any).phase = 'LINE_DONE';
    c.pendingRepeat = false;
    c.continueLine();
    expect(c.phase).not.toBe('LINE_DONE');
  });

  it('movesInLine hides the unplayed current move in quiz PLAYING (no spoiler), reveals it otherwise', () => {
    const c = make('w', 'Chapter A');
    // Quiz + PLAYING, currentPly 0: der noch nicht gespielte eigene Zug darf NICHT erscheinen
    // (sonst stünde die Lösung in der Zug-Liste). Nichts Gespieltes → leere Liste.
    expect(c.phase).toBe('PLAYING');
    expect(c.movesInLine.length).toBe(0);
    // Cursor auf 2 (zwei bereits gespielte Halbzüge) → beide 'past', der aktuelle bleibt verborgen.
    (c as any).currentPly = 2;
    let m = c.movesInLine;
    expect(m.length).toBe(2);
    expect(m[0].state).toBe('past');
    expect(m[1].state).toBe('past');
    // Außerhalb von PLAYING (Feedback nach dem Zug) wird der aktuelle Halbzug gezeigt.
    (c as any).phase = 'FEEDBACK';
    m = c.movesInLine;
    expect(m.length).toBe(3);
    expect(m[2].state).toBe('current');
    expect(m[2].num).toBe(2);                       // Nummer bei Weiß-Halbzug des 2. Zugs
  });

  it('premovable is true while the opponent is auto-moving (quiz), false on the user’s turn', () => {
    const c = make('w', 'Chapter A');
    // Nutzer (Weiß) am Zug → kein Premove.
    expect(c.phase).toBe('PLAYING');
    expect(c.premovable).toBe(false);
    // Gegnerzug-Fenster: oppMoving + Brett zeigt Schwarz am Zug → Premove erlaubt.
    (c as any).oppMoving = true;
    (c as any).fen = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
    expect(c.premovable).toBe(true);
  });

  it('learn-mode comment surfaces PGN comment of the current move', () => {
    const withComment = [
      '[Event "Rep"]',
      '[White "Philidor"]',
      '[Black "Chapter A"]',
      '',
      '1. e4 e5 2. Nf3 d6 {Philidor Defence — solid, primitive defence of e5.} 3. d4 exd4 *',
      '',
    ].join('\n');
    const c = make('w', null, withComment, [
      state(lineKeyFromSans(['e4', 'e5', 'Nf3', 'd6', 'd4', 'exd4']), PAST()),
    ]);
    // currentPly=3 = Schwarz-Halbzug d6, an dem der Kommentar hängt.
    (c as any).currentPly = 3;
    expect(c.currentComment).toContain('Philidor Defence');
    expect(c.currentCommentParagraphs.length).toBe(1);
    expect(c.currentMovePrettyLabel).toBe('2… d6');
    // Am nächsten Halbzug (d4, Ply 4) gibt es keinen Kommentar mehr.
    (c as any).currentPly = 4;
    expect(c.currentComment).toBe('');
    expect(c.currentCommentParagraphs).toEqual([]);
    expect(c.currentMovePrettyLabel).toBe('3. d4');
  });

  it('streak: correct move increments (best follows); showSolution resets, bestStreak stays', () => {
    const c = make('w', 'Chapter A');
    expect(c.currentStreak).toBe(0);
    expect(c.bestStreak).toBe(0);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });      // richtig → Streak 1
    expect(c.currentStreak).toBe(1);
    expect(c.bestStreak).toBe(1);
    // Falscher Zug OHNE Zwischen-Advance: Guard verhindert onMove-Verarbeitung (phase=FEEDBACK).
    // Wir simulieren daher direkt einen pendingWrong-Zustand und lassen showSolution die Serie brechen.
    (c as any).currentStreak = 5;
    (c as any).bestStreak = 5;
    (c as any).phase = 'FEEDBACK';
    (c as any).outcome = 'wrong';
    (c as any).pendingWrong = true;
    (c as any).wrongRevealed = false;
    c.showSolution();
    expect(c.currentStreak).toBe(0);                          // Serie gebrochen
    expect(c.bestStreak).toBe(5);                             // Session-Best bleibt
    expect(c.wrong).toBeGreaterThan(0);                       // showSolution zählt als Fehler
  });

  it('mouseslip forgives a wrong move and preserves the streak', () => {
    const c = make('w', 'Chapter A');
    // Am Ausgangs-Ply falsch spielen (a3 statt e4) — pendingWrong=true, Streak bleibt 0.
    c.onMove({ orig: 'a2' as any, dest: 'a3' as any });
    expect(c.outcome).toBe('wrong');
    expect(c.currentStreak).toBe(0);
    // Mausrutscher verzeihen — der offene Fehler zählt NICHT, Streak unangetastet.
    c.mouseslip();
    expect(c.currentStreak).toBe(0);
    expect(c.phase).toBe('PLAYING');
    // Danach den korrekten Zug spielen: Streak wächst auf 1 (der Mausrutscher hat nichts gebrochen).
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
    expect(c.currentStreak).toBe(1);
    expect(c.bestStreak).toBe(1);
  });

  it('runAdvance clears its own advanceTimer to prevent a double-fire (manual click + scheduled)', () => {
    // Direkt und race-frei: manuell scheduleAdvance simulieren, dann runAdvance aufrufen —
    // advanceTimer muss danach null sein (der scheduled setTimeout kann nicht mehr feuern).
    const c = make('w', 'Chapter A');
    (c as any).outcome = 'correct';
    (c as any).phase = 'FEEDBACK';
    (c as any).scheduleAdvance(3000);
    expect((c as any).advanceTimer).not.toBeNull();
    (c as any).runAdvance();
    expect((c as any).advanceTimer).toBeNull();
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
      { enqueue: () => {} } as any,
    );
    c.ngOnInit();
    expect(c.phase).toBe('EMPTY');
  });
});

/**
 * Offline-Training: Init fällt auf die heruntergeladene Kopie zurück; SR-Bewertungen werden lokal
 * berechnet (Backend-Spiegel), in die Offline-Kopie gespiegelt und via Offline-Queue nachgereicht.
 */
describe('RepertoireTrainerComponent offline', () => {
  const OFFLINE_KEY = REPERTOIRE_OFFLINE_PREFIX + '1';

  function makeOffline(
    states: LineStateDto[],
    reviewSpy: jasmine.Spy = jasmine.createSpy('reviewLine'),
    enqueue: jasmine.Spy = jasmine.createSpy('enqueue'),
    withCache = true,
  ): RepertoireTrainerComponent {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));
    if (withCache) {
      localStorage.setItem(OFFLINE_KEY, JSON.stringify({
        meta: { id: 1, name: 'Rep' }, pgn: PGN, states, config: null, savedAt: '2026-07-18T00:00:00Z',
      }));
    }
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } } };
    const training: any = {
      getPgn: () => throwError(() => new Error('offline')),
      getLineStates: () => throwError(() => new Error('offline')),
      reviewLine: reviewSpy,
      promote: () => of({ affected: 1 }),
    };
    const c = new RepertoireTrainerComponent(
      route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue } as any,
    );
    c.ngOnInit();
    return c;
  }

  afterEach(() => {
    localStorage.removeItem(OFFLINE_KEY);
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
  });

  it('falls back to the downloaded copy when the server is unreachable', () => {
    const c = makeOffline([state(KEY_A, PAST()), state(KEY_B, PAST())]);
    expect(c.offlineSession).toBeTrue();
    expect(c.queue.length).toBe(2);
    expect(c.phase).toBe('PLAYING');
  });

  it('stays EMPTY without a downloaded copy', () => {
    const c = makeOffline([], jasmine.createSpy(), jasmine.createSpy(), false);
    expect(c.offlineSession).toBeFalse();
    expect(c.phase).toBe('EMPTY');
  });

  it('a failed review is computed locally, mirrored into the copy and queued for reconnect', fakeAsync(() => {
    const reviewSpy = jasmine.createSpy('reviewLine').and.returnValue(throwError(() => new Error('offline')));
    const enqueue = jasmine.createSpy('enqueue');
    const c = makeOffline([state(KEY_A, PAST())], reviewSpy, enqueue);
    // Linie A fehlerfrei durchspielen → finishLine → lokale Bewertung
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    tick(3000); tick(400);
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    tick(3000); tick(400);
    expect(c.phase).toBe('LINE_DONE');
    expect(enqueue).toHaveBeenCalledWith('POST', '/api/repertoires/1/training/line-review',
      jasmine.objectContaining({ lineKey: KEY_A, correct: true }));
    // Fälligkeit lokal berechnet (Stufe 1 → 2) und im „Linie fertig"-Kasten angezeigt
    expect(c.nextRepeatAt).not.toBeNull();
    const cached = JSON.parse(localStorage.getItem(OFFLINE_KEY)!);
    const st = cached.states.find((s: any) => s.lineKey === KEY_A);
    expect(st.level).toBe(2);
    expect(new Date(st.dueAt).getTime()).toBeGreaterThan(Date.now());
  }));
});
