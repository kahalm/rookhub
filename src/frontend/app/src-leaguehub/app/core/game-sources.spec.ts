import { sourceGroups, thousands } from './game-sources';
import { GameSources } from './league.models';

const S: GameSources = {
  board: [
    { key: 'Lumbra', label: 'Lumbra', games: 34838 },
    { key: 'Mega', label: 'ChessBase-Megabase', games: 18839 },
    { key: 'chess-results', label: 'chess-results', games: 4603 },
    { key: 'Verein', label: 'Vereins-Datenbank', games: 54 },
  ],
  boardTotal: 58334,
  online: [{ key: 'lichess', label: 'Lichess', games: 667562 }, { key: 'chess.com', label: 'chess.com', games: 29522 }],
  onlineTotal: 697084,
  countedAt: '2026-10-01T13:00:00Z',
};

describe('game-sources', () => {
  it('Tausenderpunkt fest, ohne Rundung', () => {
    expect(thousands(0)).toBe('0');
    expect(thousands(999)).toBe('999');
    expect(thousands(1000)).toBe('1.000');
    expect(thousands(697084)).toBe('697.084');
    expect(thousands(1234567)).toBe('1.234.567');
  });

  it('Gruppen mit Summe, Anteil und größter Quelle — ohne Liga und Begegnung leer', () => {
    const [brett, online] = sourceGroups(S);
    expect([brett.title, brett.games, brett.league, brett.opp]).toEqual(['Brett', '58.334', '', '']);
    expect(brett.rows.map(r => [r.label, r.games, r.share, r.top])).toEqual([
      ['Lumbra', '34.838', 59.7, true], ['ChessBase-Megabase', '18.839', 32.3, false],
      ['chess-results', '4.603', 7.9, false], ['Vereins-Datenbank', '54', 0.1, false],
    ]);
    expect([online.title, online.games]).toEqual(['Online', '697.084']);
    expect(online.rows.map(r => [r.label, r.share, r.top])).toEqual([['Lichess', 95.8, true], ['chess.com', 4.2, false]]);
    expect(sourceGroups({ ...S, online: [], onlineTotal: 0 }).map(g => g.title)).toEqual(['Brett']);
  });

  it('Liga und Begegnung: fehlende Brett-Quelle = 0, Seite ohne Konto = „–“, ohne jedes Konto auch die Summe „–“', () => {
    const withLeague = sourceGroups({ ...S, league: {
      players: 177, board: { Lumbra: 29982, Mega: 12311 }, boardTotal: 42293,
      online: { lichess: { games: 345890, accounts: 54 }, 'chess.com': { games: 22288, accounts: 30 } }, onlineTotal: 368178, onlineAccounts: 84,
    } });
    expect([withLeague[0].league, ...withLeague[0].rows.map(r => r.league)]).toEqual(['42.293', '29.982', '12.311', '0', '0']);
    expect([withLeague[1].league, ...withLeague[1].rows.map(r => r.league)]).toEqual(['368.178', '345.890', '22.288']);
    expect(withLeague[0].opp).toBe('');
    const withOpp: GameSources = { ...S, opponent: {
      players: 11, board: { Lumbra: 187, Mega: 44, 'chess-results': 10, Verein: 1 }, boardTotal: 242,
      online: { lichess: { games: 120, accounts: 1 } }, onlineTotal: 120, onlineAccounts: 1,
    } };
    const [brett, online] = sourceGroups(withOpp);
    expect(brett.opp).toBe('242');
    expect(brett.rows.map(r => r.opp)).toEqual(['187', '44', '10', '1']);
    expect([online.opp, ...online.rows.map(r => r.opp)]).toEqual(['120', '120', '–']);
    const noAccounts = sourceGroups({ ...withOpp, opponent: { ...withOpp.opponent!, online: {}, onlineTotal: 0, onlineAccounts: 0 } })[1];
    expect([noAccounts.opp, ...noAccounts.rows.map(r => r.opp)]).toEqual(['–', '–', '–']);
    expect(sourceGroups({ ...withOpp, opponent: { ...withOpp.opponent!, board: {} } })[0].rows.map(r => r.opp)).toEqual(['0', '0', '0', '0']);
  });
});
