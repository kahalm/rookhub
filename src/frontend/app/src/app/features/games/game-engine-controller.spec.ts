import { of } from 'rxjs';
import { ExternalEngineService } from '../analysis/external-engine.service';
import { ANALYSIS_DEPTH_KEY, ANALYSIS_PROVIDER_KEY } from '../analysis/analysis-settings';
import { GameEngineController, GameEngineHost } from './game-engine-controller';
import { LiveEngineSession } from './live-engine-session';
import { MistakesSession } from './mistakes-session';

type FakeSession = jasmine.SpyObj<LiveEngineSession>;

function fakeSession(): FakeSession {
  const s = jasmine.createSpyObj<LiveEngineSession>('LiveEngineSession',
    ['sync', 'destroy', 'fen', 'variation', 'canRedo', 'engineId', 'useRemote', 'useBrowser', 'play']);
  s.fen.and.callFake((base: string) => base + '+');
  s.variation.and.returnValue([]);
  s.canRedo.and.returnValue(false);
  s.engineId.and.returnValue(null);
  return s;
}

function fakeTraining(over: Partial<Record<string, unknown>> = {}): MistakesSession {
  const v = { phase: 'right', index: 0, side: 'white', boardFen: 'after', lastMove: ['e2', 'e4'], foundSan: 'e4',
              current: { fenBefore: 'before', bestSan: 'e4' }, ...over };
  return {
    phase: () => v.phase, index: () => v.index, side: () => v.side, boardFen: () => v.boardFen,
    lastMove: () => v.lastMove, foundSan: () => v.foundSan, current: () => v.current,
  } as unknown as MistakesSession;
}

describe('GameEngineController', () => {
  let sessions: FakeSession[];
  let training: MistakesSession | null;
  let loggedIn: boolean;
  let ply: number;
  let engines: jasmine.SpyObj<ExternalEngineService>;
  let ctl: GameEngineController;
  let stored: { provider: string | null; depth: string | null };

  beforeEach(() => {
    sessions = [];
    training = null;
    loggedIn = false;
    ply = 3;
    try { stored = { provider: localStorage.getItem(ANALYSIS_PROVIDER_KEY), depth: localStorage.getItem(ANALYSIS_DEPTH_KEY) }; }
    catch { stored = { provider: null, depth: null }; }
    localStorage.removeItem(ANALYSIS_PROVIDER_KEY);
    localStorage.removeItem(ANALYSIS_DEPTH_KEY);
    engines = jasmine.createSpyObj<ExternalEngineService>('ExternalEngineService', ['listEngines', 'analyse']);
    const host: GameEngineHost = {
      createSession: () => { const s = fakeSession(); sessions.push(s); return s; },
      currentFen: () => 'fen-' + ply,
      currentMoveIndex: () => ply,
      training: () => training,
      loggedIn: () => loggedIn,
    };
    ctl = new GameEngineController(host, engines);
  });

  afterEach(() => {
    const restore = (k: string, v: string | null) => v === null ? localStorage.removeItem(k) : localStorage.setItem(k, v);
    restore(ANALYSIS_PROVIDER_KEY, stored.provider);
    restore(ANALYSIS_DEPTH_KEY, stored.depth);
  });

  it('schaltet die Live-Engine ein und aus und gleicht sie mit der Partie ab', () => {
    expect(ctl.positionFen()).toBe('fen-3');
    ctl.toggleLive();
    const s = sessions[0];
    expect(ctl.live()).toBe(s);
    expect(s.sync).toHaveBeenCalledWith(3, 'fen-3');
    expect(ctl.positionFen()).toBe('fen-3+');
    expect(engines.listEngines).withContext('abgemeldet: keine gemerkte Engine').not.toHaveBeenCalled();

    ply = 4;
    ctl.sync();
    expect(s.sync).toHaveBeenCalledWith(4, 'fen-4');

    ctl.toggleLive();
    expect(s.destroy).toHaveBeenCalled();
    expect(ctl.live()).toBeNull();
  });

  it('inVariation nur mit eigenen Zuegen oder etwas zum Wiederholen', () => {
    expect(ctl.inVariation()).toBeNull();
    ctl.toggleLive();
    expect(ctl.inVariation()).toBeNull();
    sessions[0].canRedo.and.returnValue(true);
    expect(ctl.inVariation()).toBe(sessions[0]);
  });

  it('uebernimmt die am Analysebrett gemerkte externe Engine (ohne Hintergrund-Engines)', () => {
    loggedIn = true;
    localStorage.setItem(ANALYSIS_PROVIDER_KEY, 'cloud');
    engines.listEngines.and.returnValue(of<unknown>({
      hasCredentials: true, tokenInvalid: false, backgroundEngineIds: [],
      engines: [{ id: 'cloud', name: 'Cloud', maxThreads: 1, maxHash: 1 }],
    }) as never);
    ctl.toggleLive();
    expect(sessions[0].useRemote).toHaveBeenCalledTimes(1);
    expect(ctl.currentEngineId()).withContext('Sitzung sagt null').toBeNull();
    ctl.stopLive();
    expect(ctl.currentEngineId()).withContext('ohne Sitzung: die gemerkte').toBe('cloud');
  });

  it('Engine-Wahl ohne laufende Sitzung startet sie; mit Sitzung wechselt sie', () => {
    ctl.chooseLiveEngine(null);
    expect(localStorage.getItem(ANALYSIS_PROVIDER_KEY)).toBe('wasm');
    expect(ctl.live()).not.toBeNull();
    ctl.chooseLiveEngine(null);
    expect(sessions[0].useBrowser).toHaveBeenCalled();
    ctl.chooseLiveEngine({ id: 'x', name: 'X', maxThreads: 1, maxHash: 1 } as never);
    expect(sessions[0].useRemote).toHaveBeenCalled();
    expect(sessions.length).toBe(1);
  });

  it('listet Live-Engines nur angemeldet und ohne Hintergrund-Engines', () => {
    ctl.loadLiveEngines();
    expect(engines.listEngines).not.toHaveBeenCalled();
    loggedIn = true;
    engines.listEngines.and.returnValue(of<unknown>({
      hasCredentials: true, tokenInvalid: false, backgroundEngineIds: ['bg'],
      engines: [{ id: 'a', name: 'A' }, { id: 'bg', name: 'BG' }],
    }) as never);
    ctl.loadLiveEngines();
    expect(ctl.liveEngines().map(e => e.id)).toEqual(['a']);
  });

  it('Training-Analyse: nach der Loesung ab der Stellung VOR dem Fehler, der Zug steht drauf', () => {
    ctl.toggleTrainingAnalysis();
    expect(sessions.length).withContext('ohne Training nichts').toBe(0);
    training = fakeTraining();
    ctl.toggleTrainingAnalysis();
    const a = ctl.trainingAnalysis()!;
    expect(a.base).toBe('before');
    expect(sessions[0].sync).toHaveBeenCalledWith(-2, 'before');
    expect(sessions[0].play).toHaveBeenCalledWith({ from: 'e2', to: 'e4', san: 'e4', fen: 'after' }, 'before');
  });

  it('Training-Analyse: nach einem Fehlversuch ab der Stellung NACH dem eigenen Zug', () => {
    training = fakeTraining({ phase: 'wrong', boardFen: 'tried', lastMove: ['d2', 'd4'] });
    ctl.toggleTrainingAnalysis();
    const a = ctl.trainingAnalysis()!;
    expect(a.base).toBe('tried');
    expect(a.lastMove).toEqual(['d2', 'd4']);
    expect(sessions[0].play).not.toHaveBeenCalled();
  });

  const endCases: [string, Record<string, unknown> | null][] = [
    ['kein Training mehr', null], ['naechste Aufgabe', { index: 1 }], ['andere Seite', { side: 'black' }],
    ['Phase ask', { phase: 'ask' }], ['Phase checking', { phase: 'checking' }], ['Phase done', { phase: 'done' }],
  ];
  for (const [name, change] of endCases) {
    it('sync beendet die Training-Analyse: ' + name, () => {
      training = fakeTraining();
      ctl.toggleTrainingAnalysis();
      const s = sessions[0];
      training = change === null ? null : fakeTraining(change);
      ctl.sync();
      expect(ctl.trainingAnalysis()).toBeNull();
      expect(s.destroy).toHaveBeenCalled();
    });
  }

  it('sync haelt die Training-Analyse bei derselben Aufgabe am Laufen', () => {
    training = fakeTraining();
    ctl.toggleTrainingAnalysis();
    sessions[0].sync.calls.reset();
    ctl.sync();
    expect(ctl.trainingAnalysis()).not.toBeNull();
    expect(sessions[0].sync).toHaveBeenCalledWith(-2, 'before');
  });

  it('destroy beendet beide Sitzungen', () => {
    ctl.toggleLive();
    training = fakeTraining();
    ctl.toggleTrainingAnalysis();
    ctl.destroy();
    expect(sessions.every(s => s.destroy.calls.count() === 1)).toBeTrue();
  });

  it('storedDepth: 6 … 50 aus dem Speicher, sonst 22', () => {
    expect(ctl.storedDepth()).toBe(22);
    localStorage.setItem(ANALYSIS_DEPTH_KEY, '30');
    expect(ctl.storedDepth()).toBe(30);
    localStorage.setItem(ANALYSIS_DEPTH_KEY, '99');
    expect(ctl.storedDepth()).toBe(22);
  });
});
