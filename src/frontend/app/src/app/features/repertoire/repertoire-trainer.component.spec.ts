import { fakeAsync, tick } from '@angular/core/testing';
import { EMPTY, of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto } from './repertoire-training.service';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { Chess } from 'chess.js';
import { DEFAULT_EXPLORER_SETTINGS, ExplorerAnalysisResult } from './repertoire-explorer.service';

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

/** Explorer ohne Wirkung — „Häufigste zuerst" ist in den übrigen Tests aus. */
const NO_EXPLORER: any = { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) };

/** Explorer-Stub mit gegebener `run`-Antwort und der Vorgabe-Auswahl. */
function explorerWith(run: any): any {
  return { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) };
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
  explorer: any = NO_EXPLORER,
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
  const c = new RepertoireTrainerComponent(route, training, prefs, translate, cdr, stockfish, dialog, offlineQueue ?? ({ enqueue: () => {} } as any), explorer);
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

  it('info lines ([%info] or „Info | " in White) are neither quizzed nor learned nor promoted', () => {
    const info = [
      '[Event "Rep"]', '[White "Idee"]', '[Black "Chapter A"]', '', '{[%info]} 1. c4 e5 2. Nc3 Nf6 *', '',
      '[Event "Rep"]', '[White "Info | Plan"]', '[Black "Chapter B"]', '', '1. Nf3 d5 2. g3 Nf6 *', '',
    ].join('\n');
    const keyInfo1 = lineKeyFromSans(['c4', 'e5', 'Nc3', 'Nf6']);
    const keyInfo2 = lineKeyFromSans(['Nf3', 'd5', 'g3', 'Nf6']);
    const pgn = PGN + '\n' + info;
    const quiz = make('w', null, pgn, [state(KEY_A, PAST()), state(KEY_B, PAST()), state(keyInfo1, PAST()), state(keyInfo2, PAST())]);
    expect(quiz.queue.length).toBe(2);   // nur die beiden echten Linien, obwohl die Info-Linien fällig im Pool liegen

    const c = make('w', null, pgn, []);
    const spy = jasmine.createSpy('promote').and.returnValue(of({ affected: 2 }));
    (c as any).training.promote = spy;
    c.promoteAllToPool();
    expect(spy.calls.mostRecent().args[1]).toEqual([KEY_A, KEY_B]);
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
      NO_EXPLORER,
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
      NO_EXPLORER,
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
      NO_EXPLORER,
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
      NO_EXPLORER,
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
      NO_EXPLORER,
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

/**
 * Der Trainer urteilt seit 0.499.10 über den gemeinsamen Kern `shared/chess/line-solver`
 * (`judgeMove`/`resolveExpectedUci`) — verglichen werden FELDER, nicht mehr normalisierter
 * Zug-TEXT.
 *
 * <p><b>Gemessen, nicht angenommen</b>: `parsePgnText` liefert die Linie als chess.js-`Move`-Objekte
 * (`chess.loadPgn` → `history({verbose:true})`), deren `san` IMMER die kanonische Schreibweise von
 * chess.js ist — ein `Nbd2` im PGN kommt als `Nd2` an, ein `e2e4` als `e4`. Ein Test, der nur ein
 * PGN hineingibt, prüft deshalb den PARSER und nicht den Vergleich. Die Tests hier schreiben die
 * SAN der Linie darum bewusst auf eine NICHT kanonische Form um: genau so sähe sie aus, wenn die
 * Linie einmal nicht über ein Brett kanonisiert ankommt — und genau daran hing bisher „richtig".</p>
 */
describe('RepertoireTrainerComponent — Feldvergleich statt Zug-TEXT', () => {
  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_color_1');
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
  });

  /** Linien-Schlüssel eines einzeiligen Test-PGN — aus demselben Weg wie im Trainer (`lineKeyOf`). */
  const keyOf = (pgn: string) => lineKeyFromSans(parsePgnText(pgn)[0].moves.map(m => m.san));

  /** Schreibt die SAN eines Halbzugs der laufenden Linie um (siehe Kommentar oben). */
  const writeSan = (c: RepertoireTrainerComponent, ply: number, san: string) => {
    (c as any).queue[(c as any).qIndex].moves[ply].san = san;
  };

  const game = (white: string, moves: string, fen?: string) => [
    '[Event "Rep"]', `[White "${white}"]`, '[Black "Chapter A"]',
    ...(fen ? [`[FEN "${fen}"]`] : []), '', moves, '',
  ].join('\n');

  const CASTLING_FEN = 'r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1';
  const PROMO_FEN = '4k3/P7/8/8/8/8/8/4K3 w - - 0 1';
  /** Weiß am Zug, NUR der b1-Springer kommt nach d2 — chess.js schreibt dort `Nd2`. */
  const ONE_KNIGHT_FEN = 'rnbqkbnr/ppp1pppp/8/3p4/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 0 3';
  /** Weiß am Zug, BEIDE Springer (b1 und f3) kommen nach d2 — `Nd2` ist dort mehrdeutig. */
  const TWO_KNIGHTS_FEN = 'rnbqkbnr/ppp1pppp/8/3p4/3P4/5N2/PPP1PPPP/RNBQKB1R w KQkq - 0 3';

  const open = (pgn: string) => make('w', null, pgn, [state(keyOf(pgn), PAST())]);

  it('ein UNTERSCHEIDER in der Linie (Nbd2 statt Nd2) zählt als richtig — dieselben Felder', () => {
    // Nur der b1-Springer kommt nach d2, chess.js schreibt also `Nd2`; die Linie trägt `Nbd2`, wie
    // es ein Chessable-PGN notiert. Früher: `normSan('Nbd2') !== 'Nd2'` → Fehlzug.
    const c = open(game('Nbd2', '3. Nd2 Nf6 *', ONE_KNIGHT_FEN));
    writeSan(c, 0, 'Nbd2');
    c.onMove({ orig: 'b1' as any, dest: 'd2' as any });
    expect(c.outcome).toBe('correct');
  });

  it('LANG-ALGEBRAISCH in der Linie (e2e4) zählt als richtig', () => {
    const c = open(game('lang', '1. e4 e5 2. Nf3 Nc6 *'));
    writeSan(c, 0, 'e2e4');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
  });

  it('ein Unterscheider mitten in der Linie (Nbd2 statt Nd2) zählt als richtig', fakeAsync(() => {
    const pgn = game('Nbd2', '1. d4 d5 2. Nd2 Nf6 *');
    const c = open(pgn);
    writeSan(c, 2, 'Nbd2');                       // Halbzug 2 = der weiße Springerzug
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });
    tick(3000); tick(400);                        // Feedback + Gegnerzug d5
    expect(c.phase).toBe('PLAYING');
    c.onMove({ orig: 'b1' as any, dest: 'd2' as any });
    expect(c.outcome).toBe('correct');
    tick(3000); tick(400);
  }));

  it('ein wirklich ANDERER Zug bleibt falsch — der Feldvergleich ist milder, nicht blind', () => {
    const c = open(game('lang', '1. e4 e5 2. Nf3 Nc6 *'));
    writeSan(c, 0, 'e2e4');
    c.onMove({ orig: 'a2' as any, dest: 'a3' as any });
    expect(c.outcome).toBe('wrong');
  });

  it('Regression: 0-0 in der Linie bleibt der Rochadezug', () => {
    const c = open(game('Rochade', '1. O-O O-O *', CASTLING_FEN));
    writeSan(c, 0, '0-0');
    c.onMove({ orig: 'e1' as any, dest: 'g1' as any });
    expect(c.outcome).toBe('correct');
  });

  it('Regression: c8Q-Schreibweise in der Linie bleibt der Umwandlungszug', () => {
    const c = open(game('Umwandlung', '1. a8=Q Kd7 *', PROMO_FEN));
    writeSan(c, 0, 'a8Q');
    c.onMove({ orig: 'a7' as any, dest: 'a8' as any, promotion: 'q' });
    expect(c.outcome).toBe('correct');
  });

  it('Umwandlung OHNE gewählte Figur: es zählt, und die Figur der LINIE landet auf dem Brett', () => {
    // Die eine wirklich sichtbare Verhaltensänderung. Vorher wurde der Nutzerzug mit Dame-Vorgabe
    // gespielt und gegen `a8=R` als FALSCH verglichen; jetzt passt ein Zug ohne genannte Figur auf
    // jede Umwandlung (Regel des Kerns) — und aufs Brett kommt der Turm der Linie, nicht die Dame.
    const c = open(game('Unterverwandlung', '1. a8=R Kd7 *', PROMO_FEN));
    c.onMove({ orig: 'a7' as any, dest: 'a8' as any });
    expect(c.outcome).toBe('correct');
    expect(c.fen.split('/')[0]).toBe('R3k3');
  });

  it('eine ANDERE genannte Umwandlungsfigur bleibt ein Fehlzug', () => {
    const c = open(game('Unterverwandlung', '1. a8=R Kd7 *', PROMO_FEN));
    c.onMove({ orig: 'a7' as any, dest: 'a8' as any, promotion: 'q' });
    expect(c.outcome).toBe('wrong');
  });

  it('eine geduldete Alternative bleibt geduldet — und der Hauptzug wird weiter verlangt', fakeAsync(() => {
    const c = make('w', null, PGN_ALT, [state(KEY_A, PAST())]);
    const startFen = c.fen;
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });   // [%alt d4]
    expect(c.outcome).toBe('tolerated');
    tick(1500);                                           // zurücknehmen → dieselbe Stellung
    expect(c.fen).toBe(startFen);
    expect(c.phase).toBe('PLAYING');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // erst der Hauptzug führt weiter
    expect(c.outcome).toBe('correct');
    tick(3000); tick(400);
  }));

  it('der HAUPTZUG ist keine Alternative, auch wenn [%alt] ihn mitnennt', () => {
    // Früher siebte `accepted.delete(expectedSan)` ihn aus der Menge; jetzt steckt die Regel im
    // Kern: `judgeMove` prüft den erwarteten Zug ZUERST und antwortet `correct`.
    const pgn = [
      '[Event "Rep"]', '[White "1.e4"]', '[Black "Chapter A"]', '',
      '1. e4 {[%alt e4 d4]} e5 2. Nf3 Nc6 *', '',
    ].join('\n');
    const c = make('w', null, pgn, [state(KEY_A, PAST())]);
    expect((c as any).altsAt((c as any).fen.split(' ').slice(0, 4).join(' ')).length).toBe(2);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');                    // NICHT 'tolerated'
  });

  it('„Lösung zeigen" spielt auch einen nicht kanonisch notierten Zug aufs Brett', () => {
    const c = open(game('lang', '1. e4 e5 2. Nf3 Nc6 *'));
    writeSan(c, 0, 'e2e4');
    const startFen = c.fen;
    c.onMove({ orig: 'a2' as any, dest: 'a3' as any });   // falsch → FEEDBACK
    c.showSolution();
    expect(c.fen).not.toBe(startFen);
    expect(c.lastMove).toEqual(['e2', 'e4'] as any);
  });

  it('MEHRDEUTIGE SAN in der Linie: kein Zug gilt als richtig, die Linie bleibt heil', () => {
    // Der Kern löst `Nd2` bei zwei erreichbaren Springern zu `null` auf — geraten wird nicht.
    // Für den Trainer heißt das: wie ein nicht auflösbarer Zug behandeln. Kein „richtig",
    // „Lösung zeigen" enthüllt nur den TEXT (nichts wird gespielt), und der Trainer läuft weiter.
    const c = open(game('mehrdeutig', '3. Nbd2 Nf6 *', TWO_KNIGHTS_FEN));
    writeSan(c, 0, 'Nd2');
    const startFen = c.fen;
    c.onMove({ orig: 'b1' as any, dest: 'd2' as any });
    expect(c.outcome).toBe('wrong');
    expect(c.phase).toBe('FEEDBACK');
    expect(c.fen).toBe(startFen);
    c.showSolution();
    expect(c.wrongRevealed).toBeTrue();
    expect(c.expectedDisplay).toBe('Nd2');
    expect(c.fen).toBe(startFen);                         // nichts geraten, nichts gespielt
    expect(() => c.continueAfterWrong()).not.toThrow();
  });

  it('Lern-Modus akzeptiert NUR den erwarteten Zug, keine Alternative', fakeAsync(() => {
    const route: any = {
      snapshot: {
        paramMap: { get: () => '1' },
        queryParamMap: { get: (k: string) => k === 'mode' ? 'learn' : null },
      },
    };
    const training: any = {
      getPgn: () => of(PGN_ALT),
      getLineStates: () => of([]),                        // nichts im Pool → lernbar
      reviewLine: () => of(state(KEY_A, FUTURE())),
      promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 0 }), reset: () => of({ deleted: 0 }),
    };
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    const c = new RepertoireTrainerComponent(
      route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any,
      NO_EXPLORER,
    );
    c.ngOnInit();
    expect(c.phase).toBe('LEARN_SHOW');
    tick(1000);
    expect(c.phase).toBe('PLAYING');
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });   // die im Abfragen-Modus GEDULDETE Alternative
    expect(c.phase).toBe('LEARN_SHOW');                   // → im Lernen nicht akzeptiert, nochmal vorzeigen
    expect((c as any).currentPly).toBe(0);
    tick(1000);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // nur der erwartete Zug führt weiter
    expect((c as any).currentPly).toBe(1);
    tick(400); tick(800);
  }));

  it('Lern-Modus: ein nicht kanonisch notierter erwarteter Zug zählt ebenfalls', fakeAsync(() => {
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
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));
    const c = new RepertoireTrainerComponent(
      route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any,
      NO_EXPLORER,
    );
    c.ngOnInit();
    (c as any).learnPass = 1;                             // Wiederholungs-Durchlauf: kein Vorzeigen
    (c as any).startCurrentLine();
    expect(c.phase).toBe('PLAYING');
    (c as any).queue[(c as any).qIndex].moves[0].san = 'e2e4';
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect((c as any).currentPly).toBe(1);                // akzeptiert, kein erneutes Vorzeigen
    tick(400); tick(800);
  }));
});

describe('RepertoireTrainerComponent „Häufigste zuerst"', () => {
  /** Endstellung einer Linie im Schlüssel des Servers (erste drei FEN-Felder). */
  function endKey(sans: string[]): string {
    const chess = new Chess();
    for (const san of sans) chess.move(san);
    return chess.fen().split(' ').slice(0, 3).join(' ');
  }

  function result(extra: Partial<ExplorerAnalysisResult> = {}): ExplorerAnalysisResult {
    return {
      complete: true, positionsAnalyzed: 3, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
      tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [],
      lineFrequencies: {
        [endKey(['e4', 'e5', 'Nf3', 'Nc6'])]: 0.1,
        [endKey(['d4', 'd5', 'c4', 'e6'])]: 0.4,
      },
      ...extra,
    };
  }

  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem('rookhub_rep_train_freq_order');
  });

  it('asks the explorer for line frequencies of all chapters and puts the most frequent line first', () => {
    localStorage.setItem('rookhub_rep_train_freq_order', '1');
    const run = jasmine.createSpy('run').and.returnValue(of(result()));
    const explorer = explorerWith(run);

    const c = make('w', null, PGN, undefined, undefined, true, undefined, explorer);

    const req = run.calls.mostRecent().args[1];
    expect(req.color).toBeNull();
    expect(req.includeLineFrequencies).toBeTrue();
    expect(req.includeHoles).toBeFalse();
    expect(req.chapterColors).toEqual({ 'Chapter A': 'w', 'Chapter B': 'w' });
    expect(req.source).toBe('local');       // Vorgabe: lokaler Explorer, Meisterpartien
    expect(req.database).toBe('masters');
    expect(c.queue.map(l => l.headers['White'])).toEqual(['1.d4 d5', '1.e4 e5']);
    expect(c.currentLineFrequency).toBeCloseTo(0.4, 6);
    expect(c.freqNotice).toBeNull();
  });

  it('is off by default: no explorer call, no frequency shown', () => {
    const run = jasmine.createSpy('run').and.returnValue(of(result()));
    const explorer = explorerWith(run);

    const c = make('w', null, PGN, undefined, undefined, true, undefined, explorer);

    expect(run).not.toHaveBeenCalled();
    expect(c.currentLineFrequency).toBeNull();
    expect(c.queue.length).toBe(2);
  });

  it('without a token the session still starts, in the usual order, and says why', () => {
    localStorage.setItem('rookhub_rep_train_freq_order', '1');
    const explorer = explorerWith(() => of(result({ complete: false, tokenMissing: true, lineFrequencies: {} })));

    const c = make('w', null, PGN, undefined, undefined, true, undefined, explorer);

    expect(c.phase).toBe('PLAYING');
    expect(c.queue.length).toBe(2);
    expect(c.freqNotice).toBe('repertoireTrainer.freqToken');
  });

  it('toggling remembers the choice and rebuilds the queue in frequency order', () => {
    const run = jasmine.createSpy('run').and.returnValue(of(result()));
    const explorer = explorerWith(run);
    const c = make('w', null, PGN, undefined, undefined, true, undefined, explorer);

    c.toggleFreqOrder();

    expect(localStorage.getItem('rookhub_rep_train_freq_order')).toBe('1');
    expect(run).toHaveBeenCalledTimes(1);
    expect(c.queue[0].headers['White']).toBe('1.d4 d5');

    c.toggleFreqOrder();
    expect(localStorage.getItem('rookhub_rep_train_freq_order')).toBe('0');
    expect(c.currentLineFrequency).toBeNull();
  });
});
