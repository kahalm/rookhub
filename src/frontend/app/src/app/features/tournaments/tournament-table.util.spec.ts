import { sortTableData, toDisplayPairings, PLAYER_COLUMNS } from './tournament-table.util';

describe('tournament-table.util', () => {
  describe('sortTableData', () => {
    it('gibt die Daten unverändert zurück ohne aktive Sortierung', () => {
      const data = [{ snr: 2 }, { snr: 1 }];
      expect(sortTableData(data, { active: '', direction: '' })).toBe(data);
      expect(sortTableData(data, { active: 'snr', direction: '' })).toBe(data);
    });

    it('sortiert numerisch aufsteigend/absteigend', () => {
      const data = [{ snr: 3 }, { snr: 1 }, { snr: 2 }];
      expect(sortTableData(data, { active: 'snr', direction: 'asc' }).map(x => x.snr)).toEqual([1, 2, 3]);
      expect(sortTableData(data, { active: 'snr', direction: 'desc' }).map(x => x.snr)).toEqual([3, 2, 1]);
    });

    it('sortiert Strings via localeCompare', () => {
      const data = [{ name: 'Müller' }, { name: 'Bauer' }, { name: 'Zander' }];
      expect(sortTableData(data, { active: 'name', direction: 'asc' }).map(x => x.name)).toEqual(['Bauer', 'Müller', 'Zander']);
    });

    it('mappt die Sort-Keys team→teamName und board→boardNumber', () => {
      const players = [{ teamName: 'B', boardNumber: 2 }, { teamName: 'A', boardNumber: 1 }];
      expect(sortTableData(players, { active: 'team', direction: 'asc' })[0].teamName).toBe('A');
      expect(sortTableData(players, { active: 'board', direction: 'desc' })[0].boardNumber).toBe(2);
    });

    it('verändert das Eingabe-Array nicht (Kopie)', () => {
      const data = [{ snr: 2 }, { snr: 1 }];
      sortTableData(data, { active: 'snr', direction: 'asc' });
      expect(data.map(x => x.snr)).toEqual([2, 1]);
    });
  });

  describe('toDisplayPairings', () => {
    it('erkennt Team-Paarungen (homeTeam) und formatiert den Score', () => {
      const raw = [{ matchNumber: 1, homeTeam: 'A', awayTeam: 'B', homeScore: 3, awayScore: 1 }];
      const { pairings, hasTeamPairings } = toDisplayPairings(raw);
      expect(hasTeamPairings).toBeTrue();
      expect(pairings[0]).toEqual({ board: 1, white: 'A', black: 'B', result: '3 : 1' });
    });

    it('lässt den Team-Score leer wenn homeScore null ist', () => {
      const raw = [{ matchNumber: 2, homeTeam: 'A', awayTeam: 'B', homeScore: null, awayScore: null }];
      expect(toDisplayPairings(raw).pairings[0].result).toBe('');
    });

    it('erkennt Einzel-Paarungen (boardNumber)', () => {
      const raw = [{ boardNumber: 1, white: 'P1', black: 'P2', result: '1-0' }];
      const { pairings, hasTeamPairings } = toDisplayPairings(raw);
      expect(hasTeamPairings).toBeFalse();
      expect(pairings[0]).toEqual({ board: 1, white: 'P1', black: 'P2', result: '1-0' });
    });

    it('leere Antwort → keine Team-Paarungen, leere Liste', () => {
      expect(toDisplayPairings([])).toEqual({ pairings: [], hasTeamPairings: false });
    });
  });

  it('Spaltendefinitionen sind stabil', () => {
    expect(PLAYER_COLUMNS).toContain('fav');
    expect(PLAYER_COLUMNS[0]).toBe('fav');
  });
});
