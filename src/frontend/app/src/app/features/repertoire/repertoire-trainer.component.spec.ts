import { fakeAsync, tick } from '@angular/core/testing';
import { EMPTY, Subject, of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto } from './repertoire-training.service';
import { TOLERATED_HOLD_MS } from './repertoire-trainer-session';
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
const NO_SNACKBAR: any = { warn: () => ({}) };
/** Rückfrage (ConfirmService) sofort bestätigt — `resetProgress` fragt vorher. */
const CONFIRM_YES: any = { ask: () => of(true) };

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
  const c = new RepertoireTrainerComponent(route, training, prefs, translate, cdr, stockfish, dialog, offlineQueue ?? ({ enqueue: () => {} } as any), explorer, NO_SNACKBAR, CONFIRM_YES);
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
    expect((c as any).session.queue[0].headers.Black).toBe('Chapter B');
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
    tick(TOLERATED_HOLD_MS);
    expect(c.fen).toBe(startFen);
    expect(c.phase).toBe('PLAYING');
  }));

  it('resetProgress clears state and empties the pool', () => {
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

  describe('scheitert nicht still (Fehler → Snackbar + Ansicht markiert)', () => {
    /** Snackbar und ChangeDetector als Spione einsetzen — die Komponente ist OnPush. */
    function watchFeedback(c: RepertoireTrainerComponent) {
      const warn = jasmine.createSpy('warn');
      const markForCheck = jasmine.createSpy('markForCheck');
      (c as any).snackbar = { warn };
      (c as any).cdr = { markForCheck };
      return { warn, markForCheck };
    }

    it('resetProgress: Fehler meldet sich, gibt den Knopf frei und markiert die OnPush-Ansicht', () => {
      const c = make('w', null);
      const { warn, markForCheck } = watchFeedback(c);
      (c as any).training.reset = () => throwError(() => new Error('500'));
      c.resetProgress();
      expect(c.resetting).toBeFalse();
      expect(markForCheck).toHaveBeenCalled();
      expect(warn).toHaveBeenCalledWith('repertoireTrainer.resetFailed');
    });

    it('promoteAllToPool: Fehler meldet sich', () => {
      const c = make('w', null, PGN, []);
      const { warn } = watchFeedback(c);
      (c as any).training.promote = () => throwError(() => new Error('500'));
      c.promoteAllToPool();
      expect(c.poolBusy).toBeFalse();
      expect(warn).toHaveBeenCalledWith('repertoireTrainer.poolFailed');
    });

    it('makeAllDue: Fehler meldet sich', () => {
      const c = make('w', null);
      const { warn } = watchFeedback(c);
      (c as any).training.makeDue = () => throwError(() => new Error('500'));
      c.makeAllDue();
      expect(c.poolBusy).toBeFalse();
      expect(warn).toHaveBeenCalledWith('repertoireTrainer.poolFailed');
    });

    it('Neuladen nach geglückter Pool-Aktion scheitert: sagt, dass nur die Anzeige veraltet ist', () => {
      const c = make('w', null);
      const { warn } = watchFeedback(c);
      (c as any).training.getLineStates = () => throwError(() => new Error('502'));
      c.makeAllDue();
      expect(c.poolBusy).toBeFalse();
      expect(warn).toHaveBeenCalledWith('repertoireTrainer.reloadFailed');
    });
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
      NO_SNACKBAR,
      {} as any,
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
      NO_SNACKBAR,
      {} as any,
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
      NO_SNACKBAR,
      {} as any,
    );
    c.ngOnInit();
    // In einen Wiederholungs-Durchlauf versetzen: der 2. Durchlauf zeigt NICHT vor → direkt PLAYING.
    (c as any).session.learnPass = 1;
    (c as any).session.startCurrentLine();
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
    (c as any).session.phase = 'LINE_DONE';
    (c as any).session.pendingRepeat = false;
    c.continueLine();
    expect(c.phase).not.toBe('LINE_DONE');
  });

  it('Linienende: Klick ins Brett geht nur weiter, wenn nicht analysiert wird (Engine aus, nicht weggeblättert)', () => {
    const c = make('w', 'Chapter A');
    const s = (c as any).session;
    const toLineEnd = () => { s.phase = 'LINE_DONE'; s.pendingRepeat = false; s.reviewPly = s.currentPly; };
    // weggeblättert: bleibt stehen
    toLineEnd(); s.reviewPly = s.currentPly - 1;
    expect(s.reviewing).toBeTrue();
    c.onPlayClick();
    expect(c.phase).toBe('LINE_DONE');
    // Engine an (und verfügbar): bleibt stehen
    toLineEnd();
    c.reviewEngineOn = true;
    spyOnProperty(c, 'reviewEngineAvailable', 'get').and.returnValue(true);
    c.onPlayClick();
    expect(c.phase).toBe('LINE_DONE');
    // nichts davon: Klick geht weiter
    c.reviewEngineOn = false;
    c.onPlayClick();
    expect(c.phase).not.toBe('LINE_DONE');
  });

  it('movesInLine hides the unplayed current move in quiz PLAYING (no spoiler), reveals it otherwise', () => {
    const c = make('w', 'Chapter A');
    // Quiz + PLAYING, currentPly 0: der noch nicht gespielte eigene Zug darf NICHT erscheinen
    // (sonst stünde die Lösung in der Zug-Liste). Nichts Gespieltes → leere Liste.
    expect(c.phase).toBe('PLAYING');
    expect(c.movesInLine.length).toBe(0);
    // Cursor auf 2 (zwei bereits gespielte Halbzüge) → beide 'past', der aktuelle bleibt verborgen.
    (c as any).session.currentPly = 2;
    let m = c.movesInLine;
    expect(m.length).toBe(2);
    expect(m[0].state).toBe('past');
    expect(m[1].state).toBe('past');
    // Außerhalb von PLAYING (Feedback nach dem Zug) wird der aktuelle Halbzug gezeigt.
    (c as any).session.phase = 'FEEDBACK';
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
    (c as any).session.oppMoving = true;
    (c as any).session.fen = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
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
    (c as any).session.currentPly = 3;
    expect(c.currentComment).toContain('Philidor Defence');
    expect(c.currentCommentParagraphs.length).toBe(1);
    expect(c.currentMovePrettyLabel).toBe('2… d6');
    // Am nächsten Halbzug (d4, Ply 4) gibt es keinen Kommentar mehr.
    (c as any).session.currentPly = 4;
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
    (c as any).session.currentStreak = 5;
    (c as any).session.bestStreak = 5;
    (c as any).session.phase = 'FEEDBACK';
    (c as any).session.outcome = 'wrong';
    (c as any).session.pendingWrong = true;
    (c as any).session.wrongRevealed = false;
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
    (c as any).session.outcome = 'correct';
    (c as any).session.phase = 'FEEDBACK';
    (c as any).session.scheduleAdvance(3000);
    expect((c as any).session.advanceTimer).not.toBeNull();
    (c as any).session.runAdvance();
    expect((c as any).session.advanceTimer).toBeNull();
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
      NO_SNACKBAR,
      {} as any,
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
      NO_SNACKBAR,
      {} as any,
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
    (c as any).session.queue[(c as any).session.qIndex].moves[ply].san = san;
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
    tick(TOLERATED_HOLD_MS);                              // zurücknehmen → dieselbe Stellung
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
    expect((c as any).session.altsAt((c as any).session.fen.split(' ').slice(0, 4).join(' ')).length).toBe(2);
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
    expect(c.wrongShown).toBeTrue();                      // 0.886.0: der Fehlzug steht, bis „Nochmal"/„Lösung zeigen"
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
      NO_SNACKBAR,
      {} as any,
    );
    c.ngOnInit();
    expect(c.phase).toBe('LEARN_SHOW');
    tick(1000);
    expect(c.phase).toBe('PLAYING');
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });   // die im Abfragen-Modus GEDULDETE Alternative
    expect(c.phase).toBe('LEARN_SHOW');                   // → im Lernen nicht akzeptiert, nochmal vorzeigen
    expect((c as any).session.currentPly).toBe(0);
    tick(1000);
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });   // nur der erwartete Zug führt weiter
    expect((c as any).session.currentPly).toBe(1);
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
      NO_SNACKBAR,
      {} as any,
    );
    c.ngOnInit();
    (c as any).session.learnPass = 1;                             // Wiederholungs-Durchlauf: kein Vorzeigen
    (c as any).session.startCurrentLine();
    expect(c.phase).toBe('PLAYING');
    (c as any).session.queue[(c as any).session.qIndex].moves[0].san = 'e2e4';
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect((c as any).session.currentPly).toBe(1);                // akzeptiert, kein erneutes Vorzeigen
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

describe('RepertoireTrainerComponent: Reihenfolge nach einem Gegner (?opponent=, 2026-10-07)', () => {
  /** Drei Linien: zwei Weiß-Kapitel, ein Schwarz-Kapitel. */
  const PGN3 = [
    '[Event "Rep"]', '[White "1.e4 e5"]', '[Black "Chapter A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
    '[Event "Rep"]', '[White "1.d4 d5"]', '[Black "Chapter B"]', '', '1. d4 d5 2. c4 e6 *', '',
    '[Event "Rep"]', '[White "1.e4 c5"]', '[Black "Chapter S"]', '', '1. e4 c5 2. Nf3 d6 *', '',
  ].join('\n');
  const KEY_S = lineKeyFromSans(['e4', 'c5', 'Nf3', 'd6']);

  function makeOpp(query: Record<string, string>, http: any, states: LineStateDto[] = [state(KEY_A, PAST()), state(KEY_B, PAST())]) {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w', 'Chapter S': 'b' }));
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
    const training: any = {
      getPgn: () => of(PGN3), getLineStates: () => of(states), reviewLine: () => of(state(KEY_A, FUTURE())),
      promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
    };
    const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any, { enqueue: () => {} } as any,
      NO_EXPLORER, NO_SNACKBAR, CONFIRM_YES, http);
    c.ngOnInit();
    return c;
  }

  const answer = (keys: string[], color: 'w' | 'b' = 'w') => ({
    repertoires: [], repertoire: 1, color, colors: ['w', 'b'], games: 9, total: keys.length, more: 0,
    lines: keys.map(key => ({ key, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
  });

  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  it('holt ALLE Trainingslinien gegen den Gegner (mit Filter, Kapitelfarben) und fragt in dieser Reihenfolge ab — nur die eigene Farbe', () => {
    const get = jasmine.createSpy('get').and.returnValue(of(answer([KEY_B, KEY_A])));
    const c = makeOpp({ opponent: 'prep:42', color: 'w', source: 'both', all: 'true', evil: 'x' }, { get });

    const [url, opts] = get.calls.mostRecent().args;
    expect(url).toBe('/api/prep/player/42/training-lines');
    expect(opts.params.get('repertoire')).toBe('1');
    expect(opts.params.get('take')).toBe('5000');
    expect(opts.params.get('color')).toBe('w');
    expect(opts.params.get('source')).toBe('both');
    expect(opts.params.get('all')).toBe('true');
    expect(opts.params.get('evil')).toBeNull();
    expect(JSON.parse(opts.params.get('chapterColors'))).toEqual({ 'Chapter A': 'w', 'Chapter B': 'w', 'Chapter S': 'b' });
    // gemischt wäre Zufall — hier stur in der Reihenfolge des Gegners, das Schwarz-Kapitel fällt weg
    expect(c.queue.map(l => l.headers['White'])).toEqual(['1.d4 d5', '1.e4 e5']);
    expect(c.opponentNotice).toBe('repertoireTrainer.opponentOrder');
  });

  it('?line= ist der Einstieg: die Linie sofort, die Reihung im Hintergrund, KEIN Modus-Wechsel trotz nichts fällig', () => {
    const answers = new Subject<any>();
    const get = jasmine.createSpy('get').and.returnValue(answers);
    // keine Zustände: nichts im Pool, nichts fällig — früher schaltete er in „Lernen"
    const c = makeOpp({ opponent: 'league:1606921', color: 'w', line: KEY_B }, { get }, []);

    expect(get.calls.mostRecent().args[0]).toBe('/api/league/player/1606921/training-lines');
    expect(c.phase).toBe('PLAYING');                              // nicht auf die Reihung warten
    expect(c.queue.map(l => l.headers['White'])).toEqual(['1.d4 d5']);
    expect(c.mode).toBe('quiz');

    answers.next(answer([KEY_A, KEY_B]));                         // Reihung: A vor B — nach dem Einstieg B kommt nichts mehr
    expect(c.queue.map(l => l.headers['White'])).toEqual(['1.d4 d5']);
    expect(c.mode).toBe('quiz');
    expect(c.opponentNotice).toBe('repertoireTrainer.opponentOrder');
  });

  it('Fehler beim Holen: wie gewohnt weiter, aber mit Hinweis', () => {
    const c = makeOpp({ opponent: 'prep:42', color: 'w' }, { get: () => throwError(() => new Error('500')) });
    expect(c.phase).toBe('PLAYING');
    expect(c.queue.length).toBe(2);
    expect(c.opponentNotice).toBe('repertoireTrainer.opponentFailed');
  });

  it('ohne ?opponent= (oder mit unbrauchbarem) bleibt alles wie bisher — kein Aufruf', () => {
    const get = jasmine.createSpy('get');
    const c = makeOpp({ opponent: 'evil:1' }, { get });
    expect(get).not.toHaveBeenCalled();
    expect(c.opponent).toBeNull();
    expect(c.opponentNotice).toBeNull();
    expect(c.queue.length).toBe(2);
  });

  describe('Einstieg mitten in der Reihung', () => {
    const PGN5 = [1, 2, 3, 4, 5].map(i => {
      const first = ['e4', 'd4', 'c4', 'Nf3', 'g3'][i - 1];
      return `[Event "Rep"]\n[White "L${i}"]\n[Black "W"]\n\n1. ${first} d5 2. b3 *\n`;
    }).join('\n');
    const keys = ['e4', 'd4', 'c4', 'Nf3', 'g3'].map(f => lineKeyFromSans([f, 'd5', 'b3']));

    function make5(query: Record<string, string>, http: any): RepertoireTrainerComponent {
      localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ W: 'w' }));
      const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
      const training: any = {
        getPgn: () => of(PGN5), getLineStates: () => of([]), reviewLine: () => of(state(keys[0], FUTURE())),
        promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
      };
      const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
        { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
        { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any, { enqueue: () => {} } as any,
        NO_EXPLORER, NO_SNACKBAR, CONFIRM_YES, http);
      c.ngOnInit();
      return c;
    }

    it('Einstieg bei Linie 3 → danach 4, 5; am Ende „Alle N Linien … durch"', () => {
      const get = jasmine.createSpy('get').and.returnValue(of(answer(keys)));
      const c = make5({ opponent: 'prep:42', color: 'w', line: keys[2] }, { get });
      expect(c.queue.map(l => l.headers['White'])).toEqual(['L3', 'L4', 'L5']);
      expect(c.opponentTotal).toBe(3);
      (c as any).session.qIndex = 3;
      (c as any).session.startCurrentLine();
      expect(c.phase).toBe('DONE');
      // „Von vorn": die ganze Reihung ab Linie 1
      c.restart();
      expect(c.queue.map(l => l.headers['White'])).toEqual(['L1', 'L2', 'L3', 'L4', 'L5']);
      expect(c.opponentTotal).toBe(5);
    });

    it('ohne ?opponent= bleibt ?line= eine Einzellinie mit Fälligkeit (nichts fällig → leer)', () => {
      const get = jasmine.createSpy('get');
      const c = make5({ line: keys[2] }, { get });
      expect(get).not.toHaveBeenCalled();
      expect(c.queue.length).toBe(0);
      expect(c.phase).toBe('EMPTY');
    });
  });
});

describe('RepertoireTrainerComponent: Linie pausieren (Taste p, 0.889.0)', () => {
  const PGN3 = [
    '[Event "Rep"]', '[White "1.e4 e5"]', '[Black "Chapter A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
    '[Event "Rep"]', '[White "1.d4 d5"]', '[Black "Chapter B"]', '', '1. d4 d5 2. c4 e6 *', '',
    '[Event "Rep"]', '[White "1.c4 e5"]', '[Black "Chapter C"]', '', '1. c4 e5 2. Nc3 Nf6 *', '',
  ].join('\n');
  const KEY_C = lineKeyFromSans(['c4', 'e5', 'Nc3', 'Nf6']);
  const ALL = () => [state(KEY_A, PAST()), state(KEY_B, PAST()), state(KEY_C, PAST())];

  function setup(query: Record<string, string> = {}, states: LineStateDto[] = ALL(), http: any = null) {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w', 'Chapter C': 'w' }));
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
    const setPaused = jasmine.createSpy('setPaused').and.returnValue(of({ affected: 1 }));
    const promote = jasmine.createSpy('promote').and.returnValue(of({ affected: 1 }));
    const reviewLine = jasmine.createSpy('reviewLine').and.returnValue(of(state(KEY_A, FUTURE())));
    const training: any = {
      getPgn: () => of(PGN3), getLineStates: () => of(states), reviewLine, promote, setPaused,
      makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
    };
    const undo = new Subject<void>();
    const snackbar: any = { warn: () => ({}), show: jasmine.createSpy('show').and.returnValue({ onAction: () => undo }) };
    const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any, { enqueue: () => {} } as any,
      NO_EXPLORER, snackbar, CONFIRM_YES, http);
    c.ngOnInit();
    return { c, setPaused, promote, reviewLine, snackbar, undo };
  }
  const key = (c: RepertoireTrainerComponent) => c.lineKeyOf((c as any).session.currentLine);

  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  it('pausieren ruft setPaused(repId, [lineKey], true), nimmt die Linie aus der Warteschlange und springt zur nächsten', () => {
    const { c, setPaused, reviewLine, snackbar } = setup();
    const first = key(c);
    expect(c.canPause).toBeTrue();
    c.pauseLine();
    expect(setPaused).toHaveBeenCalledWith(1, [first], true);
    expect(reviewLine).not.toHaveBeenCalled();
    expect(c.queue.length).toBe(2);
    expect(c.queue.map(l => c.lineKeyOf(l))).not.toContain(first);
    expect(key(c)).not.toBe(first);
    expect(c.phase).toBe('PLAYING');
    expect(snackbar.show).toHaveBeenCalledWith('repertoireTrainer.linePaused', jasmine.objectContaining({ action: 'common.undo' }));
    // „Von vorn": die pausierte Linie bleibt draußen
    c.restart();
    expect(c.queue.map(l => c.lineKeyOf(l))).not.toContain(first);
  });

  it('Rückgängig hebt die Pause sofort auf und reiht die Linie wieder ein', () => {
    const { c, setPaused, undo } = setup();
    const first = key(c);
    c.pauseLine();
    undo.next();
    expect(setPaused.calls.mostRecent().args).toEqual([1, [first], false]);
    expect(key(c)).toBe(first);
    expect(c.queue.length).toBe(3);
    c.restart();
    expect(c.queue.map(l => c.lineKeyOf(l))).toContain(first);
  });

  it('Überspringen lässt den Pool unverändert: kein setPaused/promote, Zustand gleich, kommt in der nächsten Runde wieder', () => {
    const { c, setPaused, promote } = setup();
    const first = key(c);
    const before = { ...(c as any).sr.stateOf(first) };
    c.skipLine();
    expect(setPaused).not.toHaveBeenCalled();
    expect(promote).not.toHaveBeenCalled();
    expect((c as any).sr.stateOf(first)).toEqual(before);
    c.restart();
    expect(c.queue.map(l => c.lineKeyOf(l))).toContain(first);
  });

  it('Taste p pausiert (verhindert den Standard); nicht, wenn keine Linie läuft', () => {
    const { c, setPaused } = setup();
    const ev = new KeyboardEvent('keydown', { key: 'p', cancelable: true });
    c.onKeyDown(ev);
    expect(ev.defaultPrevented).toBeTrue();
    expect(setPaused).toHaveBeenCalledTimes(1);
    c.onKeyDown(new KeyboardEvent('keydown', { key: 'P' }));
    c.onKeyDown(new KeyboardEvent('keydown', { key: 'p' }));
    expect(setPaused).toHaveBeenCalledTimes(3);
    expect(c.phase).toBe('DONE');
    const idle = new KeyboardEvent('keydown', { key: 'p', cancelable: true });
    c.onKeyDown(idle);
    expect(idle.defaultPrevented).toBeFalse();
    expect(setPaused).toHaveBeenCalledTimes(3);
  });

  it('?line= auf eine pausierte Linie: wird gezeigt, mit Hinweis „pausiert", ohne Pausieren-Knopf', () => {
    const { c } = setup({ line: KEY_B }, [state(KEY_A, PAST()), state(KEY_B, PAST(), { paused: true })]);
    expect(c.queue.length).toBe(1);
    expect(key(c)).toBe(KEY_B);
    expect(c.currentLinePaused).toBeTrue();
    expect(c.canPause).toBeFalse();
    expect(c.canSkip).toBeTrue();
  });

  describe('Gegner-Modus', () => {
    const answer = (keys: string[]) => ({
      repertoires: [], repertoire: 1, color: 'w', colors: ['w', 'b'], games: 9, total: keys.length, more: 0,
      lines: keys.map(k => ({ key: k, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
    });

    it('pausierte Linien fehlen in der Gegner-Reihenfolge; Pausieren springt zur nächsten in Gegner-Reihenfolge', () => {
      const get = () => of(answer([KEY_C, KEY_A, KEY_B]));
      const { c, setPaused } = setup({ opponent: 'prep:42', color: 'w' },
        [state(KEY_A, PAST(), { paused: true }), state(KEY_B, PAST()), state(KEY_C, PAST())], { get });
      expect(c.queue.map(l => c.lineKeyOf(l))).toEqual([KEY_C, KEY_B]);
      expect(c.opponentTotal).toBe(2);
      c.pauseLine();
      expect(setPaused).toHaveBeenCalledWith(1, [KEY_C], true);
      expect(key(c)).toBe(KEY_B);
      expect(c.opponentTotal).toBe(1);
      c.restart();
      expect(c.queue.map(l => c.lineKeyOf(l))).toEqual([KEY_B]);
    });

    it('eine pausierte Einstiegslinie (?line=) wird gezeigt, mit Hinweis', () => {
      const get = () => of(answer([KEY_A, KEY_B, KEY_C]));
      const { c } = setup({ opponent: 'prep:42', color: 'w', line: KEY_A },
        [state(KEY_A, PAST(), { paused: true }), state(KEY_B, PAST()), state(KEY_C, PAST())], { get });
      expect(c.queue.map(l => c.lineKeyOf(l))).toEqual([KEY_A, KEY_B, KEY_C]);
      expect(c.currentLinePaused).toBeTrue();
    });
  });
});

describe('RepertoireTrainerComponent: Nachbetrachtung nach dem Linienende (0.890.0)', () => {
  function engineStub() {
    const analysis$ = new Subject<any>();
    return {
      analysis$,
      analyze: jasmine.createSpy('analyze').and.returnValue(Promise.resolve()),
      stop: jasmine.createSpy('stop'),
      setMultiPv: jasmine.createSpy('setMultiPv'),
    };
  }

  function setup(engine: any = engineStub()) {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => k === 'chapter' ? 'Chapter A' : null } } };
    const reviewLine = jasmine.createSpy('reviewLine').and.returnValue(of(state(KEY_A, FUTURE())));
    const training: any = {
      getPgn: () => of(PGN + '\n[Event "Rep"]\n[White "1.e4 e5 3"]\n[Black "Chapter A"]\n\n1. e4 e5 2. Bc4 Nc6 *\n'),
      getLineStates: () => of([state(KEY_A, PAST()), state(lineKeyFromSans(['e4', 'e5', 'Bc4', 'Nc6']), PAST())]),
      reviewLine, promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
    };
    const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any,
      { instant: (k: string) => k } as any, { markForCheck: () => {} } as any,
      { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any, { enqueue: () => {} } as any,
      NO_EXPLORER, NO_SNACKBAR, CONFIRM_YES, null);
    (c as any).analysisEngine = engine;
    c.ngOnInit();
    return { c, engine, reviewLine };
  }

  /** Die laufende Linie fehlerfrei zu Ende spielen (beide Linien beginnen mit e4, dann Nf3 bzw. Bc4). */
  function finishLine(c: RepertoireTrainerComponent): void {
    const second = (c as any).session.currentLine.moves[2].san === 'Bc4' ? ['f1', 'c4'] : ['g1', 'f3'];
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any }); tick(600);
    c.onMove({ orig: second[0] as any, dest: second[1] as any }); tick(600);
  }

  const key = (k: string) => new KeyboardEvent('keydown', { key: k, cancelable: true });

  beforeEach(() => localStorage.removeItem('rookhub_rep_train_review_engine'));
  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem('rookhub_rep_train_review_engine');
  });

  it('Pfeiltasten/Pos1/Ende klicken durch die Linie (verhindern den Standard), lösen aber kein „Weiter" aus', fakeAsync(() => {
    const { c, reviewLine } = setup();
    finishLine(c);
    expect(c.phase).toBe('LINE_DONE');
    expect(reviewLine).toHaveBeenCalledTimes(1);
    const line = (c as any).session.currentLine;
    const left = key('ArrowLeft');
    c.onKeyDown(left);
    expect(left.defaultPrevented).toBeTrue();
    expect(c.reviewPly).toBe(3);
    expect(c.fen).toBe(line.fens[3]);
    c.onKeyDown(key('Home'));
    expect(c.reviewPly).toBe(0);
    expect(c.fen).toBe(line.fens[0]);
    c.onKeyDown(key('ArrowRight'));
    expect(c.lastMove).toEqual(['e2' as any, 'e4' as any]);
    c.onKeyDown(key('End'));
    expect(c.reviewPly).toBe(4);
    expect(c.phase).toBe('LINE_DONE');
    expect(c.qIndex).toBe(0);
    expect(reviewLine).toHaveBeenCalledTimes(1);               // SR unverändert
    c.reviewTo(2);                                              // Klick auf einen Zug der Liste
    expect(c.fen).toBe(line.fens[2]);
    c.onKeyDown(key(' '));                                      // Leertaste = nächste Linie
    expect(c.qIndex).toBe(1);
    expect(c.phase).toBe('PLAYING');
    expect(reviewLine).toHaveBeenCalledTimes(1);
  }));

  it('vor dem Linienende tun die Pfeiltasten nichts', () => {
    const { c } = setup();
    const ev = key('ArrowLeft');
    c.onKeyDown(ev);
    expect(ev.defaultPrevented).toBeFalse();
    expect(c.canReview).toBeFalse();
  });

  it('Engine-Schalter: Vorgabe aus; an → rechnet die angezeigte Stellung, folgt beim Durchklicken; Weiter stoppt; gemerkt', fakeAsync(() => {
    const { c, engine } = setup();
    expect(c.reviewEngineOn).toBeFalse();
    finishLine(c);
    expect(engine.analyze).not.toHaveBeenCalled();
    c.toggleReviewEngine();
    expect(localStorage.getItem('rookhub_rep_train_review_engine')).toBe('1');
    expect(engine.analyze).toHaveBeenCalledWith(c.fen);
    engine.analysis$.next({ fen: c.fen, depth: 12, lines: [{ multipv: 1, depth: 12, scoreType: 'cp', score: 35, evalText: '+0.35', pvUci: ['d2d4'] }], running: true, nodes: 0, nps: 0 });
    expect(c.reviewEvalText).toBe('+0.35');
    expect(c.reviewBestLine).toContain('d4');
    expect(c.reviewDepth).toBe(12);
    c.reviewStep(-1);
    expect(engine.analyze.calls.mostRecent().args[0]).toBe(c.fen);
    expect(c.reviewEvalText).toBe('');                          // alte Bewertung gehört zur alten Stellung
    c.continueLine();
    expect(engine.stop).toHaveBeenCalled();
    expect(c.reviewEngineRunning).toBeFalse();
    // nächste fertige Linie: Schalter steht noch → rechnet wieder
    finishLine(c);
    expect(engine.analyze.calls.mostRecent().args[0]).toBe(c.fen);
    c.toggleReviewEngine();
    expect(engine.stop).toHaveBeenCalledTimes(2);
    expect(localStorage.getItem('rookhub_rep_train_review_engine')).toBe('0');
    c.ngOnDestroy();
  }));

  it('gemerkter Schalter startet die Engine am Linienende von selbst; ngOnDestroy stoppt sie', fakeAsync(() => {
    localStorage.setItem('rookhub_rep_train_review_engine', '1');
    const { c, engine } = setup();
    expect(engine.analyze).not.toHaveBeenCalled();             // während des Spielens nie
    finishLine(c);
    expect(engine.analyze).toHaveBeenCalledTimes(1);
    c.ngOnDestroy();
    expect(engine.stop).toHaveBeenCalled();
  }));

  it('offline: kein Engine-Schalter (deaktiviert), Durchklicken geht', fakeAsync(() => {
    const { c, engine } = setup();
    c.offlineSession = true;
    finishLine(c);
    expect(c.reviewEngineAvailable).toBeFalse();
    c.toggleReviewEngine();
    expect(engine.analyze).not.toHaveBeenCalled();
    c.reviewStep(-1);
    expect(c.reviewPly).toBe(3);
  }));

  it('„Im Analysebrett öffnen": Ausgangsstellung, Züge als UCI, Orientierung', fakeAsync(() => {
    const { c } = setup();
    finishLine(c);
    const url = new URL(c.analysisLink!, 'http://x');
    expect(url.pathname).toBe('/analysis');
    expect(url.searchParams.get('moves')).toMatch(/^e2e4 e7e5 (g1f3|f1c4) b8c6$/);
    expect(url.searchParams.get('orientation')).toBe('white');
    expect(url.searchParams.get('fen')).toContain('rnbqkbnr/pppppppp');
  }));
});

describe('RepertoireTrainerComponent: Partien des Gegners zur Stellung (?opponent=, 0.892.0)', () => {
  const fensOf = (sans: string[]) => { const ch = new Chess(); return [ch.fen(), ...sans.map(s => { ch.move(s); return ch.fen(); })]; };
  const F = fensOf(['e4', 'e5', 'Nf3', 'Nc6']);
  const key = (fen: string) => fen.split(' ').slice(0, 3).join(' ');

  function make(band: string | null) {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));
    const query: Record<string, string> = { opponent: 'prep:42', color: 'w', name: 'Maras' };
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
    const training: any = {
      getPgn: () => of(PGN), getLineStates: () => of([state(KEY_A, PAST()), state(KEY_B, PAST())]),
      reviewLine: () => of(state(KEY_A, FUTURE())), promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
    };
    const get = jasmine.createSpy('get').and.returnValue(of({
      repertoires: [], repertoire: 1, color: 'w', colors: ['w'], games: 14, total: 2, more: 0, lichessBand: band,
      lines: [KEY_A, KEY_B].map(k => ({ key: k, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
      // er erreicht die Stellung nach 1.e4 (14×, spielt 1…e5 9× / 1…c5 5×) und nach 1…e5, aber nie die nach 2.Nf3
      positions: { [key(F[0])]: { n: 14, y: 2024, m: {} }, [key(F[1])]: { n: 14, y: 2024, m: { e5: 9, c5: 5 } }, [key(F[2])]: { n: 9, y: 2023, m: {} } },
    }));
    const translate: any = { instant: (k: string, p?: any) => `${k}|${JSON.stringify(p ?? {})}`, getCurrentLang: () => 'de' };
    const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any, translate,
      { markForCheck: () => {} } as any, { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any, NO_EXPLORER, NO_SNACKBAR, CONFIRM_YES, { get } as any);
    c.ngOnInit();
    return { c, get };
  }
  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  it('fragt die Partien je Stellung EINMAL mit (positions=true) und zeigt sie zur Stellung; Umschlag am ersten Zug ohne Partien', fakeAsync(() => {
    const { c, get } = make(null);
    expect(get).toHaveBeenCalledTimes(1);
    expect(get.calls.mostRecent().args[1].params.get('positions')).toBe('true');
    // Linie 1.e4 e5 2.Nf3 Nc6 zuerst (Reihung A, B)
    expect(c.queue[0].headers['White']).toBe('1.e4 e5');
    expect(c.opponentNote!.text).toContain('opponentNote.reached');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    tick(600);                                                    // er antwortet 1…e5 — Stellung nach 1…e5 noch in seinen Partien
    expect(c.opponentNote!.left).toBeFalse();
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    tick(600);                                                    // nach 2.Nf3 keine seiner Partien mehr
    expect(c.phase).toBe('LINE_DONE');
    expect(c.opponentNote).toEqual({ text: 'opponentNote.leftRepertoire|{"move":"2.Sf3","who":"Maras","band":null}', moves: null, left: true });
    c.reviewTo(1);                                                // zurückgeklickt: nach 1.e4 seine Züge
    expect(c.opponentNote!.moves).toBe('opponentNote.played|{"moves":"1…e5 9×, 1…c5 5×"}');
  }));

  it('mit Lichess-Schätzung: „ab hier geschätzt (Lichess …)"', fakeAsync(() => {
    const { c } = make('2143–2443');
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any }); tick(600);
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any }); tick(600);
    expect(c.opponentNote!.text).toBe('opponentNote.leftEstimated|{"move":"2.Sf3","who":"Maras","band":"2143–2443"}');
  }));

  it('ohne Gegner keine Anzeige', () => {
    const c = make(null).c;
    (c as any).opponentPositions = null;
    expect(c.opponentNote).toBeNull();
  });
});

describe('RepertoireTrainerComponent: gespeicherter Gegner eines „Prep: …"-Repertoires (ohne ?opponent=, 0.894.0)', () => {
  const fensOf = (sans: string[]) => { const ch = new Chess(); return [ch.fen(), ...sans.map(s => { ch.move(s); return ch.fen(); })]; };
  const F = fensOf(['e4', 'e5']);
  const key = (fen: string) => fen.split(' ').slice(0, 3).join(' ');
  const STORED = { opponent: 'league:1608169', name: 'Moritz', color: 'w' as const, params: { source: 'both', years: '3', evil: 'x' } };

  function make(query: Record<string, string>, opponent: typeof STORED | null) {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', 'Chapter B': 'w' }));
    const route: any = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
    const training: any = {
      getPgn: () => of(PGN), getLineStates: () => of([state(KEY_A, PAST()), state(KEY_B, PAST())]),
      getRepertoireMeta: () => of({ name: 'Prep: Moritz, Christian Mag. 2026', opponent }),
      reviewLine: () => of(state(KEY_A, FUTURE())), promote: () => of({ affected: 1 }), makeDue: () => of({ affected: 1 }), reset: () => of({ deleted: 0 }),
    };
    const get = jasmine.createSpy('get').and.returnValue(of({
      repertoires: [], repertoire: 1, color: 'w', colors: ['w'], games: 14, total: 2, more: 0, lichessBand: null,
      lines: [KEY_B, KEY_A].map(k => ({ key: k, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
      positions: { [key(F[0])]: { n: 14, y: 2024, m: {} }, [key(F[1])]: { n: 14, y: 2024, m: { e5: 9 } } },
    }));
    const translate: any = { instant: (k: string, p?: any) => `${k}|${JSON.stringify(p ?? {})}`, getCurrentLang: () => 'de' };
    const c = new RepertoireTrainerComponent(route, training, { boardTheme: 'brown', pieceSet: 'cburnett' } as any, translate,
      { markForCheck: () => {} } as any, { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as any, {} as any,
      { enqueue: () => {} } as any, NO_EXPLORER, NO_SNACKBAR, CONFIRM_YES, { get } as any);
    c.ngOnInit();
    return { c, get };
  }
  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  it('lädt die Trainingslinien des gespeicherten Gegners (positions, Filter, meine Farbe) und zeigt die Gegner-Zeile', () => {
    const { c, get } = make({}, STORED);
    expect(get).toHaveBeenCalledTimes(1);
    const [url, opts] = get.calls.mostRecent().args;
    expect(url).toBe('/api/league/player/1608169/training-lines');
    expect(opts.params.get('positions')).toBe('true');
    expect(opts.params.get('color')).toBe('w');
    expect(opts.params.get('source')).toBe('both');
    expect(opts.params.get('years')).toBe('3');
    expect(opts.params.get('evil')).toBeNull();
    expect(c.opponent).toBe('league:1608169');
    expect(c.opponentNotice).toBe('repertoireTrainer.opponentOrder');
    expect(c.queue.map(l => l.headers['White'])[0]).toBe('1.d4 d5');           // Reihenfolge des Gegners (B vor A)
    expect(c.repertoireName).toBe('Prep: Moritz, Christian Mag. 2026');
  });

  it('Gegner-Zeile nennt den gespeicherten Namen', () => {
    const { c } = make({ line: KEY_A }, STORED);                                // Einstieg 1.e4 e5 …
    expect(c.opponentNote!.text).toContain('opponentNote.reached');
    expect(c.opponentNote!.text).toContain('Moritz');
  });

  it('?opponent= in der Adresse geht vor; ohne gespeicherten Gegner bleibt alles wie bisher', () => {
    const viaUrl = make({ opponent: 'prep:42', color: 'w', name: 'Maras' }, STORED);
    expect(viaUrl.get.calls.mostRecent().args[0]).toBe('/api/prep/player/42/training-lines');
    expect(viaUrl.get.calls.mostRecent().args[1].params.get('years')).toBeNull();
    const none = make({}, null);
    expect(none.get).not.toHaveBeenCalled();
    expect(none.c.opponent).toBeNull();
  });
});
