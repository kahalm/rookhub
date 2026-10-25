import { GuessPlayback, GuessPlaybackHost, PlaybackClock } from './guess-playback';
import { GuessHistoryMove, GuessSession } from './guess.service';

/** Handgesteuerte Uhr: merkt sich die eine laufende Wartezeit samt Dauer. */
class FakeClock implements PlaybackClock {
  pending: { fn: () => void; ms: number; id: number } | null = null;
  private next = 1;
  cleared: number[] = [];
  setTimeout(fn: () => void, ms: number): unknown {
    this.pending = { fn, ms, id: this.next++ };
    return this.pending.id;
  }
  clearTimeout(handle: unknown): void {
    this.cleared.push(handle as number);
    if (this.pending?.id === handle) this.pending = null;
  }
  /** Die laufende Wartezeit ablaufen lassen; liefert ihre Dauer. */
  fire(): number {
    const p = this.pending!;
    this.pending = null;
    p.fn();
    return p.ms;
  }
}

function mv(ply: number, uci: string, comment?: string): GuessHistoryMove {
  return { ply, moveNumber: Math.floor(ply / 2) + 1, white: ply % 2 === 0, san: uci, uci, fen: 'fen-' + ply, comment };
}

function session(history: GuessHistoryMove[]): GuessSession {
  return {
    id: 1, gameAnalysisId: 1, title: '', white: '', black: '', guessWhite: true, startPly: 0, status: 'running',
    points: 0, maxPoints: 0, movesPlayed: 0, gameMoveHits: 0, secondsSpent: 0,
    position: null, totalGuesses: 1, startFen: null, history,
  } as GuessSession;
}

function setup(gameMove: string | null = 'a2a4') {
  const log: string[] = [];
  const host: GuessPlaybackHost = {
    showStep: s => log.push('show ' + s.ply),
    apply: s => log.push('apply ' + s.history.length),
    setBusy: b => log.push('busy ' + b),
    clearStepNote: () => log.push('clearNote'),
    gameMoveUci: () => gameMove ?? undefined,
    changed: () => log.push('changed'),
  };
  const clock = new FakeClock();
  return { pb: new GuessPlayback(host, clock), clock, log };
}

describe('GuessPlayback', () => {
  /** Tabelle: Schrittfolge → Wartezeiten zwischen den Schritten (Partiezug 1 s, Rest 0,5 s). */
  const cases: { name: string; history: GuessHistoryMove[]; from: number; delays: number[] }[] = [
    { name: 'Partiezug + Antwort', history: [mv(0, 'x'), mv(8, 'a2a4'), mv(9, 'b7b5')], from: 8, delays: [1000] },
    { name: 'mit uebersprungenem Zug', history: [mv(8, 'a2a4'), mv(9, 'b7b5'), mv(10, 'c2c4'), mv(11, 'd7d5')],
      from: 8, delays: [1000, 500, 500] },
    { name: 'Antwort ist nicht der Partiezug', history: [mv(8, 'h2h4'), mv(9, 'b7b5')], from: 8, delays: [500] },
    { name: 'ein einziger Schritt', history: [mv(8, 'a2a4')], from: 8, delays: [] },
  ];
  for (const t of cases) {
    it('Wartezeiten: ' + t.name, () => {
      const { pb, clock, log } = setup();
      pb.start(session(t.history), t.from);
      const delays: number[] = [];
      while (clock.pending) delays.push(clock.fire());
      expect(delays).toEqual(t.delays);
      expect(log[log.length - 1]).toBe(t.delays.length ? 'changed' : 'busy false');
      expect(log).toContain('apply ' + t.history.length);
      expect(pb.running).toBeFalse();
    });
  }

  it('ohne neue Halbzuege: sofort die Sitzung, Brett frei', () => {
    const { pb, clock, log } = setup();
    pb.start(session([mv(0, 'x')]), 8);
    expect(log).toEqual(['apply 1', 'busy false']);
    expect(clock.pending).toBeNull();
  });

  it('ohne Partiezug (noch kein Rateversuch) laufen alle Schritte mit 0,5 s', () => {
    const { pb, clock } = setup(null);
    pb.start(session([mv(8, 'a2a4'), mv(9, 'b7b5')]), 8);
    expect(clock.fire()).toBe(500);
  });

  it('haelt am Kommentar mitten in der Folge, „Weiter" spielt den naechsten Schritt', () => {
    const { pb, clock, log } = setup();
    pb.start(session([mv(8, 'a2a4', 'Text'), mv(9, 'b7b5')]), 8);
    expect(pb.holding).toBeTrue();
    expect(pb.holdingNote).toBeTrue();
    expect(clock.pending).withContext('keine Zeitschranke').toBeNull();
    expect(log).toEqual(['show 8', 'busy false']);

    expect(pb.continue()).toBeTrue();
    expect(pb.holding).toBeFalse();
    expect(pb.holdingNote).toBeFalse();
    expect(log.slice(2)).toEqual(['clearNote', 'show 9', 'apply 2', 'busy false']);
  });

  it('am letzten Schritt kein Halt, auch mit Kommentar', () => {
    const { pb, log } = setup();
    pb.start(session([mv(8, 'a2a4'), mv(9, 'b7b5', 'Text')]), 8);
    expect(log).toEqual(['show 8', 'busy true']);
  });

  it('eigener anderer Zug: haelt, „Weiter" startet ab dem gemerkten Halbzug', () => {
    const { pb, clock, log } = setup();
    pb.hold(session([mv(6, 'x'), mv(8, 'a2a4'), mv(9, 'b7b5')]), 8);
    expect(pb.holding).toBeTrue();
    expect(pb.holdingNote).toBeFalse();
    expect(pb.continue()).toBeTrue();
    expect(pb.holding).toBeFalse();
    expect(log).toEqual(['show 8', 'busy true']);
    expect(clock.fire()).toBe(1000);
  });

  it('„Weiter" waehrend einer Wartezeit tut nichts', () => {
    const { pb, log } = setup();
    pb.start(session([mv(8, 'a2a4'), mv(9, 'b7b5')]), 8);
    expect(pb.running).toBeTrue();
    const before = log.length;
    expect(pb.continue()).toBeFalse();
    expect(log.length).toBe(before);
  });

  it('dropHold vergisst die wartende Sitzung', () => {
    const { pb, log } = setup();
    pb.hold(session([mv(8, 'a2a4')]), 8);
    pb.dropHold();
    expect(pb.holding).toBeFalse();
    expect(pb.continue()).toBeTrue();
    expect(log).withContext('nichts mehr abzuspielen').toEqual([]);
  });

  it('dispose raeumt die laufende Wartezeit ab', () => {
    const { pb, clock } = setup();
    pb.start(session([mv(8, 'a2a4'), mv(9, 'b7b5')]), 8);
    pb.dispose();
    expect(clock.pending).toBeNull();
    expect(clock.cleared.length).toBe(1);
    expect(pb.running).toBeFalse();
  });
});
