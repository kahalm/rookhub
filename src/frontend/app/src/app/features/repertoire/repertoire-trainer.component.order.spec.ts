import { EMPTY, Observable, Subject, of, throwError } from 'rxjs';
import { Chess } from 'chess.js';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto, SrConfigDto, SrLevel } from './repertoire-training.service';
import { DEFAULT_SR_LEVELS } from './repertoire-sr.util';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { DEFAULT_EXPLORER_SETTINGS, ExplorerAnalysisResult } from './repertoire-explorer.service';
import { TrainOrderDialogComponent } from './train-order-dialog.component';

/**
 * Reihenfolge der Linien im Trainer (0.996.0): je Nutzer und Repertoire gewählt, Vorgabe „Wenig trainierte zuerst“.
 * Die Regel selbst prüft `repertoire-trainer-queue.util.spec.ts` (`orderLines`); hier: dass die Warteschlange des
 * Trainers ihr folgt — beim Start, bei jedem Neuaufbau, offline — und Gegner-Sitzung/Einzellinie unberührt bleiben.
 * Der Zufall ist festgenagelt (`fixShuffle`), nichts hängt an echtem Mischen.
 */

const SANS: Record<string, string[]> = {
  A: ['e4', 'e5', 'Nf3', 'Nc6'],
  B: ['d4', 'd5', 'c4', 'e6'],
  C: ['c4', 'e5', 'Nc3', 'Nf6'],
  D: ['Nf3', 'd5', 'g3', 'Nf6'],
};
const KEY = (n: string) => lineKeyFromSans(SANS[n]);
/** Vier Weiß-Linien in Repertoire-Reihenfolge A, B, C, D. */
const PGN = [
  '[Event "Rep"]', '[White "A"]', '[Black "Chapter A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
  '[Event "Rep"]', '[White "B"]', '[Black "Chapter A"]', '', '1. d4 d5 2. c4 e6 *', '',
  '[Event "Rep"]', '[White "C"]', '[Black "Chapter A"]', '', '1. c4 e5 2. Nc3 Nf6 *', '',
  '[Event "Rep"]', '[White "D"]', '[Black "Chapter A"]', '', '1. Nf3 d5 2. g3 Nf6 *', '',
].join('\n');

const PAST = () => Date.now() - 3_600_000;
const FUTURE = () => Date.now() + 3_600_000;

function due(name: string, level: number, extra: Partial<LineStateDto> = {}): LineStateDto {
  return {
    lineKey: KEY(name), level, reps: level, lapses: 0, dueAt: new Date(PAST()).toISOString(), lastReviewedAt: null,
    inPool: true, paused: false, ...extra,
  };
}

/** A zweimal, C einmal, B und D noch nie richtig — alle fällig. */
const MIXED = (): LineStateDto[] => [due('A', 2), due('B', 0), due('C', 1), due('D', 0)];

function config(lineOrder: string | null, effective: SrLevel[] = DEFAULT_SR_LEVELS): SrConfigDto {
  return { effective, user: effective, repertoire: null, source: 'default', lineOrder };
}

/**
 * Fisher–Yates festnageln: aus A B C D wird A D C B (i=3 tauscht mit 1, danach kein Tausch) — die gemischte
 * Reihenfolge weicht also von der des Repertoires ab, und zwar genau bei den beiden Stufe-0-Linien B und D.
 * Das gilt für das ERSTE Mischen; weitere Aufrufe liefern „kein Tausch“ (A B C D), bis {@link rearmShuffle}.
 */
let shuffleValues: number[] = [];
function rearmShuffle(): void { shuffleValues = [0.3, 0.9, 0.9]; }
function fixShuffle(): jasmine.Spy {
  rearmShuffle();
  return spyOn(Math, 'random').and.callFake(() => shuffleValues.shift() ?? 0.999);
}

interface Made {
  c: RepertoireTrainerComponent;
  training: Record<string, jasmine.Spy>;
  dialogOpen: jasmine.Spy;
  markForCheck: jasmine.Spy;
  /** Was der nächste Dialog zurückgibt (Reihenfolge-Dialog: gewählte Reihenfolge bzw. `undefined` = abgebrochen). */
  dialogResult: { value: unknown };
}

function make(opts: {
  states?: LineStateDto[];
  query?: Record<string, string>;
  /** `undefined` = Test-Double ohne `getConfig`; sonst die Antwort bzw. ein Observable (Fehler, verzögert). */
  config?: SrConfigDto | Observable<SrConfigDto>;
  explorer?: unknown;
  http?: unknown;
  offline?: boolean;
} = {}): Made {
  localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
  const query = opts.query ?? {};
  const route = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
  const states = opts.states ?? MIXED();
  const training: Record<string, jasmine.Spy> = {
    getPgn: jasmine.createSpy('getPgn').and.callFake(() => opts.offline ? throwError(() => new Error('offline')) : of(PGN)),
    getLineStates: jasmine.createSpy('getLineStates').and.callFake(() => opts.offline ? throwError(() => new Error('offline')) : of(states)),
    reviewLine: jasmine.createSpy('reviewLine').and.returnValue(of(due('A', 3, { dueAt: new Date(FUTURE()).toISOString() }))),
    promote: jasmine.createSpy('promote').and.returnValue(of({ affected: 1 })),
    makeDue: jasmine.createSpy('makeDue').and.returnValue(of({ affected: 1 })),
    reset: jasmine.createSpy('reset').and.returnValue(of({ deleted: 0 })),
  };
  if (opts.config !== undefined) {
    const answer = opts.config;
    training['getConfig'] = jasmine.createSpy('getConfig').and.callFake(() => answer instanceof Observable ? answer : of(answer));
  }
  const dialogResult = { value: undefined as unknown };
  const dialogOpen = jasmine.createSpy('open').and.callFake(() => ({ afterClosed: () => of(dialogResult.value) }));
  const markForCheck = jasmine.createSpy('markForCheck');
  const explorer = opts.explorer ?? { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) };
  const c = new RepertoireTrainerComponent(
    route as never, training as never, { boardTheme: 'brown', pieceSet: 'cburnett' } as never,
    { instant: (k: string) => k } as never, { markForCheck } as never,
    { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as never,
    { open: dialogOpen } as never, { enqueue: jasmine.createSpy('enqueue') } as never,
    explorer as never, { warn: () => ({}) } as never, { ask: () => of(true) } as never,
    (opts.http ?? null) as never,
  );
  c.ngOnInit();
  return { c, training, dialogOpen, markForCheck, dialogResult };
}

const names = (c: RepertoireTrainerComponent) => c.queue.map(l => l.headers['White']).join('');

describe('RepertoireTrainerComponent — Reihenfolge der Linien (0.996.0)', () => {
  const OFFLINE_KEY = REPERTOIRE_OFFLINE_PREFIX + '1';

  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem('rookhub_rep_train_freq_order');
    localStorage.removeItem(OFFLINE_KEY);
  });

  describe('Vorgabe „Wenig trainierte zuerst“ (nichts gewählt)', () => {
    it('Abfragen beginnt mit den nie richtig gelösten Linien, dann 1×, dann 2× — Gleichstand in gemischter Reihenfolge', () => {
      fixShuffle();
      const { c } = make({ config: config(null) });
      // gemischt: A D C B → Stufe 0: D B · Stufe 1: C · Stufe 2: A
      expect(names(c)).toBe('DBCA');
      expect(c.lineOrder).toBe('leastTrained');
      expect(c.phase).toBe('PLAYING');
      expect(c.queue[c.qIndex].headers['White']).toBe('D');
    });

    it('gilt auch, wenn der Server (noch) keine Einstellung liefert (Test-Double ohne getConfig, Abruf gescheitert)', () => {
      fixShuffle();
      expect(names(make().c)).toBe('DBCA');
      rearmShuffle();
      expect(names(make({ config: throwError(() => new Error('500')) }).c)).toBe('DBCA');
    });

    it('nur FÄLLIGE Pool-Linien: geplante, pausierte und ungelernte kommen nicht — die Reihenfolge ändert daran nichts', () => {
      fixShuffle();
      const { c } = make({ states: [
        due('A', 2), due('B', 0, { dueAt: new Date(FUTURE()).toISOString() }), due('C', 1, { paused: true }),
      ] });
      expect(names(c)).toBe('A');
    });

    it('eigene Intervalle: die Zeit bis zur Wiederkehr entscheidet, nicht die Stufennummer', () => {
      // Stufe 1 = 2 Tage, Stufe 2 = 6 Stunden: C (Stufe 1 → 6 h) kommt VOR B und D (Stufe 0 → 2 Tage) und A (Stufe 2 → 1 Woche)
      const own: SrLevel[] = [{ value: 2, unit: 'd' }, { value: 6, unit: 'h' }, { value: 1, unit: 'w' }, ...DEFAULT_SR_LEVELS.slice(3)];
      fixShuffle();
      expect(names(make({ config: config(null, own) }).c)).toBe('CDBA');
    });
  });

  describe('die gewählte Reihenfolge vom Server', () => {
    it('„Viel trainierte zuerst“: 2× vor 1× vor nie, Gleichstand gemischt', () => {
      fixShuffle();
      const { c } = make({ config: config('mostTrained') });
      expect(names(c)).toBe('ACDB');
      expect(c.lineOrder).toBe('mostTrained');
    });

    it('„Zufällig“: die gemischte Reihenfolge, ohne Blick auf die Stufe', () => {
      fixShuffle();
      expect(names(make({ config: config('random') }).c)).toBe('ADCB');
    });

    it('„Reihenfolge im Repertoire“: wie im PGN — es wird nicht gemischt', () => {
      const random = fixShuffle();
      expect(names(make({ config: config('repertoire') }).c)).toBe('ABCD');
      expect(random).not.toHaveBeenCalled();
    });

    it('ein Wert, den diese Fassung nicht kennt, gilt als nichts gewählt', () => {
      fixShuffle();
      const { c } = make({ config: config('alphabetical') });
      expect(c.lineOrder).toBe('leastTrained');
      expect(names(c)).toBe('DBCA');
    });
  });

  describe('Lernen', () => {
    it('ungelernte Linien in Repertoire-Reihenfolge — wie bisher, nichts wird gemischt', () => {
      const random = fixShuffle();
      const { c } = make({ query: { mode: 'learn' }, states: [], config: config(null) });
      expect(c.mode).toBe('learn');
      expect(names(c)).toBe('ABCD');
      expect(random).not.toHaveBeenCalled();
    });

    it('„Viel trainierte zuerst“ ergibt dort mangels Trainingsstand ebenfalls die Repertoire-Reihenfolge; „Zufällig“ mischt', () => {
      fixShuffle();
      expect(names(make({ query: { mode: 'learn' }, states: [], config: config('mostTrained') }).c)).toBe('ABCD');
      expect(names(make({ query: { mode: 'learn' }, states: [], config: config('random') }).c)).toBe('ADCB');
    });

    it('Wechsel Abfragen → Lernen → Abfragen baut jedes Mal nach der Regel des Modus', () => {
      fixShuffle();
      // B und D ungelernt (kein Zustand), A und C im Pool
      const { c } = make({ states: [due('A', 2), due('C', 0)], config: config(null) });
      expect(names(c)).toBe('CA');
      c.setMode('learn');
      expect(names(c)).toBe('BD');
      c.setMode('quiz');
      expect(names(c)).toBe('CA');
    });
  });

  describe('unberührt', () => {
    const answer = (keys: string[]) => ({
      repertoires: [], repertoire: 1, color: 'w', colors: ['w', 'b'], games: 9, total: keys.length, more: 0,
      lines: keys.map(key => ({ key, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
    });

    it('Gegner-Sitzung: die Reihenfolge des Gegners, ohne Fälligkeit, ohne Mischen — die Einstellung greift nicht', () => {
      const random = fixShuffle();
      const get = jasmine.createSpy('get').and.returnValue(of(answer([KEY('C'), KEY('A'), KEY('D'), KEY('B')])));
      // B ist nicht fällig und D ungelernt — in der Gegner-Sitzung kommen sie trotzdem
      const { c } = make({
        query: { opponent: 'prep:42', color: 'w' }, http: { get }, config: config('mostTrained'),
        states: [due('A', 2), due('B', 0, { dueAt: new Date(FUTURE()).toISOString() }), due('C', 1)],
      });
      expect(names(c)).toBe('CADB');
      expect(c.opponentNotice).toBe('repertoireTrainer.opponentOrder');
      expect(random).not.toHaveBeenCalled();
    });

    it('ausdrücklich gewählte Einzellinie (?line=): genau diese', () => {
      fixShuffle();
      const { c } = make({ query: { line: KEY('C') }, config: config('mostTrained') });
      expect(names(c)).toBe('C');
    });
  });

  describe('alter Geräte-Schalter „Häufigste zuerst“ (bis 0.995.0)', () => {
    function endKey(sans: string[]): string {
      const chess = new Chess();
      for (const san of sans) chess.move(san);
      return chess.fen().split(' ').slice(0, 3).join(' ');
    }
    const frequencies = (): ExplorerAnalysisResult => ({
      complete: true, positionsAnalyzed: 4, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
      tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [],
      lineFrequencies: { [endKey(SANS['A'])]: 0.1, [endKey(SANS['B'])]: 0.2, [endKey(SANS['C'])]: 0.4, [endKey(SANS['D'])]: 0.3 },
    });
    const explorerWith = (run: jasmine.Spy) => ({ run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) });

    it('an und für das Repertoire nichts gewählt: „Häufigste zuerst“ gilt weiter', () => {
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      const run = jasmine.createSpy('run').and.returnValue(of(frequencies()));
      const { c } = make({ config: config(null), explorer: explorerWith(run) });
      expect(c.lineOrder).toBe('frequency');
      expect(run).toHaveBeenCalledTimes(1);
      expect(names(c)).toBe('CDBA');
      expect(c.currentLineFrequency).toBeCloseTo(0.4, 6);
    });

    it('an, aber für das Repertoire ist etwas gewählt: die Wahl gilt, kein Explorer-Lauf', () => {
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      fixShuffle();
      const run = jasmine.createSpy('run').and.returnValue(of(frequencies()));
      const { c } = make({ config: config('leastTrained'), explorer: explorerWith(run) });
      expect(c.lineOrder).toBe('leastTrained');
      expect(run).not.toHaveBeenCalled();
      expect(names(c)).toBe('DBCA');
      expect(c.currentLineFrequency).toBeNull();
    });

    it('„Häufigste zuerst“ als gespeicherte Wahl braucht den Schalter nicht', () => {
      const run = jasmine.createSpy('run').and.returnValue(of(frequencies()));
      const { c } = make({ config: config('frequency'), explorer: explorerWith(run) });
      expect(run).toHaveBeenCalledTimes(1);
      expect(names(c)).toBe('CDBA');
    });

    it('ohne Häufigkeiten (Abruf gescheitert) gilt die Vorgabe — mit Hinweis', () => {
      fixShuffle();
      const run = jasmine.createSpy('run').and.returnValue(throwError(() => new Error('x')));
      const { c } = make({ config: config('frequency'), explorer: explorerWith(run) });
      expect(c.phase).toBe('PLAYING');
      expect(c.freqNotice).toBe('repertoireTrainer.freqFailed');
      expect(names(c)).toBe('DBCA');
    });

    it('der Schalter wird nicht mehr geschrieben', () => {
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      const { c, dialogResult } = make({ config: config(null), explorer: explorerWith(jasmine.createSpy('run').and.returnValue(of(frequencies()))) });
      dialogResult.value = 'repertoire';
      c.openOrder();
      expect(c.lineOrder).toBe('repertoire');
      expect(localStorage.getItem('rookhub_rep_train_freq_order')).toBe('1');
    });
  });

  describe('Umstellen im Trainer (Knopf „Reihenfolge“ → geteilter Dialog)', () => {
    it('öffnet den Dialog mit der geltenden Reihenfolge; die Wahl baut die Sitzung in der neuen Reihenfolge neu auf', () => {
      fixShuffle();
      const { c, dialogOpen, dialogResult, markForCheck } = make({ config: config(null) });
      expect(names(c)).toBe('DBCA');
      c.session.qIndex = 2; c.session.correct = 5;      // mitten in der Sitzung
      markForCheck.calls.reset();

      dialogResult.value = 'repertoire';
      c.openOrder();

      const [component, cfg] = dialogOpen.calls.mostRecent().args;
      expect(component).toBe(TrainOrderDialogComponent);
      // ohne Sitzung gegen einen gespeicherten Gegner steht „Nach dem Gegner“ nicht zur Wahl (0.999.3)
      expect(cfg.data).toEqual({ repertoireId: 1, repertoireName: null, current: 'leastTrained', hasOpponent: false });
      // Auswahl-Dialog nach DESIGN 5.6: schmal, nie breiter als das Fenster, Fokus auf dem Dialog statt der ersten Option
      expect(cfg).toEqual(jasmine.objectContaining({ width: '440px', maxWidth: '94vw', autoFocus: 'dialog' }));
      expect(c.lineOrder).toBe('repertoire');
      expect(names(c)).toBe('ABCD');
      expect(c.qIndex).toBe(0);                          // von vorn, wie ein Moduswechsel
      expect(c.correct).toBe(0);
      expect(c.phase).toBe('PLAYING');
      expect(markForCheck).toHaveBeenCalled();           // OnPush: die Dialog-Antwort zeichnet die Ansicht nicht von selbst
    });

    it('abgebrochen oder dieselbe Reihenfolge: die laufende Sitzung bleibt, wie sie ist', () => {
      fixShuffle();
      const { c, dialogResult } = make({ config: config(null) });
      const queue = c.queue;
      c.session.qIndex = 1;

      dialogResult.value = undefined;
      c.openOrder();
      dialogResult.value = 'leastTrained';
      c.openOrder();

      expect(c.queue).toBe(queue);
      expect(c.qIndex).toBe(1);
    });

    it('auf „Häufigste zuerst“: erst die Häufigkeiten holen (Ladeansicht), dann aufbauen', () => {
      const runs = new Subject<ExplorerAnalysisResult>();
      const run = jasmine.createSpy('run').and.returnValue(runs);
      const { c, dialogResult } = make({ config: config('repertoire'), explorer: { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } });
      expect(run).not.toHaveBeenCalled();

      dialogResult.value = 'frequency';
      c.openOrder();

      expect(run).toHaveBeenCalledTimes(1);
      expect(c.phase).toBe('LOADING');
      expect(c.freqRunning).toBeTrue();
    });

    it('während die Häufigkeiten laden auf eine andere Reihenfolge: Abruf abbrechen — kein zweiter Neuaufbau hinterher', () => {
      fixShuffle();
      const runs = new Subject<ExplorerAnalysisResult>();
      const run = jasmine.createSpy('run').and.returnValue(runs);
      const { c, dialogResult } = make({ config: config('frequency'), explorer: { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } });
      expect(c.phase).toBe('LOADING');

      dialogResult.value = 'mostTrained';
      c.openOrder();

      expect(runs.observed).toBeFalse();
      expect(c.freqRunning).toBeFalse();
      expect(c.phase).toBe('PLAYING');
      expect(names(c)).toBe('ACDB');
      const queue = c.queue;
      c.session.qIndex = 1;
      runs.complete();                                   // der abgebrochene Lauf meldet sich nicht mehr
      expect(c.queue).toBe(queue);
      expect(c.qIndex).toBe(1);
    });

    it('solange PGN, Zustände und Einstellung noch laden: kein Dialog — die gespeicherte Wahl ist noch nicht bekannt', () => {
      const pending = new Subject<SrConfigDto>();
      const { c, dialogOpen } = make({ config: pending });
      expect(c.sessionLoaded).toBeFalse();
      c.openOrder();
      expect(dialogOpen).not.toHaveBeenCalled();

      pending.next(config('repertoire')); pending.complete();
      expect(c.sessionLoaded).toBeTrue();
      expect(c.lineOrder).toBe('repertoire');
      c.openOrder();
      expect(dialogOpen.calls.mostRecent().args[1].data.current).toBe('repertoire');
    });

    it('offline gibt es keine Auswahl (die Wahl liegt am Server)', () => {
      localStorage.setItem(OFFLINE_KEY, JSON.stringify({ meta: { id: 1, name: 'Rep' }, pgn: PGN, states: MIXED(), config: null, savedAt: '' }));
      const { c, dialogOpen } = make({ offline: true });
      expect(c.offlineSession).toBeTrue();
      c.openOrder();
      expect(dialogOpen).not.toHaveBeenCalled();
    });
  });

  describe('jeder Neuaufbau der Warteschlange folgt derselben Regel', () => {
    it('„Erneut trainieren“', () => {
      fixShuffle();
      const { c } = make({ config: config('mostTrained') });
      c.session.qIndex = 3;
      c.restart();
      // zweites Mischen ohne Tausch (A B C D) → Stufe 2: A · Stufe 1: C · Stufe 0: B D
      expect(names(c)).toBe('ACBD');
      expect(c.qIndex).toBe(0);
    });

    it('„Alle jetzt fällig machen“: die neu geladenen Zustände kommen in der gewählten Reihenfolge', () => {
      fixShuffle();
      const { c, training } = make({ config: config(null), states: [due('A', 2, { dueAt: new Date(FUTURE()).toISOString() })] });
      expect(c.phase).toBe('EMPTY');
      training['getLineStates'].and.returnValue(of([due('A', 2), due('B', 4), due('C', 1), due('D', 3)]));

      c.makeAllDue();

      expect(training['makeDue']).toHaveBeenCalled();
      expect(names(c)).toBe('CADB');                     // Stufe 1, 2, 3, 4
    });

    it('nach „Übungszyklen“ gespeichert: die neuen Intervalle gelten ab dem nächsten Aufbau, die laufende Sitzung bleibt', () => {
      fixShuffle();
      const own: SrLevel[] = [{ value: 2, unit: 'd' }, { value: 6, unit: 'h' }, { value: 1, unit: 'w' }, ...DEFAULT_SR_LEVELS.slice(3)];
      const { c, training, dialogResult } = make({ config: config(null) });
      expect(names(c)).toBe('DBCA');
      const queue = c.queue;

      training['getConfig'].and.returnValue(of(config(null, own)));
      dialogResult.value = true;                         // Intervall-Dialog: gespeichert
      c.openConfig();

      expect(training['getConfig']).toHaveBeenCalledTimes(2);
      expect(c.queue).toBe(queue);                       // nicht mitten in der Sitzung umbauen
      c.restart();
      expect(names(c)).toBe('CBDA');                     // C (Stufe 1 → 6 h) jetzt vor B, D (→ 2 Tage) und A (→ 1 Woche)
    });
  });

  describe('Offline-Kopie', () => {
    const copy = (extra: Record<string, unknown>) =>
      JSON.stringify({ meta: { id: 1, name: 'Rep' }, pgn: PGN, states: MIXED(), config: null, savedAt: '2026-07-18T00:00:00Z', ...extra });

    it('offline gilt die Reihenfolge aus der Kopie', () => {
      localStorage.setItem(OFFLINE_KEY, copy({ lineOrder: 'mostTrained' }));
      fixShuffle();
      const { c } = make({ offline: true });
      expect(c.offlineSession).toBeTrue();
      expect(c.lineOrder).toBe('mostTrained');
      expect(names(c)).toBe('ACDB');
    });

    it('Kopie ohne Reihenfolge (vor 0.996.0) oder mit unbekanntem Wert: die Vorgabe', () => {
      fixShuffle();
      localStorage.setItem(OFFLINE_KEY, copy({}));
      expect(names(make({ offline: true }).c)).toBe('DBCA');
      localStorage.setItem(OFFLINE_KEY, copy({ lineOrder: 'alphabetical' }));
      expect(make({ offline: true }).c.lineOrder).toBe('leastTrained');
    });

    it('„Häufigste zuerst“ offline: keine Häufigkeiten, kein Explorer-Lauf — es gilt die Vorgabe', () => {
      localStorage.setItem(OFFLINE_KEY, copy({ lineOrder: 'frequency' }));
      fixShuffle();
      const run = jasmine.createSpy('run').and.returnValue(EMPTY);
      const { c } = make({ offline: true, explorer: { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } });
      expect(run).not.toHaveBeenCalled();
      expect(c.phase).toBe('PLAYING');
      expect(names(c)).toBe('DBCA');
    });

    it('offline zählen die Intervalle der Kopie für die Wiederkehr', () => {
      const own: SrLevel[] = [{ value: 2, unit: 'd' }, { value: 6, unit: 'h' }, { value: 1, unit: 'w' }, ...DEFAULT_SR_LEVELS.slice(3)];
      localStorage.setItem(OFFLINE_KEY, copy({ config: own }));
      fixShuffle();
      expect(names(make({ offline: true }).c)).toBe('CDBA');
    });

    it('online geöffnet: Reihenfolge und Intervalle des Servers wandern in eine vorhandene Kopie', () => {
      const own: SrLevel[] = [{ value: 2, unit: 'd' }, ...DEFAULT_SR_LEVELS.slice(1)];
      localStorage.setItem(OFFLINE_KEY, copy({ lineOrder: 'random' }));
      make({ config: config('repertoire', own) });
      const saved = JSON.parse(localStorage.getItem(OFFLINE_KEY)!);
      expect(saved.lineOrder).toBe('repertoire');
      expect(saved.config).toEqual(own);
      expect(saved.pgn).toBe(PGN);
    });

    it('online ohne Kopie wird keine angelegt', () => {
      make({ config: config('repertoire') });
      expect(localStorage.getItem(OFFLINE_KEY)).toBeNull();
    });

    it('Einstellungen nicht abrufbar, aber eine Kopie da: deren Reihenfolge gilt', () => {
      localStorage.setItem(OFFLINE_KEY, copy({ lineOrder: 'repertoire' }));
      const { c } = make({ config: throwError(() => new Error('500')) });
      expect(c.offlineSession).toBeFalse();
      expect(c.lineOrder).toBe('repertoire');
      expect(names(c)).toBe('ABCD');
    });
  });
});
