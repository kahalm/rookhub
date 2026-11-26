import { LIVE_MAX_ADDED, LIVE_MAX_EXT, evalText, joinParam, lastMovePair, parseAddParam, parseBroadcastLink, parseExtParam, liveBestMove, liveMoveNumber, shownClock, whiteShare } from './live-boards.util';
import { LiveGame } from './live-boards.models';

describe('live-boards.util', () => {
  it('whiteShare: 50 bei 0, Matt ganz, ohne Bewertung null', () => {
    expect(whiteShare({ cp: 0, mate: null, source: 'cloud', depth: null })).toBe(50);
    expect(whiteShare({ cp: 300, mate: null, source: 'cloud', depth: null })!).toBeGreaterThan(70);
    expect(whiteShare({ cp: null, mate: -2, source: 'cloud', depth: null })).toBe(0);
    expect(whiteShare(null)).toBeNull();
  });

  it('evalText', () => {
    expect(evalText({ cp: 42, mate: null, source: 'cloud', depth: 30 })).toBe('+0.4');
    expect(evalText({ cp: null, mate: -3, source: 'broadcast', depth: null })).toBe('#−3');
  });

  it('lastMovePair', () => {
    expect(lastMovePair('e7e8q')).toEqual(['e7', 'e8']);
    expect(lastMovePair(null)).toBeUndefined();
  });

  it('shownClock: nur die Seite am Zug läuft, fertige Partie steht', () => {
    const g = {
      white: { clockSeconds: 600 }, black: { clockSeconds: 500 }, whiteToMove: true, finished: false, plies: 10,
    } as unknown as LiveGame;
    expect(shownClock(g, true, 0, 5000)).toBe(595);
    expect(shownClock(g, false, 0, 5000)).toBe(500);
    expect(shownClock({ ...g, finished: true }, true, 0, 5000)).toBe(600);
    expect(shownClock(g, true, 5000, 1000)).toBe(600);   // „jetzt“ vor dem Eintreffen: keine Sekunde dazu
  });

  // 1.025.0: weitere Bretter in der Adresse
  it('parseAddParam: Begegnung.Brett, Brett, Lichess-Kennung; Unsinn und Doppel weg, gedeckelt', () => {
    expect(parseAddParam('3.2, 7,AbCd1234,3.2,../x,1234.5,')).toEqual(['3.2', '7', 'AbCd1234']);
    expect(parseAddParam(null)).toEqual([]);
    expect(parseAddParam(Array.from({ length: 40 }, (_, i) => `1.${i + 1}`).join(',')).length).toBe(LIVE_MAX_ADDED);
  });

  it('parseExtParam: nur roundId.gameId, höchstens 4 Runden und 12 Bretter', () => {
    expect(parseExtParam('fnmEB1kH.RD8sbYw1,bad,fnmEB1kH.RD8sbYw1,fnmEB1k.RD8sbYw1')).toEqual(['fnmEB1kH.RD8sbYw1']);
    const five = Array.from({ length: 5 }, (_, i) => `Round00${i}.Game0001`).join(',');
    expect(new Set(parseExtParam(five).map(k => k.slice(0, 8))).size).toBe(4);
    expect(parseExtParam(Array.from({ length: 20 }, (_, i) => `fnmEB1kH.Game00${10 + i}`).join(',')).length).toBe(LIVE_MAX_EXT);
    expect(joinParam([])).toBeNull();
    expect(joinParam(['a', 'b'])).toBe('a,b');
  });

  it('parseBroadcastLink: Partie, Runde, Turnier; andere Wirte und schiefe Kennungen nicht', () => {
    expect(parseBroadcastLink('https://lichess.org/broadcast/fide-world-chess-championship-2026/game-3/fnmEB1kH/RD8sbYw1'))
      .toEqual({ tourId: null, roundId: 'fnmEB1kH', gameId: 'RD8sbYw1' });
    expect(parseBroadcastLink('lichess.org/broadcast/wm/game-3/fnmEB1kH')).toEqual({ tourId: null, roundId: 'fnmEB1kH', gameId: null });
    expect(parseBroadcastLink('https://lichess.org/broadcast/wm/WmTour01')).toEqual({ tourId: 'WmTour01', roundId: null, gameId: null });
    expect(parseBroadcastLink('https://evil.example/broadcast/wm/game-3/fnmEB1kH')).toBeNull();
    expect(parseBroadcastLink('https://lichess.org/study/fnmEB1kH/RD8sbYw1')).toBeNull();
    expect(parseBroadcastLink('https://lichess.org/broadcast/wm/game-3/fnmEB1kH/RD8-bYw1')).toBeNull();
    expect(parseBroadcastLink('kein link')).toBeNull();
  });
  it('liveMoveNumber: laufend floor(plies/2)+1, fertig ceil(plies/2), vor dem ersten Zug null', () => {
    expect(liveMoveNumber({ plies: 0, finished: false })).toBeNull();
    expect(liveMoveNumber({ plies: 1, finished: false })).toBe(1);
    expect(liveMoveNumber({ plies: 66, finished: false })).toBe(34);
    expect(liveMoveNumber({ plies: 67, finished: false })).toBe(34);
    expect(liveMoveNumber({ plies: 81, finished: true })).toBe(41);
    expect(liveMoveNumber({ plies: 82, finished: true })).toBe(41);
  });
  it('liveBestMove: SAN zum UCI, Rochade auch als König-auf-Turm, illegal → null', () => {
    const start = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    expect(liveBestMove(start, 'g1f3')).toEqual({ from: 'g1', to: 'f3', san: 'Nf3' });
    expect(liveBestMove(start, 'e7e5')).toBeNull();
    expect(liveBestMove(start, null)).toBeNull();
    expect(liveBestMove('kaputt', 'g1f3')).toBeNull();
    const castle = 'r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1';
    expect(liveBestMove(castle, 'e1h1')).toEqual({ from: 'e1', to: 'g1', san: 'O-O' });
    expect(liveBestMove(castle, 'e1c1')!.san).toBe('O-O-O');
    expect(liveBestMove('8/P6k/8/8/8/8/8/K7 w - - 0 1', 'a7a8q')!.san).toBe('a8=Q');
  });
});
