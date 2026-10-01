import { Sort } from '@angular/material/sort';
import { TournamentPlayer, TournamentTeam, DisplayPairing } from '@rh/core/models';
import { sortTableData } from './tournament-table.util';

/** Aufgelöste Favoriten-Namen (für die „nur Favoriten"-Filterung der Tabellen). */
export interface FavoriteNames {
  playerNames: Set<string>;
  teamNames: Set<string>;
}

/**
 * Leitet aus den favorisierten Snr (Spieler + Team) die Namensmengen ab:
 * - ein Team ist favorisiert, wenn seine Snr favorisiert ist ODER ein favorisierter Spieler dazugehört
 * - ein Spieler gilt als favorisiert, wenn seine Snr favorisiert ist ODER sein Team favorisiert ist
 */
export function computeFavoriteNames(
  players: TournamentPlayer[],
  teams: TournamentTeam[],
  favoritePlayerSnrs: Set<number>,
  favoriteTeamSnrs: Set<number>,
): FavoriteNames {
  const teamNames = new Set<string>();
  for (const t of teams) {
    if (favoriteTeamSnrs.has(t.snr)) teamNames.add(t.name);
  }
  for (const p of players) {
    if (favoritePlayerSnrs.has(p.snr) && p.teamName) teamNames.add(p.teamName);
  }

  const playerNames = new Set<string>();
  for (const p of players) {
    if (favoritePlayerSnrs.has(p.snr) || (p.teamName && teamNames.has(p.teamName))) {
      playerNames.add(p.name);
    }
  }
  return { playerNames, teamNames };
}

export function filterPlayersByFavorites(
  players: TournamentPlayer[],
  favoritePlayerSnrs: Set<number>,
  favoriteTeamNames: Set<string>,
): TournamentPlayer[] {
  return players.filter(p => favoritePlayerSnrs.has(p.snr) || (!!p.teamName && favoriteTeamNames.has(p.teamName)));
}

export function filterTeamsByFavorites(
  teams: TournamentTeam[],
  favoriteTeamNames: Set<string>,
): TournamentTeam[] {
  return teams.filter(t => favoriteTeamNames.has(t.name));
}

export function filterPairingsByFavorites(
  pairings: DisplayPairing[],
  hasTeamPairings: boolean,
  favoritePlayerNames: Set<string>,
  favoriteTeamNames: Set<string>,
): DisplayPairing[] {
  const names = hasTeamPairings ? favoriteTeamNames : favoritePlayerNames;
  return pairings.filter(p => names.has(p.white) || names.has(p.black));
}

/** Was die drei Turnier-Tabellen zum Anzeigen brauchen: Daten, Favoriten, Filter, Sortierung. */
export interface TournamentTablesState {
  players: TournamentPlayer[];
  teams: TournamentTeam[];
  pairings: DisplayPairing[];
  hasTeamPairings: boolean;
  favoriteSnrs: Set<number>;
  favoriteTeamSnrs: Set<number>;
  showFavoritesOnly: boolean;
  playerSort: Sort;
  teamSort: Sort;
  pairingSort: Sort;
}

export interface DisplayedTables {
  players: TournamentPlayer[];
  teams: TournamentTeam[];
  pairings: DisplayPairing[];
}

/**
 * Die angezeigten Zeilen aller drei Tabellen — bei „nur Favoriten" gefiltert, dann sortiert. Beide
 * Turnier-Ansichten (angemeldet und öffentlich) rufen das nach JEDER Änderung auf und halten das
 * Ergebnis in Feldern: als Getter bekäme mat-table je Änderungslauf ein neues Array. Immer alle drei,
 * weil die Favoriten-Namen von Spielern UND Teams abhängen — kommen die Teams vor den Spielern, fehlte
 * sonst das Team eines favorisierten Spielers in der Teamliste.
 */
export function displayedTables(s: TournamentTablesState): DisplayedTables {
  let { players, teams, pairings } = s;
  if (s.showFavoritesOnly) {
    const { playerNames, teamNames } = computeFavoriteNames(s.players, s.teams, s.favoriteSnrs, s.favoriteTeamSnrs);
    players = filterPlayersByFavorites(players, s.favoriteSnrs, teamNames);
    teams = filterTeamsByFavorites(teams, teamNames);
    pairings = filterPairingsByFavorites(pairings, s.hasTeamPairings, playerNames, teamNames);
  }
  return {
    players: sortTableData(players, s.playerSort),
    teams: sortTableData(teams, s.teamSort),
    pairings: sortTableData(pairings, s.pairingSort),
  };
}
