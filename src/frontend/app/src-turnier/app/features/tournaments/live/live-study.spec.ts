import { BehaviorSubject } from 'rxjs';
import { AnalysisEngineService, AnalysisState } from '@rh/features/analysis/analysis-engine.service';
import { LIVE_FS_KEY, LiveStudy, readLiveFsPrefs, writeLiveFsPrefs } from './live-study';
import { LiveGame } from './live-boards.models';

/** Engine ohne Worker: merkt sich die Aufträge. */
function fakeEngine() {
  const state$ = new BehaviorSubject<AnalysisState>({ fen: '', depth: 0, lines: [], running: false, nodes: 0, nps: 0 });
  const engine = {
    analysis$: state$.asObservable(), analyzed: [] as string[], multiPv: 0, stopped: 0, destroyed: 0,
    analyze(fen: string) { this.analyzed.push(fen); return Promise.resolve(); },
    setMultiPv(n: number) { this.multiPv = n; }, setDepth() { /* egal */ }, setRemoteEngine() { /* egal */ },
    remoteFallback$: new BehaviorSubject<boolean>(false), stop() { this.stopped++; }, destroy() { this.destroyed++; },
  };
  return { engine, state$ };
}

const PGN = `[Event "Bundesliga"]
[White "Weiß"]
[Black "Schwarz"]

1. e4 { [%clk 1:30:00] } e5 { [%clk 1:29:50] } 2. Nf3 { [%clk 1:29:40] } *`;
const AFTER_NF3 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2';
const AFTER_NC6 = 'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3';
const AFTER_BB5 = 'r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3';

function live(fen: string, lastMove: string, plies: number, blackClock: number | null = null): LiveGame {
  return {
    id: 'g1', boardTag: '1.1', fen, lastMove, plies, result: '*', eval: null, finished: false, whiteToMove: fen.includes(' w '),
    white: { name: 'Weiß', title: null, elo: null, fideId: null, team: null, clockSeconds: null },
    black: { name: 'Schwarz', title: null, elo: null, fideId: null, team: null, clockSeconds: blackClock },
  };
}

describe('LiveStudy', () => {
  function setup(engineOn = true) {
    const { engine, state$ } = fakeEngine();
    const study = new LiveStudy(() => engine as unknown as AnalysisEngineService, { engine: engineOn, lines: 2 });
    return { study, engine, state$ };
  }

  it('lädt die Züge aus dem PGN, folgt der Partie und hängt passende Live-Züge an (samt Uhr)', () => {
    const { study, engine } = setup();
    study.update(live(AFTER_NF3, 'g1f3', 3));
    expect(study.loadPgn(PGN)).toBeTrue();
    expect(study.moves().map(m => m.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(study.clocks()[1]).toBe(5390);
    expect(study.following()).toBeTrue();
    expect(study.index()).toBe(2);
    expect(study.fen()).toBe(AFTER_NF3);
    expect(engine.multiPv).toBe(2);

    expect(study.update(live(AFTER_NC6, 'b8c6', 4, 5300))).toBe('appended');
    expect(study.moves().map(m => m.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6']);
    expect(study.clocks()[3]).toBe(5300);
    expect(study.index()).toBe(3);                   // folgt
    expect(study.fen()).toBe(AFTER_NC6);
    expect(study.lastMove()).toEqual(['b8', 'c6']);
    expect(engine.analyzed[engine.analyzed.length - 1]).toBe(AFTER_NC6);
    expect(study.update(live(AFTER_NC6, 'b8c6', 4))).toBe('same');
  });

  it('wer zurückblättert, bleibt stehen, wenn neue Züge kommen; der letzte Zug heißt wieder folgen', () => {
    const { study } = setup();
    study.update(live(AFTER_NF3, 'g1f3', 3));
    study.loadPgn(PGN);
    study.goTo(0);
    expect(study.following()).toBeFalse();
    expect(study.lastMove()).toEqual(['e2', 'e4']);
    study.update(live(AFTER_NC6, 'b8c6', 4));
    expect(study.index()).toBe(0);                   // nicht weggesprungen
    expect(study.fen()).toBe(study.moves()[0].after);
    study.next(); study.next(); study.next();
    expect(study.following()).toBeTrue();
    expect(study.atLive()).toBeTrue();
    study.first();
    expect(study.index()).toBe(-1);
    study.last();
    expect(study.atLive()).toBeTrue();
  });

  it('eigene Züge: Ansicht heftet sich an, Live-Züge überschreiben sie nicht, „Zur Live-Stellung“ verwirft sie', () => {
    const { study } = setup();
    study.update(live(AFTER_NF3, 'g1f3', 3));
    study.loadPgn(PGN);
    study.setOwnMoves(true);
    study.play({ from: 'g8', to: 'f6', san: 'Nf6', fen: 'rnbqkb1r/pppp1ppp/5n2/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3' });
    expect(study.atLive()).toBeFalse();
    expect(study.following()).toBeFalse();
    study.update(live(AFTER_NC6, 'b8c6', 4));
    study.update(live(AFTER_BB5, 'f1b5', 5));
    expect(study.moves().length).toBe(5);            // Partie läuft weiter
    expect(study.session.variationSan()).toBe('2... Nf6');   // eigene Züge bleiben
    expect(study.fen()).toContain('5n2');
    study.prev();                                    // eigenen Zug zurück
    expect(study.fen()).toBe(AFTER_NF3);
    study.next();
    expect(study.session.variation().length).toBe(1);
    study.backToLive();
    expect(study.atLive()).toBeTrue();
    expect(study.fen()).toBe(AFTER_BB5);
    study.play({ from: 'a7', to: 'a6', san: 'a6', fen: 'r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4' });
    study.setOwnMoves(false);                        // „Selbst ziehen“ aus → eigene Züge weg
    expect(study.session.variation().length).toBe(0);
  });

  it('passt ein Live-Ereignis nicht an die Zugliste, heißt das „stale“ (PGN neu holen) — das Brett zeigt trotzdem live', () => {
    const { study } = setup();
    study.update(live(AFTER_NF3, 'g1f3', 3));
    study.loadPgn(PGN);
    expect(study.update(live(AFTER_BB5, 'f1b5', 5))).toBe('stale');   // Nc6 verpasst
    expect(study.fen()).toBe(AFTER_BB5);
    expect(study.loadPgn(PGN + ' ')).toBeTrue();
  });

  it('Engine aus rechnet nichts; an rechnet die Stellung am Brett und liefert Pfeile', () => {
    const { study, engine, state$ } = setup(false);
    study.update(live(AFTER_NF3, 'g1f3', 3));
    study.loadPgn(PGN);
    expect(engine.analyzed).toEqual([]);
    expect(study.arrows()).toEqual([]);
    study.setEngine(true);
    expect(engine.analyzed).toEqual([AFTER_NF3]);
    state$.next({ fen: AFTER_NF3, depth: 14, running: true, nodes: 1, nps: 1, lines: [
      { multipv: 1, depth: 14, scoreType: 'cp', score: -20, evalText: '-0.20', pvUci: ['b8c6'] }] });
    expect(study.arrows()).toEqual([{ from: 'b8', to: 'c6', brush: 'blue' }]);
    expect(study.session.topEval()).toEqual({ cp: -20, mate: null });
    study.destroy();
    expect(engine.destroyed).toBe(1);
  });

  it('merkt Engine und Linienzahl je Gerät; kaputter Wert → Vorgabe', () => {
    writeLiveFsPrefs({ engine: false, lines: 5 });
    expect(readLiveFsPrefs()).toEqual({ engine: false, lines: 5 });
    localStorage.setItem(LIVE_FS_KEY, '{kaputt');
    expect(readLiveFsPrefs()).toEqual({ engine: true, lines: 3 });
    localStorage.setItem(LIVE_FS_KEY, JSON.stringify({ lines: 99 }));
    expect(readLiveFsPrefs()).toEqual({ engine: true, lines: 3 });
    localStorage.removeItem(LIVE_FS_KEY);
  });
});
