import { throwError } from 'rxjs';
import { EndlessRunHost, EndlessRunScreen, EndlessRunStore } from './endless-run.store';
import { EndlessRunClock } from './endless-run-timer';
import { ManualClock } from '../../testing/manual-clock';
import { makeEndlessRunStore } from '../../testing/endless-run-store';

/** Synchroner Observable-Stub: ruft next sofort auf. */
function sub(value: unknown): any {
  return { subscribe: (h: any) => { (typeof h === 'function' ? h : h?.next)?.(value); return { unsubscribe() {} }; } };
}

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const CHAIN = [
  { id: 100, lichessId: 'a', fen: FEN, moves: 'e2e4 e7e5', rating: 700 },
  { id: 101, lichessId: 'b', fen: FEN, moves: 'e2e4 e7e5', rating: 900 },
  { id: 102, lichessId: 'c', fen: FEN, moves: 'e2e4 e7e5', rating: 1100 },
];

/** Handuhr mit Wanduhr: `advance()` = 1 s Takt, `now` steht, bis der Test ihn verstellt. */
function manualRunClock(t0 = 1_000_000): ManualClock & EndlessRunClock & { t: number } {
  const c = new ManualClock() as ManualClock & EndlessRunClock & { t: number };
  c.t = t0;
  c.now = () => c.t;
  return c;
}

interface Harness {
  run: EndlessRunStore;
  host: EndlessRunHost & { states: EndlessRunScreen[]; loads: number; notices: string[]; ticks: number[] };
  storage: any;
  puzzleService: any;
  clock: ReturnType<typeof manualRunClock>;
}

function makeStore(opts: { batch?: () => any; loggedIn?: boolean } = {}): Harness {
  const storage: any = {
    loadConfig: (c: any) => c, loadHighscore: () => 0, loadSessionHistory: () => [], loadOfflinePool: () => [],
    loadActiveGameLocal: () => null, saveActiveGameLocal: jasmine.createSpy('saveActiveGameLocal'),
    saveOfflinePool: () => {}, saveChainSeed: () => {}, loadChainSeed: () => '', saveConfig: () => {},
    saveProgressToServer: jasmine.createSpy('saveProgressToServer'), saveProgressImmediate: jasmine.createSpy('saveProgressImmediate'),
    checkHighscore: (max: number, hs: number) => ({ highscore: Math.max(max, hs), isNew: max > hs }),
    recordSession: (hist: any[], s: any) => [...hist, s],
    recordSessionToServer: jasmine.createSpy('recordSessionToServer').and.returnValue(sub(9)),
    loadLiveElapsed: () => null, saveLiveElapsed: jasmine.createSpy('saveLiveElapsed'),
    archiveSession: jasmine.createSpy('archiveSession').and.returnValue(sub(null)),
  };
  const puzzleService: any = {
    getRandomBatch: jasmine.createSpy('getRandomBatch').and.callFake(opts.batch ?? (() => sub(CHAIN.map(p => ({ ...p }))))),
    getWorstThemes: () => sub([]),
  };
  const clock = manualRunClock();
  const run = makeEndlessRunStore({ storage, puzzleService, auth: { isLoggedIn: opts.loggedIn ?? false }, clock });
  const host = {
    states: [] as EndlessRunScreen[], loads: 0, notices: [] as string[], ticks: [] as number[],
    setState(s: EndlessRunScreen) { this.states.push(s); },
    loadCurrent() {
      this.loads++;
      const p = run.currentPuzzle();
      if (p) run.enterPuzzle(p); else run.handleChainEnd();
    },
    notice(k: string) { this.notices.push(k); },
    tick(s: number) { this.ticks.push(s); },
  };
  run.attach(host);
  return { run, host, storage, puzzleService, clock };
}

/** Ein Lauf-Schritt, wie ihn die Komponente auslöst (Löser-Ergebnis → Store). */
type Step = 'start' | 'solve' | 'fail' | 'next' | 'finish';
function apply(h: Harness, step: Step): void {
  const { run } = h;
  switch (step) {
    case 'start': run.start(); break;
    case 'solve': run.markSolved(run.currentPuzzle()); run.syncActiveGame(run.chainIndex() + 1); break;
    case 'fail': if (run.registerLoss(run.currentPuzzle())) run.saveAfterLoss(); break;
    case 'next': if (run.lives() <= 0) run.finish('GAME_OVER'); else run.advance(); break;
    case 'finish': run.finish('WON'); break;
  }
}

describe('EndlessRunStore Übergänge (Tabelle)', () => {
  // Das Aufzeichnen merkt die Server-Id (9) in sessionStorage — wegräumen, sonst liest sie ein anderer Spec.
  afterEach(() => { sessionStorage.removeItem('rookhub_endless_pending_sid'); sessionStorage.removeItem('rookhub_endless_gameover'); });
  const CASES: { name: string; steps: Step[]; want: { lives: number; solved: number; chainIndex: number; level: number; last?: EndlessRunScreen; recorded: number } }[] = [
    { name: 'Start lädt Puzzle 0', steps: ['start'],
      want: { lives: 3, solved: 0, chainIndex: 0, level: 0, last: 'LOADING', recorded: 0 } },
    { name: 'Gelöst + weiter rückt eine Stelle', steps: ['start', 'solve', 'next'],
      want: { lives: 3, solved: 1, chainIndex: 1, level: 1, last: 'LOADING', recorded: 0 } },
    { name: 'Fehler kostet ein Leben und rückt weiter', steps: ['start', 'fail', 'next'],
      want: { lives: 2, solved: 0, chainIndex: 1, level: 1, last: 'LOADING', recorded: 0 } },
    { name: 'Zweiter Fehler am selben Puzzle kostet nichts', steps: ['start', 'fail', 'fail'],
      want: { lives: 2, solved: 0, chainIndex: 0, level: 0, last: 'LOADING', recorded: 0 } },
    { name: 'Drei Fehler → Game Over, einmal aufgezeichnet', steps: ['start', 'fail', 'next', 'fail', 'next', 'fail', 'next'],
      want: { lives: 0, solved: 0, chainIndex: 2, level: 2, last: 'GAME_OVER', recorded: 1 } },
    { name: 'Kette durch → nächster Block, kein Sieg', steps: ['start', 'solve', 'next', 'solve', 'next', 'solve', 'next'],
      want: { lives: 3, solved: 3, chainIndex: 3, level: 3, last: 'LOADING', recorded: 0 } },
    { name: 'Sieg zeichnet einmal auf', steps: ['start', 'solve', 'finish', 'finish'],
      want: { lives: 3, solved: 1, chainIndex: 0, level: 0, last: 'WON', recorded: 1 } },
  ];

  for (const c of CASES) {
    it(c.name, () => {
      const h = makeStore();
      c.steps.forEach(s => apply(h, s));
      const { run } = h;
      expect([run.lives(), run.solved(), run.chainIndex(), run.level()])
        .toEqual([c.want.lives, c.want.solved, c.want.chainIndex, c.want.level]);
      if (c.want.last) expect(h.host.states[h.host.states.length - 1]).toBe(c.want.last);
      expect(h.storage.recordSessionToServer).toHaveBeenCalledTimes(c.want.recorded);
      run.stopSessionTimer();
    });
  }
});

describe('EndlessRunStore', () => {
  // Das Aufzeichnen merkt die Server-Id (9) in sessionStorage — wegräumen, sonst liest sie ein anderer Spec.
  afterEach(() => { sessionStorage.removeItem('rookhub_endless_pending_sid'); sessionStorage.removeItem('rookhub_endless_gameover'); });
  it('Spielstand nach einem Ergebnis zeigt aufs NÄCHSTE Puzzle; bei 0 Leben wird er geräumt', () => {
    const h = makeStore();
    h.run.start();
    apply(h, 'fail');
    expect(h.storage.saveActiveGameLocal.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ lives: 2, chainIndex: 1, level: 1 }));
    h.run.lives.set(1);
    h.run.puzzleLifeLost = false;
    apply(h, 'fail');
    expect(h.storage.saveActiveGameLocal.calls.mostRecent().args[0]).toBeNull();
    expect(h.storage.saveProgressImmediate.calls.mostRecent().args[2]).toBeNull();
    h.run.stopSessionTimer();
  });

  it('Uhr: Takt hebt die Session-Zeit, meldet die Puzzle-Sekunden und sichert den Live-Stand', () => {
    const h = makeStore();
    h.run.start();
    h.clock.t += 1000;
    h.run.timer.beginPuzzle();
    h.clock.advance();
    expect(h.host.ticks.length).toBe(1);
    expect(h.storage.saveLiveElapsed).toHaveBeenCalledWith(jasmine.objectContaining({ seed: h.run.seed, chainIndex: 0 }));
    h.run.stopSessionTimer();
    h.clock.advance();
    expect(h.host.ticks.length).toBe(1);   // angehalten
  });

  it('Start-/Endzeit je Puzzle kommen von der injizierten Uhr', () => {
    const h = makeStore();
    h.run.start();
    h.clock.t = 5000;
    h.run.timer.beginPuzzle();
    h.clock.t = 8000;
    apply(h, 'solve');
    expect(h.run.puzzles[0]).toEqual(jasmine.objectContaining({ puzzleId: 100, solved: true, startedAt: 5000, endedAt: 8000 }));
    h.run.stopSessionTimer();
  });

  it('Fehler vor dem Setup-Zug: das neue Puzzle erbt weder Startzeit noch Sekunden des vorigen', () => {
    const h = makeStore();
    h.run.start();
    h.clock.t = 5000;
    h.run.timer.beginPuzzle();
    h.clock.t = 8000;
    apply(h, 'solve');
    apply(h, 'next');                      // Puzzle 101 geladen, Setup-Zug noch nicht gespielt
    h.clock.t = 9000;
    expect(h.run.timer.puzzleSeconds()).toBe(0);
    apply(h, 'fail');
    expect(h.run.puzzles[1]).toEqual(jasmine.objectContaining({ puzzleId: 101, solved: false, startedAt: 9000, endedAt: 9000 }));
    h.run.stopSessionTimer();
  });

  it('Fortsetzen nach Reload mit passendem Seed nimmt die lokale Kette und den Live-Stand', () => {
    const h = makeStore();
    h.storage.loadChainSeed = () => 'S';
    h.storage.loadLiveElapsed = () => ({ seed: 'S', chainIndex: 2, session: 50, puzzle: 7 });
    h.run.offlinePool = CHAIN.map(p => ({ ...p }));
    h.run.activeGameState.set({ lives: 1, solved: 2, chainIndex: 2, seed: 'S', sessionSeconds: 40 });
    h.run.resume();
    expect(h.puzzleService.getRandomBatch).not.toHaveBeenCalled();
    expect(h.run.currentPuzzle()!.id).toBe(102);
    expect([h.run.lives(), h.run.sessionSeconds(), h.run.timer.resumePuzzleSeconds]).toEqual([1, 50, 7]);
    h.run.stopSessionTimer();
  });

  it('Start ohne Server-Antwort fällt auf die vorab geladene Kette zurück, ohne Vorrat ehrlicher Abbruch', () => {
    const down = () => throwError(() => new Error('down'));
    const a = makeStore({ batch: down });
    a.run.offlinePool = CHAIN.map(p => ({ ...p }));
    a.run.start();
    expect(a.run.currentPuzzle()!.id).toBe(100);
    a.run.stopSessionTimer();

    const b = makeStore({ batch: down });
    b.run.start();
    expect(b.host.notices).toEqual(['endless.loadFailed']);
    expect(b.host.states[b.host.states.length - 1]).toBe('CONFIG');
    b.run.stopSessionTimer();
  });

  it('späte Prefetch-Antwort überschreibt den Vorrat eines inzwischen gestarteten Laufs nicht', () => {
    let emit: (v: unknown) => void = () => {};
    const h = makeStore({ batch: () => ({ subscribe: (o: any) => { emit = o.next; return { unsubscribe() {} }; } }) });
    h.run.prefetchRun();
    h.run.runGeneration++;
    emit([{ id: 1 }]);
    expect(h.run.offlinePool).toEqual([]);
  });

  it('Sicherheitsnetz zeichnet einen beendeten Lauf genau einmal auf, History-Ansicht nie', () => {
    const h = makeStore();
    h.run.start();
    h.run.lives.set(0);
    h.run.rescueUnrecordedRun();
    h.run.rescueUnrecordedRun();
    expect(h.storage.recordSessionToServer).toHaveBeenCalledTimes(1);
    expect(h.run.lastSessionId()).toBe(9);
    h.run.stopSessionTimer();

    const v = makeStore();
    v.run.historyView.set(true);
    v.run.lives.set(0);
    v.run.rescueUnrecordedRun();
    expect(v.storage.recordSessionToServer).not.toHaveBeenCalled();
  });

  it('Archivieren eines offenen Laufs archiviert nur angemeldet und startet danach neu', () => {
    for (const loggedIn of [false, true]) {
      const h = makeStore({ loggedIn });
      h.run.activeGameState.set({ lives: 2, solved: 1, chainIndex: 1, seed: 'S', puzzleAttempts: [] });
      let started = 0;
      h.run.archiveOpenRun(() => started++);
      expect(h.storage.archiveSession).toHaveBeenCalledTimes(loggedIn ? 1 : 0);
      expect(h.run.activeGameState()).toBeNull();
      expect(h.run.profile.sessionHistory().length).toBe(1);
      expect(started).toBe(1);
    }
  });
});
