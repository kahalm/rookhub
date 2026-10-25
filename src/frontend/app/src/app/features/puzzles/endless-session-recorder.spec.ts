import { EndlessSessionRecorder, restoreSessionPuzzles } from './endless-session-recorder';

/** Lauf-Protokoll des Endlos-Modus (Zerlegung Z19) — ohne TestBed, mit einem Speicher-Doppel. */
describe('EndlessSessionRecorder', () => {
  function makeStorage() {
    const calls: Record<string, any[][]> = { local: [], server: [], history: [], record: [] };
    const storage: any = {
      saveActiveGameLocal: (g: any) => calls['local'].push([g]),
      saveProgressToServer: (c: any, h: number, g: any) => calls['server'].push([c, h, g]),
      recordSession: (hist: any[], s: any) => { calls['history'].push([s]); return [...hist, s]; },
      recordSessionToServer: (s: any, p: any[]) => {
        calls['record'].push([s, p]);
        return { subscribe: (next: (id: number | null) => void) => next(42) };
      },
    };
    return { storage, calls };
  }
  const RUN = { lives: 2, solved: 3, level: 4, chainIndex: 4, seed: 's1', currentMinRating: 1200, maxRatingReached: 1250, sessionSeconds: 90 };
  const PUZ = (id: number, rating: number): any => ({ id, lichessId: 'L' + id, rating, themes: 'fork', fen: '', moves: '' });

  afterEach(() => { sessionStorage.removeItem('rookhub_endless_gameover'); sessionStorage.removeItem('rookhub_endless_pending_sid'); });

  it('restoreSessionPuzzles: fehlende Zeiten und Lichess-Id werden 0 bzw. leer, Themen fallen weg', () => {
    expect(restoreSessionPuzzles([{ puzzleNumber: 1, puzzleId: 5, rating: 900, solved: true, startedAt: '1000' }])).toEqual([
      { puzzleNumber: 1, puzzleId: 5, lichessId: '', rating: 900, solved: true, themes: undefined, startedAt: 1000, endedAt: 0 },
    ]);
    expect(restoreSessionPuzzles(null)).toEqual([]);
  });

  it('pushPuzzle nummeriert fortlaufend; ohne Startzeit gilt „jetzt“', () => {
    const r = new EndlessSessionRecorder(makeStorage().storage);
    r.pushPuzzle(PUZ(1, 800), true, 500, 900);
    r.pushPuzzle(PUZ(2, 900), false, 0, 1500);
    expect(r.puzzles.map(p => [p.puzzleNumber, p.puzzleId, p.solved, p.startedAt, p.endedAt, p.themes]))
      .toEqual([[1, 1, true, 500, 900, 'fork'], [2, 2, false, 1500, 1500, 'fork']]);
  });

  it('activeGame: resumeAt verschiebt Kettenposition und Stufe, Themen gehören nicht in den Spielstand', () => {
    const r = new EndlessSessionRecorder(makeStorage().storage);
    r.addMistake(1100);
    r.pushPuzzle(PUZ(9, 1100), false, 10, 20);
    const g = r.activeGame(RUN, RUN.chainIndex + 1);
    expect(g).toEqual({
      lives: 2, solved: 3, level: 5, chainIndex: 5, seed: 's1', currentMinRating: 1200, maxRatingReached: 1250,
      sessionSeconds: 90, mistakes: [1100],
      puzzleAttempts: [{ puzzleNumber: 1, puzzleId: 9, lichessId: 'L9', rating: 1100, solved: false, startedAt: 10, endedAt: 20 }],
    });
    expect(r.activeGame(RUN).chainIndex).toBe(4);
  });

  it('sync schreibt denselben Spielstand lokal und an den Server', () => {
    const { storage, calls } = makeStorage();
    const r = new EndlessSessionRecorder(storage);
    const cfg = { startElo: 700, themes: '', stockfishDepth: 16 };
    r.sync(RUN, cfg, 1500);
    expect(calls['local'][0][0]).toEqual(calls['server'][0][2]);
    expect(calls['server'][0][0]).toBe(cfg);
    expect(calls['server'][0][1]).toBe(1500);
  });

  it('recordOnce zeichnet genau einmal auf und merkt die Server-Id', () => {
    const { storage, calls } = makeStorage();
    const r = new EndlessSessionRecorder(storage);
    r.pushPuzzle(PUZ(1, 800), true, 1, 2);
    const ids: number[] = [];
    const make = jasmine.createSpy('make').and.returnValue({ timestamp: 1 });
    const hist = r.recordOnce(make, [], id => ids.push(id));
    expect(hist).toEqual([{ timestamp: 1 } as any]);
    expect(calls['record'][0][1]).toBe(r.puzzles);
    expect(ids).toEqual([42]);
    expect(sessionStorage.getItem('rookhub_endless_pending_sid')).toBe('42');
    expect(r.recordOnce(make, [], id => ids.push(id))).toBeNull();
    expect(make).toHaveBeenCalledTimes(1);
    expect(calls['record'].length).toBe(1);
    r.startFresh();
    expect(r.recorded).toBeFalse();
    expect(r.puzzles).toEqual([]);
  });

  it('showDetail baut das Protokoll aus dem Server-Detail und sperrt das Aufzeichnen', () => {
    const r = new EndlessSessionRecorder(makeStorage().storage);
    r.showDetail({ mistakeAtRatings: '900,x,1000', puzzles: [{ puzzleId: 3, lichessId: null, rating: 900, solved: false }] } as any);
    expect(r.mistakes).toEqual([900, 1000]);
    expect(r.puzzles).toEqual([{ puzzleNumber: 1, puzzleId: 3, lichessId: '', rating: 900, solved: false, startedAt: 0, endedAt: 0 }]);
    expect(r.recorded).toBeTrue();
  });

  it('resumeFrom übernimmt Fehler und Puzzles des Spielstands', () => {
    const r = new EndlessSessionRecorder(makeStorage().storage);
    r.recorded = true;
    r.resumeFrom({ mistakes: [700], puzzleAttempts: [{ puzzleNumber: 1, puzzleId: 2, rating: 700, solved: false }] });
    expect(r.mistakes).toEqual([700]);
    expect(r.puzzles.length).toBe(1);
    expect(r.recorded).toBeFalse();
  });

  it('Game-Over-Schnappschuss und offene Server-Id werden genau einmal gelesen', () => {
    EndlessSessionRecorder.saveGameOverSnapshot({ solved: 5 });
    expect(EndlessSessionRecorder.takeGameOverSnapshot()).toEqual({ solved: 5 });
    expect(EndlessSessionRecorder.takeGameOverSnapshot()).toBeNull();
    sessionStorage.setItem('rookhub_endless_gameover', '{kaputt');
    expect(EndlessSessionRecorder.takeGameOverSnapshot()).toBeNull();
    expect(sessionStorage.getItem('rookhub_endless_gameover')).withContext('unlesbar → bleibt liegen').toBe('{kaputt');
    expect(EndlessSessionRecorder.takePendingSessionId()).toBeUndefined();
    sessionStorage.setItem('rookhub_endless_pending_sid', '17');
    expect(EndlessSessionRecorder.takePendingSessionId()).toBe(17);
    expect(EndlessSessionRecorder.takePendingSessionId()).toBeUndefined();
  });
});
