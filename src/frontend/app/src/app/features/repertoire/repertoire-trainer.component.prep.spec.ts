import { EMPTY, Observable, Subject, of, throwError } from 'rxjs';
import { Chess } from 'chess.js';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { LineStateDto, SrConfigDto } from './repertoire-training.service';
import { DEFAULT_SR_LEVELS } from './repertoire-sr.util';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { DEFAULT_EXPLORER_SETTINGS, ExplorerAnalysisResult } from './repertoire-explorer.service';

/**
 * Trainer eines „Prep: …"-Repertoires mit GESPEICHERTEM Gegner (0.999.3). Wunsch: „wenn ich bei …/repertoires/308/train
 * auf review gehe bringt er immer alle lines. sollte hier nur die fälligen bringen. einzige ausnahme: nicht trainierte
 * sind auch fällig." Bis 0.999.2 nahm diese Sitzung wie die ausdrückliche Gegner-Sitzung (`?opponent=` von der
 * Spielerkarte) ALLE Linien in der Reihenfolge des Gegners; die Reihenfolge-Einstellung wirkte dort nicht.
 *
 * Zahlenbild wie beim gemeldeten Repertoire (dort 50 Linien: 2 fällig, 37 später fällig, 11 untrainiert), verkleinert
 * auf acht: 2 fällig (A, D), 3 untrainiert (B, E, H), 3 später fällig (C, F, G).
 */

const SANS: Record<string, string[]> = {
  A: ['e4', 'e5', 'Nf3', 'Nc6'], B: ['e4', 'e5', 'Nf3', 'd6'], C: ['e4', 'c5', 'Nf3', 'd6'], D: ['e4', 'c5', 'Nf3', 'Nc6'],
  E: ['e4', 'e6', 'd4', 'd5'], F: ['e4', 'c6', 'd4', 'd5'], G: ['e4', 'd5', 'exd5', 'Qxd5'], H: ['e4', 'Nf6', 'e5', 'Nd5'],
};
const KEY = (n: string) => lineKeyFromSans(SANS[n]);
const movetext = (s: string[]) => s.map((san, i) => (i % 2 === 0 ? `${i / 2 + 1}. ` : '') + san).join(' ') + ' *';
/** Acht Weiß-Linien in Repertoire-Reihenfolge A…H, dazu eine Schwarz-Linie Z (nicht die Farbe gegen diesen Gegner). */
const PGN = [
  ...Object.entries(SANS).map(([n, s]) => `[Event "Rep"]\n[White "${n}"]\n[Black "Weiß"]\n\n${movetext(s)}\n`),
  `[Event "Rep"]\n[White "Z"]\n[Black "Schwarz"]\n\n1. d4 d5 2. c4 e6 *\n`,
].join('\n');

const PAST = () => new Date(Date.now() - 3_600_000).toISOString();
const SOON = () => new Date(Date.now() + 2 * 3_600_000).toISOString();
const LATER = () => new Date(Date.now() + 48 * 3_600_000).toISOString();
const st = (name: string, level: number, dueAt: string, extra: Partial<LineStateDto> = {}): LineStateDto =>
  ({ lineKey: KEY(name), level, reps: level, lapses: 0, dueAt, lastReviewedAt: level ? PAST() : null, inPool: true, paused: false, ...extra });

/** A (Stufe 2) und D (Stufe 1) fällig · C, G (Stufe 1) und F (Stufe 3) später fällig · B, E ohne Zustand, H nicht im Pool. */
const STATES = (): LineStateDto[] => [
  st('A', 2, PAST()), st('D', 1, PAST()),
  st('C', 1, SOON()), st('G', 1, LATER()), st('F', 3, LATER()),
  st('H', 0, PAST(), { inPool: false }),
];
/** Der Gegner spielt am ehesten G, dann C, A, E, B, H; D und F kennt er nicht (kommen dahinter, in Repertoire-Reihenfolge). */
const RANKED = ['G', 'C', 'A', 'E', 'B', 'H'];
const lines = (order: string[] = RANKED) => ({
  repertoires: [], repertoire: 1, color: 'w', colors: ['w', 'b'], games: 9, total: order.length, more: 0, positions: null, lichessBand: null,
  lines: order.map(n => ({ key: KEY(n), end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
});
const STORED = { opponent: 'prep:154602', name: 'Schett, Franz', color: 'w' as const, params: {} };

function config(lineOrder: string | null, hasOpponent = true): SrConfigDto {
  return { effective: DEFAULT_SR_LEVELS, user: DEFAULT_SR_LEVELS, repertoire: null, source: 'default', lineOrder, hasOpponent };
}

interface Made {
  c: RepertoireTrainerComponent;
  get: jasmine.Spy;
  training: Record<string, jasmine.Spy>;
  dialogOpen: jasmine.Spy;
  dialogResult: { value: unknown };
  markForCheck: jasmine.Spy;
}

function make(opts: {
  query?: Record<string, string>;
  states?: LineStateDto[];
  stored?: typeof STORED | null;
  config?: SrConfigDto;
  /** Antwort der Gegner-Reihung: Daten, ein Observable (verzögert/Fehler) — Vorgabe: sofort {@link RANKED}. */
  order?: unknown;
  explorer?: unknown;
  offline?: boolean;
} = {}): Made {
  localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Weiß': 'w', 'Schwarz': 'b' }));
  const query = opts.query ?? {};
  const route = { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } };
  const down = () => throwError(() => new Error('offline'));
  const states = opts.states ?? STATES();
  const stored = opts.stored === undefined ? STORED : opts.stored;
  const training: Record<string, jasmine.Spy> = {
    getPgn: jasmine.createSpy('getPgn').and.callFake(() => opts.offline ? down() : of(PGN)),
    getLineStates: jasmine.createSpy('getLineStates').and.callFake(() => opts.offline ? down() : of(states)),
    getRepertoireMeta: jasmine.createSpy('getRepertoireMeta').and.callFake(() => opts.offline ? down() : of({ name: 'Prep: Schett, Franz 2026', opponent: stored })),
    getConfig: jasmine.createSpy('getConfig').and.callFake(() => opts.offline ? down() : of(opts.config ?? config(null, !!stored))),
    reviewLine: jasmine.createSpy('reviewLine').and.returnValue(of(st('A', 3, LATER()))),
    promote: jasmine.createSpy('promote').and.returnValue(of({ affected: 1 })),
    makeDue: jasmine.createSpy('makeDue').and.returnValue(of({ affected: 1 })),
    setPaused: jasmine.createSpy('setPaused').and.returnValue(of({ affected: 1 })),
  };
  const order = opts.order === undefined ? lines() : opts.order;
  const get = jasmine.createSpy('get').and.callFake(() => order instanceof Observable ? order : of(order));
  const dialogResult = { value: undefined as unknown };
  const dialogOpen = jasmine.createSpy('open').and.callFake(() => ({ afterClosed: () => of(dialogResult.value) }));
  const markForCheck = jasmine.createSpy('markForCheck');
  const c = new RepertoireTrainerComponent(
    route as never, training as never, { boardTheme: 'brown', pieceSet: 'cburnett' } as never,
    { instant: (k: string) => k, getCurrentLang: () => 'de' } as never, { markForCheck } as never,
    { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } as never,
    { open: dialogOpen } as never, { enqueue: jasmine.createSpy('enqueue') } as never,
    (opts.explorer ?? { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) }) as never,
    { warn: () => ({}), show: () => ({ onAction: () => EMPTY }) } as never, { ask: () => of(true) } as never,
    { get } as never,
  );
  c.ngOnInit();
  return { c, get, training, dialogOpen, dialogResult, markForCheck };
}

const names = (c: RepertoireTrainerComponent) => c.queue.map(l => l.headers['White']).join('');
const current = (c: RepertoireTrainerComponent) => c.queue[c.qIndex]?.headers['White'];

describe('RepertoireTrainerComponent — „Prep: …"-Repertoire gegen den gespeicherten Gegner (0.999.3)', () => {
  const OFFLINE_KEY = REPERTOIRE_OFFLINE_PREFIX + '1';

  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem('rookhub_rep_train_freq_order');
    localStorage.removeItem(OFFLINE_KEY);
  });

  describe('Abfragen', () => {
    it('bringt nur die fälligen und die noch nie trainierten Linien — nicht mehr alle', () => {
      const { c, get } = make();
      expect(get.calls.mostRecent().args[0]).toBe('/api/prep/player/154602/training-lines');
      expect(c.mode).toBe('quiz');
      // 2 fällig (A, D) + 3 untrainiert (B, E, H) von 8; C, F, G sind erst später fällig
      expect([...names(c)].sort().join('')).toBe('ABDEH');
      expect(c.opponentTotal).toBe(5);
      expect(c.phase).toBe('PLAYING');
    });

    it('Vorgabe „Wenig trainierte zuerst“: untrainierte zuerst, darin die wahrscheinlichsten des Gegners zuerst; dann die fälligen nach Stufe', () => {
      const random = spyOn(Math, 'random').and.callThrough();
      const { c } = make();
      // untrainiert: E (Platz 3), B (4), H (5) · fällig Stufe 1: D · fällig Stufe 2: A
      expect(names(c)).toBe('EBHDA');
      expect(c.lineOrder).toBe('leastTrained');
      expect(random).withContext('Gleichstand entscheidet der Gegner, nicht der Zufall').not.toHaveBeenCalled();
    });

    it('nur die Kapitel der Farbe gegen ihn, nie pausierte Linien', () => {
      const { c } = make({ states: [...STATES().filter(s => s.lineKey !== KEY('D')), st('D', 1, PAST(), { paused: true }),
        { lineKey: lineKeyFromSans(['d4', 'd5', 'c4', 'e6']), level: 1, reps: 1, lapses: 0, dueAt: PAST(), lastReviewedAt: null, inPool: true, paused: false }] });
      expect(names(c)).toBe('EBHA');          // D pausiert, Z ist ein Schwarz-Kapitel
    });

    it('eine ausdrücklich gewählte Einstiegslinie (?line=) kommt immer und zuerst — auch wenn sie nicht fällig ist; danach die Regel', () => {
      const { c } = make({ query: { line: KEY('G') } });   // G ist erst später fällig
      expect(names(c)).toBe('GEBHDA');
      expect(current(c)).toBe('G');
      expect(c.opponentTotal).toBe(6);
    });

    it('frisches Prep-Repertoire (nichts trainiert): alle Linien in der Reihenfolge des Gegners — wie bisher', () => {
      const { c } = make({ states: [] });
      expect(names(c)).toBe('GCAEBHDF');
    });
  });

  describe('Lernen', () => {
    it('bleibt beim Durchgehen ALLER Linien — jetzt in der gewählten Reihenfolge', () => {
      const { c } = make({ query: { mode: 'learn' } });
      expect(c.mode).toBe('learn');
      // untrainiert E B H · Stufe 1 nach Gegner: G C D · Stufe 2: A · Stufe 3: F
      expect(names(c)).toBe('EBHGCDAF');
      expect(c.opponentTotal).toBe(8);
    });

    it('Wechsel Abfragen ↔ Lernen baut jedes Mal nach der Regel des Modus', () => {
      const { c } = make();
      expect(names(c)).toBe('EBHDA');
      c.setMode('learn');
      expect(names(c)).toBe('EBHGCDAF');
      c.setMode('quiz');
      expect(names(c)).toBe('EBHDA');
    });
  });

  describe('die Reihenfolge-Einstellung gilt', () => {
    it('„Nach dem Gegner“: die fälligen und neuen Linien in seiner Reihenfolge', () => {
      const { c } = make({ config: config('opponent') });
      expect(c.lineOrder).toBe('opponent');
      expect(names(c)).toBe('AEBHD');         // Platz 2, 3, 4, 5; D kennt er nicht
    });

    it('„Viel trainierte zuerst“, „Reihenfolge im Repertoire“, „Zufällig“', () => {
      expect(names(make({ config: config('mostTrained') }).c)).toBe('ADEBH');
      expect(names(make({ config: config('repertoire') }).c)).toBe('ABDEH');
      const values = [0.3, 0.9, 0.9, 0.9];   // A B D E H → i=4 tauscht mit 1: A H D E B
      spyOn(Math, 'random').and.callFake(() => values.shift() ?? 0.999);
      expect(names(make({ config: config('random') }).c)).toBe('AHDEB');
    });

    it('„Häufigste zuerst“: holt nach der Reihung des Gegners die Häufigkeiten und reiht danach', () => {
      const endKey = (sans: string[]) => { const ch = new Chess(); for (const s of sans) ch.move(s); return ch.fen().split(' ').slice(0, 3).join(' '); };
      const result: ExplorerAnalysisResult = {
        complete: true, positionsAnalyzed: 8, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
        tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [],
        lineFrequencies: { [endKey(SANS['A'])]: 0.1, [endKey(SANS['B'])]: 0.5, [endKey(SANS['D'])]: 0.3, [endKey(SANS['E'])]: 0.2, [endKey(SANS['H'])]: 0.4 },
      };
      const run = jasmine.createSpy('run').and.returnValue(of(result));
      const { c } = make({ config: config('frequency'), explorer: { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } });
      expect(run).toHaveBeenCalledTimes(1);
      expect(names(c)).toBe('BHDEA');
      expect(c.opponentNotice).toBe('repertoireTrainer.opponentPrep');
    });

    it('der alte Geräte-Schalter „Häufigste zuerst“ zählt hier nicht (er hatte in der Gegner-Sitzung nie Wirkung)', () => {
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      const run = jasmine.createSpy('run').and.returnValue(EMPTY);
      const { c } = make({ explorer: { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } });
      expect(c.lineOrder).toBe('leastTrained');
      expect(run).not.toHaveBeenCalled();
      expect(names(c)).toBe('EBHDA');
    });

    it('der Knopf steht (anders als in der ausdrücklichen Gegner-Sitzung) und der Dialog bietet „Nach dem Gegner“ an', () => {
      const { c, dialogOpen, dialogResult } = make();
      expect(c.showOrderButton).toBeTrue();
      expect(c.moreSolo).toBeFalse();

      dialogResult.value = 'opponent';
      c.openOrder();

      expect(dialogOpen.calls.mostRecent().args[1].data).toEqual(
        { repertoireId: 1, repertoireName: 'Prep: Schett, Franz 2026', current: 'leastTrained', hasOpponent: true });
      expect(c.lineOrder).toBe('opponent');
      expect(names(c)).toBe('AEBHD');         // neu aufgebaut
      expect(c.qIndex).toBe(0);
    });
  });

  describe('Hinweis unter der Kopfleiste passt zur geltenden Reihenfolge', () => {
    const notice = (order: string | null) => make({ config: config(order) }).c.opponentNotice;

    it('„Reihenfolge nach Gegner“ nur, wenn wirklich nach ihm gereiht wird', () => {
      expect(notice('opponent')).toBe('repertoireTrainer.opponentOrder');
    });

    it('nach Trainingsstand: Farbe + „bei Gleichstand zuerst, was er am wahrscheinlichsten spielt“', () => {
      expect(notice(null)).toBe('repertoireTrainer.opponentPrepTies');
      expect(notice('leastTrained')).toBe('repertoireTrainer.opponentPrepTies');
      expect(notice('mostTrained')).toBe('repertoireTrainer.opponentPrepTies');
    });

    it('zufällig / wie im Repertoire: nur noch die Farbe', () => {
      expect(notice('random')).toBe('repertoireTrainer.opponentPrep');
      expect(notice('repertoire')).toBe('repertoireTrainer.opponentPrep');
    });
  });

  describe('nichts fällig und nichts neu', () => {
    const allLater = (): LineStateDto[] => Object.keys(SANS).map((n, i) => st(n, 1 + (i % 3), n === 'C' ? SOON() : LATER()));

    it('Leerzustand wie beim normalen Abfragen (nächste Fälligkeit) — mit dem Weg, trotzdem alle Linien zu üben', () => {
      const { c } = make({ states: allLater() });
      expect(c.phase).toBe('EMPTY');
      expect(c.queue.length).toBe(0);
      // die nächste Fälligkeit DIESER Sitzung: C in zwei Stunden (alle anderen in zwei Tagen)
      expect(Math.abs(new Date(c.nextDueAt!).getTime() - Date.now() - 2 * 3_600_000)).toBeLessThan(60_000);
      expect(c.practiceAllCount).toBe(8);
      expect(c.showOrderButton).toBeFalse();   // im Leerzustand gibt es nichts zu ordnen
    });

    it('„Alle Linien üben“ bringt einmalig alle Linien, ohne Fälligkeit — und ohne am Server etwas fällig zu machen', () => {
      const { c, training } = make({ states: allLater() });

      c.restart();

      expect(c.phase).toBe('PLAYING');
      // Stufe 1: G (Platz 0), A (2), D (–) · Stufe 2: E (3), B (4), H (5) · Stufe 3: C (1), F (–)
      expect(names(c)).toBe('GADEBHCF');
      expect(c.opponentTotal).toBe(8);
      expect(c.practiceAllCount).toBe(0);
      expect(c.opponentDueOnly).toBeFalse();
      expect(training['makeDue']).not.toHaveBeenCalled();
    });

    it('die ausdrückliche Wiederholung endet mit dem Moduswechsel: Abfragen ist danach wieder aufs Fällige beschränkt', () => {
      const { c } = make({ states: allLater() });
      c.restart();
      c.setMode('learn');
      expect(c.queue.length).toBe(8);
      c.setMode('quiz');
      expect(c.phase).toBe('EMPTY');
      expect(c.practiceAllCount).toBe(8);
    });

    it('ohne jede Linie der Farbe (bzw. alle pausiert): kein „Alle Linien üben“ — der Knopf hätte nichts zu tun', () => {
      const { c } = make({ states: Object.keys(SANS).map(n => st(n, 1, PAST(), { paused: true })) });
      expect(c.phase).toBe('EMPTY');
      expect(c.practiceAllCount).toBe(0);
    });
  });

  describe('„Von vorn“ am Ende der Sitzung', () => {
    it('nach dem Durchspielen der fälligen und neuen Linien: der Abschluss sagt das, und der Knopf bringt ausdrücklich ALLE Linien', () => {
      const { c } = make();
      expect(c.opponentDueOnly).toBeTrue();
      c.session.qIndex = c.queue.length;       // alle fünf durch
      c.session.startCurrentLine();
      expect(c.phase).toBe('DONE');
      expect(c.opponentTotal).toBe(5);

      c.restart();

      expect(c.phase).toBe('PLAYING');
      expect(names(c)).toBe('EBHGCDAF');       // alle acht, in der gewählten Reihenfolge
      expect(c.opponentDueOnly).toBeFalse();   // danach heißt der Knopf „Von vorn“ und bringt wieder alle
      c.session.qIndex = c.queue.length;
      c.session.startCurrentLine();
      c.restart();
      expect(c.queue.length).toBe(8);
    });

    it('vergisst die Einstiegslinie (wie bisher): von vorn in der Reihenfolge, nicht wieder ab ihr', () => {
      const { c } = make({ query: { line: KEY('G') } });
      expect(current(c)).toBe('G');
      c.restart();
      expect(names(c)).toBe('EBHGCDAF');
    });
  });

  describe('die Reihung des Gegners kommt nach, während die Einstiegslinie läuft', () => {
    it('die laufende Linie springt nicht; der Rest wird nach seiner Reihenfolge neu gereiht', () => {
      const answers = new Subject<unknown>();
      const { c } = make({ query: { line: KEY('G') }, order: answers });
      // sofort spielbar; solange die Reihung fehlt, Gleichstand in Repertoire-Reihenfolge (B vor E vor H)
      expect(c.phase).toBe('PLAYING');
      expect(names(c)).toBe('GBEHDA');
      expect(c.opponentNotice).toBeNull();
      const running = c.queue[0];
      c.onMove({ orig: 'e2' as never, dest: 'e4' as never });   // mitten in der Linie
      const ply = c.session.currentPly;
      const fen = c.fen;

      answers.next(lines());

      expect(c.queue[c.qIndex]).toBe(running);
      expect(c.qIndex).toBe(0);
      expect(c.session.currentPly).toBe(ply);
      expect(c.fen).toBe(fen);
      expect(names(c)).toBe('GEBHDA');         // jetzt E (Platz 3) vor B (4) vor H (5)
      expect(c.opponentTotal).toBe(6);
      expect(c.opponentNotice).toBe('repertoireTrainer.opponentPrepTies');
      c.session.clearTimers();
    });

    it('scheitert der Abruf: Hinweis, und die weiteren Linien kommen trotzdem (Gleichstand in Repertoire-Reihenfolge)', () => {
      const answers = new Subject<unknown>();
      const { c, markForCheck } = make({ query: { line: KEY('G') }, order: answers });
      markForCheck.calls.reset();

      answers.error(new Error('500'));

      expect(c.opponentNotice).toBe('repertoireTrainer.opponentFailed');
      expect(names(c)).toBe('GBEHDA');
      expect(current(c)).toBe('G');
      expect(markForCheck).toHaveBeenCalled();
    });

    it('„Häufigste zuerst“ mit Einstiegslinie: die Häufigkeiten kommen im Hintergrund, die laufende Linie bleibt, nur der Rest wird neu gereiht', () => {
      const endKey = (sans: string[]) => { const ch = new Chess(); for (const s of sans) ch.move(s); return ch.fen().split(' ').slice(0, 3).join(' '); };
      const result: ExplorerAnalysisResult = {
        complete: true, positionsAnalyzed: 8, positionsPending: 0, rateLimited: false, retryAfterSeconds: null,
        tokenMissing: false, tokenInvalid: false, fetchFailed: false, holes: [],
        lineFrequencies: { [endKey(SANS['A'])]: 0.1, [endKey(SANS['B'])]: 0.5, [endKey(SANS['D'])]: 0.3, [endKey(SANS['E'])]: 0.2, [endKey(SANS['H'])]: 0.4 },
      };
      const run = jasmine.createSpy('run').and.returnValue(of(result));
      const answers = new Subject<unknown>();
      const { c } = make({ query: { line: KEY('G') }, config: config('frequency'), order: answers,
        explorer: { run, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } });
      // die Einstiegslinie läuft sofort; ohne Häufigkeiten gilt bis dahin die Vorgabe
      expect(c.phase).toBe('PLAYING');
      expect(current(c)).toBe('G');
      expect(run).not.toHaveBeenCalled();
      const running = c.queue[0];

      answers.next(lines());

      expect(run).toHaveBeenCalledTimes(1);
      expect(c.phase).withContext('keine Ladeansicht mitten in der Linie').toBe('PLAYING');
      expect(c.queue[c.qIndex]).toBe(running);
      expect(names(c)).toBe('GBHDEA');         // Rest nach Häufigkeit: B .5, H .4, D .3, E .2, A .1
      c.session.clearTimers();
    });

    it('Reihenfolge umstellen, während der Trainer noch auf die Reihung wartet: aufgebaut wird EINMAL, wenn sie da ist', () => {
      const answers = new Subject<unknown>();
      const { c, dialogResult } = make({ order: answers });
      expect(c.phase).toBe('LOADING');

      dialogResult.value = 'opponent';
      c.openOrder();

      expect(c.lineOrder).toBe('opponent');
      expect(c.phase).withContext('noch nichts aufgebaut — sonst spränge die erste Linie beim Eintreffen der Reihung').toBe('LOADING');
      expect(c.queue.length).toBe(0);

      answers.next(lines());

      expect(c.phase).toBe('PLAYING');
      expect(names(c)).toBe('AEBHD');
    });

    it('ohne Einstiegslinie wartet der Trainer auf die Reihung; scheitert sie, baut er mit Repertoire-Gleichstand auf', () => {
      const answers = new Subject<unknown>();
      const { c } = make({ order: answers });
      expect(c.phase).toBe('LOADING');
      answers.error(new Error('500'));
      expect(c.phase).toBe('PLAYING');
      expect(names(c)).toBe('BEHDA');
      expect(c.opponentNotice).toBe('repertoireTrainer.opponentFailed');
    });
  });

  describe('unverändert', () => {
    it('ausdrückliche Gegner-Sitzung (?opponent= von der Spielerkarte): ALLE Linien in seiner Reihenfolge, ohne Fälligkeit, kein Knopf', () => {
      const { c, get } = make({ query: { opponent: 'prep:42', color: 'w' }, config: config('mostTrained') });
      expect(get.calls.mostRecent().args[0]).toBe('/api/prep/player/42/training-lines');
      expect(names(c)).toBe('GCAEBHDF');
      expect(c.opponentTotal).toBe(8);
      expect(c.opponentNotice).toBe('repertoireTrainer.opponentOrder');
      expect(c.showOrderButton).toBeFalse();
      expect(c.moreSolo).toBeTrue();
      expect(c.opponentDueOnly).toBeFalse();
      // „Von vorn“ bleibt dort, was es war: dieselben Linien noch einmal
      c.restart();
      expect(names(c)).toBe('GCAEBHDF');
      // und Lernen ändert die Auswahl nicht
      c.setMode('learn');
      expect(names(c)).toBe('GCAEBHDF');
    });

    it('ausdrückliche Gegner-Sitzung mit Einstiegslinie: ab ihr in seiner Reihenfolge (schneidet ab, wie bisher)', () => {
      const { c } = make({ query: { opponent: 'prep:42', color: 'w', line: KEY('E') } });
      expect(names(c)).toBe('EBHDF');
    });

    it('ohne gespeicherten Gegner: normales Abfragen — nur fällige Pool-Linien, untrainierte bleiben Sache von „Lernen“', () => {
      const values = [0.9];                     // A D bleibt A D
      spyOn(Math, 'random').and.callFake(() => values.shift() ?? 0.999);
      const { c, get } = make({ stored: null });
      expect(get).not.toHaveBeenCalled();
      expect(c.opponent).toBeNull();
      // fällig im Pool: D (Stufe 1), A (Stufe 2) und die Schwarz-Linie gibt es hier nicht im Zustand
      expect(names(c)).toBe('DA');
      expect(c.opponentNotice).toBeNull();
      c.setMode('learn');
      expect(names(c)).toBe('BEHZ');            // ungelernte, wie im Repertoire
    });

    it('ohne gespeicherten Gegner fällt ein gewähltes „Nach dem Gegner“ auf die Vorgabe; der Dialog bietet es nicht an', () => {
      const { c, dialogOpen } = make({ stored: null, config: config('opponent', false) });
      expect(c.lineOrder).toBe('leastTrained');
      c.openOrder();
      expect(dialogOpen.calls.mostRecent().args[1].data.hasOpponent).toBeFalse();
      expect(dialogOpen.calls.mostRecent().args[1].data.current).toBe('leastTrained');
    });
  });

  describe('offline (heruntergeladene Kopie eines Prep-Repertoires)', () => {
    it('keine Gegner-Sitzung — die Reihung käme vom Server: normales Abfragen der fälligen Pool-Linien; „Nach dem Gegner“ fällt auf die Vorgabe', () => {
      localStorage.setItem(OFFLINE_KEY, JSON.stringify({
        meta: { id: 1, name: 'Prep: Schett, Franz 2026' }, pgn: PGN, states: STATES(), config: null, lineOrder: 'opponent', savedAt: '',
      }));
      const values = [0.9];
      spyOn(Math, 'random').and.callFake(() => values.shift() ?? 0.999);
      const { c, get } = make({ offline: true });
      expect(c.offlineSession).toBeTrue();
      expect(get).not.toHaveBeenCalled();
      expect(c.opponent).toBeNull();
      expect(c.storedOpponentSession).toBeFalse();
      expect(c.lineOrder).toBe('leastTrained');
      expect(names(c)).toBe('DA');              // nur die fälligen im Pool; untrainierte nicht
    });
  });
});
