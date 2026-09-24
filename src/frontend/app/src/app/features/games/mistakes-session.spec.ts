import { Mistake, MistakesBySide } from './mistakes.util';
import { MistakesSession, UnlistedMoveJudge } from './mistakes-session';

describe('MistakesSession', () => {
  const NACH_E4_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
  const NACH_D4_D5 = 'rnbqkbnr/ppp1pppp/8/3p4/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 0 2';

  function fehler(over: Partial<Mistake> = {}): Mistake {
    return {
      ply: 2, white: true, cls: 'blunder', fenBefore: NACH_E4_E5,
      playedSan: 'Qh5', playedUci: 'd1h5', bestUci: 'g1f3', bestSan: 'Nf3',
      acceptUci: ['g1f3'], acceptSan: ['Nf3'],
      checkUnlisted: false,
      evalBefore: { cp: 30 }, evalAfter: { cp: -250 }, lostPercent: 24.3, ...over,
    };
  }

  function setup(bySide: MistakesBySide, side: 'white' | 'black' = 'white') {
    return new MistakesSession(bySide, side);
  }

  const zwei: MistakesBySide = {
    white: [fehler(), fehler({ ply: 4, fenBefore: NACH_D4_D5, playedSan: 'h4', playedUci: 'h2h4', bestUci: 'c2c4', bestSan: 'c4', acceptUci: ['c2c4'], acceptSan: ['c4'] })],
    black: [],
  };

  it('startet bei der ersten Aufgabe mit der Stellung VOR dem Fehler', () => {
    const c = setup(zwei);

    expect(c.phase()).toBe('ask');
    expect(c.boardFen()).toBe(NACH_E4_E5);
    expect(c.index()).toBe(0);
    expect(c.flipped()).toBeFalse();
    expect(c.list().length).toBe(2);
  });

  it('der Zug der Engine zaehlt als gefunden', () => {
    const c = setup(zwei);

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });

    expect(c.phase()).toBe('right');
    expect(c.solved()).toBe(1);
    expect(c.boardFen()).toBe('danach');
    expect(c.lastMove()).toEqual(['g1', 'f3']);
  });

  it('ein anderer Zug ist daneben; nach „Nochmal" steht die Ausgangsstellung wieder da', () => {
    const c = setup(zwei);

    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'falsch' });

    expect(c.phase()).toBe('wrong');
    expect(c.tried()).toBe('Nc3');
    expect(c.boardFen()).toBe('falsch');

    c.retry();

    expect(c.phase()).toBe('ask');
    expect(c.boardFen()).toBe(NACH_E4_E5);
    expect(c.lastMove()).toBeUndefined();
  });

  it('wer erst danebengreift, bekommt die Aufgabe nicht als selbst gefunden gutgeschrieben', () => {
    const c = setup(zwei);

    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'falsch' });
    c.retry();
    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });

    expect(c.phase()).toBe('right');
    expect(c.solved()).toBe(0);
  });

  it('nach dem Urteil nimmt das Brett keine Zuege mehr an', () => {
    const c = setup(zwei);

    expect(c.playable()).toBeTrue();
    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });
    expect(c.playable()).toBeFalse();
    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'zuspaet' });

    expect(c.boardFen()).toBe('danach');
    expect(c.solved()).toBe(1);
  });

  it('„Loesung zeigen" spielt den Zug vor und zaehlt nicht als gefunden', () => {
    const c = setup(zwei);

    c.showSolution();

    expect(c.phase()).toBe('shown');
    expect(c.lastMove()).toEqual(['g1', 'f3']);
    expect(c.boardFen()).toContain('N');   // Springer steht jetzt auf f3
    expect(c.boardFen()).not.toBe(NACH_E4_E5);
    expect(c.solved()).toBe(0);
  });

  it('eine Umwandlung zaehlt auch ohne genannte Figur — das Brett wandelt ohne Rueckfrage in eine Dame um', () => {
    const c = setup({ white: [fehler({ fenBefore: '4k3/4P3/8/8/8/8/8/4K3 w - - 0 1', bestUci: 'e7e8q', bestSan: 'e8=Q+', acceptUci: ['e7e8q'], acceptSan: ['e8=Q+'] })], black: [] });

    c.onMove({ from: 'e7', to: 'e8', san: 'e8=Q+', fen: 'danach' });

    expect(c.phase()).toBe('right');
  });

  it('am Ende steht die Bilanz', () => {
    const c = setup(zwei);

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });
    c.next();

    expect(c.index()).toBe(1);
    expect(c.phase()).toBe('ask');
    expect(c.boardFen()).toBe(NACH_D4_D5);
    expect(c.last()).toBeTrue();

    c.next();

    expect(c.phase()).toBe('done');
    expect(c.solved()).toBe(1);

    c.restart();

    expect(c.phase()).toBe('ask');
    expect(c.index()).toBe(0);
    expect(c.solved()).toBe(0);
  });

  it('mit Fehlern auf beiden Seiten laesst sich umschalten — Brett gedreht, Zaehler zurueck', () => {
    const c = setup({ white: zwei.white, black: [fehler({ white: false, fenBefore: NACH_D4_D5 })] }, 'white');

    expect(c.bothSides).toBeTrue();
    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });
    c.chooseSide('black');

    expect(c.side()).toBe('black');
    expect(c.flipped()).toBeTrue();
    expect(c.list().length).toBe(1);
    expect(c.index()).toBe(0);
    expect(c.solved()).toBe(0);
    expect(c.phase()).toBe('ask');
  });

  it('ohne Fehler auf der gewaehlten Seite gibt es nichts zu tun', () => {
    const c = setup({ white: zwei.white, black: [] }, 'black');

    expect(c.list()).toEqual([]);
    expect(c.bothSides).toBeFalse();
  });
  it('ein gleichwertiger Zug zaehlt als gefunden — und die Rueckmeldung nennt den Bestzug dazu', () => {
    const c = setup({ white: [fehler({ acceptUci: ['g1f3', 'b1c3'], acceptSan: ['Nf3', 'Nc3'] })], black: [] });

    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'danach' });

    expect(c.phase()).toBe('right');
    expect(c.solved()).toBe(1);
    expect(c.foundSan()).toBe('Nc3');
    expect(c.foundBest()).toBeFalse();
  });

  it('der Bestzug selbst meldet sich als Bestzug', () => {
    const c = setup(zwei);

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });

    expect(c.foundBest()).toBeTrue();
  });
  describe('nicht gelistete Zuege', () => {
    /** Ein Urteil, das der Test von aussen aufloest — so laesst sich der Zwischenstand „rechnet" pruefen. */
    function judgeLater() {
      let resolve!: (v: boolean | null) => void;
      const calls: string[] = [];
      const judge: UnlistedMoveJudge = (_m, fen) => { calls.push(fen); return new Promise(r => { resolve = r; }); };
      return { judge, calls, answer: (v: boolean | null) => resolve(v) };
    }
    const offen = { white: [fehler({ checkUnlisted: true })], black: [] };

    it('fragt die Engine, zeigt solange „rechnet" und laesst das Brett nicht ziehen', async () => {
      const j = judgeLater();
      const c = new MistakesSession(offen, 'white', j.judge);

      c.onMove({ from: 'a2', to: 'a3', san: 'a3', fen: 'nach-a3' });

      expect(j.calls).toEqual(['nach-a3']);
      expect(c.phase()).toBe('checking');
      expect(c.playable()).toBeFalse();

      j.answer(true);
      await Promise.resolve();

      expect(c.phase()).toBe('right');
      expect(c.foundSan()).toBe('a3');
      expect(c.foundByEngine()).toBeTrue();
      expect(c.solved()).toBe(1);
    });

    it('sagt die Engine nein, ist es daneben; kann sie nicht pruefen, steht das dabei', async () => {
      const j = judgeLater();
      const c = new MistakesSession(offen, 'white', j.judge);

      c.onMove({ from: 'a2', to: 'a3', san: 'a3', fen: 'x' });
      j.answer(null);
      await Promise.resolve();

      expect(c.phase()).toBe('wrong');
      expect(c.checkFailed()).toBeTrue();
      expect(c.tried()).toBe('a3');
    });

    it('ohne den Vermerk wird gar nicht gefragt', () => {
      const j = judgeLater();
      const c = new MistakesSession(zwei, 'white', j.judge);

      c.onMove({ from: 'a2', to: 'a3', san: 'a3', fen: 'x' });

      expect(j.calls).toEqual([]);
      expect(c.phase()).toBe('wrong');
    });

    it('ein spaetes Urteil zu einer schon verlassenen Aufgabe wird verworfen', async () => {
      const j = judgeLater();
      const c = new MistakesSession({ white: [fehler({ checkUnlisted: true }), fehler({ ply: 4 })], black: [] }, 'white', j.judge);

      c.onMove({ from: 'a2', to: 'a3', san: 'a3', fen: 'x' });
      c.next();
      j.answer(true);
      await Promise.resolve();

      expect(c.index()).toBe(1);
      expect(c.phase()).toBe('ask');
      expect(c.solved()).toBe(0);
    });
  });
});
