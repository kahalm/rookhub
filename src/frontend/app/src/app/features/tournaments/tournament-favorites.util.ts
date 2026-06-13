import { TournamentPlayer, TournamentTeam, DisplayPairing } from '../../core/models';

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
