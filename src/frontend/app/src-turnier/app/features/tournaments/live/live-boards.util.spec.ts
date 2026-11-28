import { BroadcastLink, LIVE_MAX_ADDED, analysisLinkTarget, broadcastHost, broadcastRoundUrl, extRoundOf, finishedLast, flipDeltas, isLiveGameIdFor, isLiveRoundId, LIVE_MAX_EXT, evalText, joinParam, lastMovePair, parseAddParam, parseBroadcastLink, parseExtParam, liveBestMove, liveMoveNumber, shownClock, whiteShare } from './live-boards.util';
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
      .toEqual({ source: 'lichess', tourId: null, roundId: 'fnmEB1kH', gameId: 'RD8sbYw1' });
    expect(parseBroadcastLink('lichess.org/broadcast/wm/game-3/fnmEB1kH')).toEqual({ source: 'lichess', tourId: null, roundId: 'fnmEB1kH', gameId: null });
    expect(parseBroadcastLink('https://lichess.org/broadcast/wm/WmTour01')).toEqual({ source: 'lichess', tourId: 'WmTour01', roundId: null, gameId: null });
    expect(parseBroadcastLink('https://evil.example/broadcast/wm/game-3/fnmEB1kH')).toBeNull();
    expect(parseBroadcastLink('https://lichess.org/study/fnmEB1kH/RD8sbYw1')).toBeNull();
    expect(parseBroadcastLink('https://lichess.org/broadcast/wm/game-3/fnmEB1kH/RD8-bYw1')).toBeNull();
    expect(parseBroadcastLink('kein link')).toBeNull();
  });

  // 1.040.0: chess.com-Übertragungen (wie ChessComEvents.LinkOf im Server, gleiche Fälle wie ChessComLiveTests)
  it('parseBroadcastLink: chess.com-Event, Tab, Runde, Partie, Dashboard, Sprache, Gruppe', () => {
    const wc = '2026-fide-chess-world-championship';
    const cc = (round: string | null, game: string | null): BroadcastLink =>
      ({ source: 'chesscom', tourId: 'cc~' + wc, roundId: round ? `cc~${wc}~${round}` : null, gameId: game });
    expect(parseBroadcastLink(`https://www.chess.com/events/${wc}`)).toEqual(cc(null, null));
    expect(parseBroadcastLink(`chess.com/events/${wc}/games`)).toEqual(cc(null, null));
    expect(parseBroadcastLink(`https://www.chess.com/events/${wc}/results?x=1#y`)).toEqual(cc(null, null));
    expect(parseBroadcastLink(`https://www.chess.com/events/${wc}/03/Sindarov_Javokhir-Gukesh_D`)).toEqual(cc('03', 'Sindarov_Javokhir-Gukesh_D'));
    expect(parseBroadcastLink(`https://www.chess.com/de/events/${wc}/03/Sindarov_Javokhir-Gukesh_D`)).toEqual(cc('03', 'Sindarov_Javokhir-Gukesh_D'));
    expect(parseBroadcastLink(`https://www.chess.com/events/${wc}/dashboard/03/Sindarov_Javokhir-Gukesh_D`)).toEqual(cc('03', 'Sindarov_Javokhir-Gukesh_D'));
    expect(parseBroadcastLink(`https://www.chess.com/events/${wc}/04`)).toEqual(cc('04', null));
    expect(parseBroadcastLink(`http://schach.chess.com/events/${wc}`)).toEqual(cc(null, null));
    expect(parseBroadcastLink('https://www.chess.com/events/info/2026-xtx-markets-london-chess-classic'))
      .toEqual({ source: 'chesscom', tourId: null, roundId: null, gameId: null });
  });

  it('parseBroadcastLink: keine fremden Wirte, keine chess.com-Seiten außer Events', () => {
    for (const bad of ['https://evil.com/events/x', 'https://chess.com.evil.com/events/x', 'https://www.chess.com/game/live/123456',
      'https://www.chess.com/events', 'https://www.chess.com/events/current', 'https://www.chess.com/events/x/03/Name.With.Dots',
      'ftp://www.chess.com/events/x']) {
      expect(parseBroadcastLink(bad)).withContext(bad).toBeNull();
    }
  });

  it('chess.com-Kennungen: ext-Schlüssel, Runde, Partie, Links, Herkunft', () => {
    const r = 'cc~2026-fide-chess-world-championship~03';
    expect(parseExtParam(`${r}.Sindarov_Javokhir-Gukesh_D,fnmEB1kH.RD8sbYw1,cc~x y~1.a`))
      .toEqual([`${r}.Sindarov_Javokhir-Gukesh_D`, 'fnmEB1kH.RD8sbYw1']);
    expect(extRoundOf(`${r}.Sindarov_Javokhir-Gukesh_D`)).toBe(r);
    expect(isLiveRoundId(r)).toBeTrue();
    expect(isLiveRoundId('fnmEB1kH')).toBeTrue();
    expect(isLiveRoundId('cc~bad slug~03')).toBeFalse();
    expect(isLiveGameIdFor(r, 'Sindarov_Javokhir-Gukesh_D')).toBeTrue();
    expect(isLiveGameIdFor('fnmEB1kH', 'Sindarov_Javokhir-Gukesh_D')).toBeFalse();
    expect(broadcastRoundUrl(r)).toBe('https://www.chess.com/events/2026-fide-chess-world-championship/games');
    expect(broadcastRoundUrl('fnmEB1kH')).toBe('https://lichess.org/broadcast/-/-/fnmEB1kH');
    expect(broadcastHost('https://www.chess.com/events/x')).toBe('chess.com');
    expect(broadcastHost('https://lichess.org/broadcast/-/IjDgZVUR')).toBe('lichess.org');
    expect(broadcastHost(null)).toBe('lichess.org');
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

describe('analysisLinkTarget', () => {
  const media = (on: boolean) => ({ matchMedia: (q: string) => ({ matches: on && q === '(display-mode: standalone)' }) as MediaQueryList });
  it('öffnet in der installierten App (PWA/TWA) im selben Fenster, im Browser in einem neuen Tab', () => {
    expect(analysisLinkTarget(media(false), '')).toBe('_blank');
    expect(analysisLinkTarget(media(false), 'https://turnier.oberschmid.homes/')).toBe('_blank');
    expect(analysisLinkTarget(media(true), '')).toBe('_self');
    expect(analysisLinkTarget(media(false), 'android-app://homes.oberschmid.turnier/')).toBe('_self');
    expect(analysisLinkTarget(null, '')).toBe('_blank');
  });
});

describe('finishedLast / flipDeltas (beendete Partien ans Ende, 1.043.0)', () => {
  const b = (k: string, done: boolean, g = '') => ({ k, done, g });
  const keys = (list: { k: string }[]) => list.map(x => x.k);

  it('laufende vorne in alter Reihenfolge, beendete stabil dahinter', () => {
    const list = [b('a', true), b('b', false), b('c', true), b('d', false), b('e', false)];
    expect(keys(finishedLast(list, x => x.done))).toEqual(['b', 'd', 'e', 'a', 'c']);
    expect(keys(finishedLast([b('a', true), b('b', true)], x => x.done))).toEqual(['a', 'b']);
    expect(finishedLast([], () => true)).toEqual([]);
  });

  it('Gruppen bleiben ganz und an ihrer Stelle; sortiert wird nur innerhalb', () => {
    const list = [b('a1', true, 'A'), b('a2', false, 'A'), b('b1', false, 'B'), b('b2', true, 'B'), b('b3', false, 'B')];
    expect(keys(finishedLast(list, x => x.done, x => x.g))).toEqual(['a2', 'a1', 'b1', 'b3', 'b2']);
  });

  it('flipDeltas: ohne frisch beendete Partie (Erstladen, Rundenwechsel) keine Bewegung', () => {
    const prev = new Map([['a', { x: 0, y: 0 }]]);
    const next = new Map([['a', { x: 0, y: 0 }], ['b', { x: 250, y: 0 }]]);
    expect(flipDeltas(new Map(), next, new Map(), new Map([['a', true], ['b', false]])).size).toBe(0);
    // schon fertig gewesen → kein Wechsel
    expect(flipDeltas(prev, new Map([['a', { x: 250, y: 0 }]]), new Map([['a', true]]), new Map([['a', true]])).size).toBe(0);
    // neue Bretter ohne Vorgänger → kein Wechsel
    expect(flipDeltas(new Map(), next, new Map(), new Map([['a', true]])).size).toBe(0);
  });

  it('flipDeltas: frisch beendet → Versatz alte minus neue Lage für jede bewegte Karte', () => {
    const prev = new Map([['a', { x: 0, y: 0 }], ['b', { x: 250, y: 0 }], ['c', { x: 500, y: 0 }]]);
    const next = new Map([['b', { x: 0, y: 0 }], ['c', { x: 250, y: 0 }], ['a', { x: 500, y: 0 }]]);
    const d = flipDeltas(prev, next, new Map([['a', false], ['b', false], ['c', false]]), new Map([['a', true], ['b', false], ['c', false]]));
    expect(d.get('a')).toEqual({ dx: -500, dy: 0 });
    expect(d.get('b')).toEqual({ dx: 250, dy: 0 });
    expect(d.get('c')).toEqual({ dx: 250, dy: 0 });
  });
});
