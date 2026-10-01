import { TournamentPlayer, TournamentTeam, DisplayPairing } from '@rh/core/models';
import {
  computeFavoriteNames,
  filterPlayersByFavorites,
  filterTeamsByFavorites,
  filterPairingsByFavorites,
  displayedTables,
  TournamentTablesState,
} from './tournament-favorites.util';

const players = [
  { snr: 1, name: 'Alice', teamName: 'Red' },
  { snr: 2, name: 'Bob', teamName: 'Red' },
  { snr: 3, name: 'Carol', teamName: 'Blue' },
  { snr: 4, name: 'Dave', teamName: undefined },
] as unknown as TournamentPlayer[];

const teams = [
  { snr: 10, name: 'Red' },
  { snr: 11, name: 'Blue' },
] as unknown as TournamentTeam[];

describe('tournament-favorites.util', () => {
  describe('computeFavoriteNames', () => {
    it('ein favorisierter Spieler zieht seinen Teamnamen mit hinein', () => {
      const { playerNames, teamNames } = computeFavoriteNames(players, teams, new Set([1]), new Set());
      // Alice (fav) + Teamkollege Bob über Team "Red"
      expect(teamNames.has('Red')).toBeTrue();
      expect(playerNames.has('Alice')).toBeTrue();
      expect(playerNames.has('Bob')).toBeTrue();
      expect(playerNames.has('Carol')).toBeFalse();
    });

    it('ein favorisiertes Team erfasst alle seine Spieler', () => {
      const { playerNames, teamNames } = computeFavoriteNames(players, teams, new Set(), new Set([11]));
      expect(teamNames.has('Blue')).toBeTrue();
      expect(playerNames.has('Carol')).toBeTrue();
      expect(playerNames.has('Alice')).toBeFalse();
    });

    it('ohne Favoriten sind beide Mengen leer', () => {
      const { playerNames, teamNames } = computeFavoriteNames(players, teams, new Set(), new Set());
      expect(playerNames.size).toBe(0);
      expect(teamNames.size).toBe(0);
    });
  });

  describe('filter*ByFavorites', () => {
    it('filtert Spieler nach Snr oder Team-Name', () => {
      const result = filterPlayersByFavorites(players, new Set([4]), new Set(['Red']));
      expect(result.map(p => p.name).sort()).toEqual(['Alice', 'Bob', 'Dave']);
    });

    it('filtert Teams nach Name', () => {
      expect(filterTeamsByFavorites(teams, new Set(['Blue'])).map(t => t.name)).toEqual(['Blue']);
    });

    it('filtert Einzel-Paarungen über Spielernamen', () => {
      const pairings = [
        { board: 1, white: 'Alice', black: 'Carol', result: '1-0' },
        { board: 2, white: 'Eve', black: 'Frank', result: '0-1' },
      ] as DisplayPairing[];
      const result = filterPairingsByFavorites(pairings, false, new Set(['Alice']), new Set());
      expect(result.length).toBe(1);
      expect(result[0].white).toBe('Alice');
    });

    it('filtert Team-Paarungen über Teamnamen (hasTeamPairings=true)', () => {
      const pairings = [
        { board: 1, white: 'Red', black: 'Blue', result: '3 : 1' },
        { board: 2, white: 'Green', black: 'Yellow', result: '2 : 2' },
      ] as DisplayPairing[];
      const result = filterPairingsByFavorites(pairings, true, new Set(['Alice']), new Set(['Red']));
      expect(result.length).toBe(1);
      expect(result[0].white).toBe('Red');
    });
  });

  // W5 F6-024: beide Turnier-Ansichten filtern + sortieren über diese eine Funktion.
  describe('displayedTables', () => {
    const noSort = { active: '', direction: '' as const };
    const pairings = [
      { board: 1, white: 'Carol', black: 'Dave', result: '1-0' },
      { board: 2, white: 'Alice', black: 'Eve', result: '0-1' },
    ] as DisplayPairing[];
    const state = (over: Partial<TournamentTablesState> = {}): TournamentTablesState => ({
      players, teams, pairings, hasTeamPairings: false,
      favoriteSnrs: new Set<number>(), favoriteTeamSnrs: new Set<number>(), showFavoritesOnly: false,
      playerSort: noSort, teamSort: noSort, pairingSort: noSort, ...over,
    });

    it('ohne Filter und Sortierung bleiben es dieselben Arrays', () => {
      const d = displayedTables(state());
      expect(d.players).toBe(players);
      expect(d.teams).toBe(teams);
      expect(d.pairings).toBe(pairings);
    });

    it('nur Favoriten: ein favorisierter Spieler bringt Team und Teamkollegen mit, Paarungen nach Spielernamen', () => {
      const d = displayedTables(state({ favoriteSnrs: new Set([1]), showFavoritesOnly: true }));
      expect(d.players.map(p => p.name)).toEqual(['Alice', 'Bob']);
      expect(d.teams.map(t => t.name)).toEqual(['Red']);
      expect(d.pairings.map(p => p.board)).toEqual([2]);
    });

    it('sortiert nach dem Filtern je Tabelle', () => {
      const d = displayedTables(state({
        favoriteTeamSnrs: new Set([10, 11]), showFavoritesOnly: true,
        playerSort: { active: 'name', direction: 'desc' }, teamSort: { active: 'name', direction: 'asc' },
      }));
      expect(d.players.map(p => p.name)).toEqual(['Carol', 'Bob', 'Alice']);
      expect(d.teams.map(t => t.name)).toEqual(['Blue', 'Red']);
    });
  });
});
