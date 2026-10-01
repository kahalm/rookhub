import { boardSourcesText, onlineSourcesText, thousands } from './game-sources';
import { GameSources } from './league.models';

const S: GameSources = {
  board: [
    { key: 'Lumbra', label: 'Lumbra', games: 34838 },
    { key: 'Mega', label: 'ChessBase-Megabase', games: 18839 },
    { key: 'chess-results', label: 'chess-results', games: 4603 },
    { key: 'Lichess-Übertragung', label: 'Lichess-Übertragungen', games: 212 },
    { key: 'Verein', label: 'Vereins-Datenbank', games: 54 },
    { key: 'Eigene', label: 'Eigene', games: 3 },
  ],
  boardTotal: 58549,
  online: [{ key: 'lichess', label: 'Lichess', games: 667881 }, { key: 'chess.com', label: 'chess.com', games: 29528 }],
  onlineTotal: 697409,
  countedAt: '2026-10-01T13:00:00Z',
};

describe('game-sources', () => {
  it('Tausenderpunkt fest, ohne Rundung', () => {
    expect(thousands(0)).toBe('0');
    expect(thousands(999)).toBe('999');
    expect(thousands(1000)).toBe('1.000');
    expect(thousands(697409)).toBe('697.409');
    expect(thousands(1234567)).toBe('1.234.567');
  });

  it('Brett und online als Satz, je Quelle mit passendem Wort; unbekannte „aus <Name>"', () => {
    expect(boardSourcesText(S)).toBe('58.549 Brettpartien: 34.838 aus Lumbra, 18.839 aus der ChessBase-Megabase, 4.603 von chess-results, '
      + '212 aus Lichess-Übertragungen, 54 aus der Vereins-Datenbank, 3 aus Eigene');
    expect(onlineSourcesText(S)).toBe('697.409 Online-Partien: 667.881 von Lichess, 29.528 von chess.com');
    expect(onlineSourcesText({ ...S, online: [], onlineTotal: 0 })).toBe('');
  });
});
