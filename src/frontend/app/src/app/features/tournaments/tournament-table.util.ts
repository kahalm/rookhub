import { Sort } from '@angular/material/sort';
import { DisplayPairing } from '../../core/models';

/**
 * Geteilte Tabellen-Logik für die Turnier-Ansichten (authentifiziert =
 * tournament-detail, öffentlich = public-tournament). Reine Funktionen/Konstanten
 * ohne Komponenten-State — die unterschiedliche Daten-/Favoriten-Haltung der beiden
 * Komponenten (gecachte Felder vs. Getter, Server vs. localStorage) bleibt dort.
 */

export const PLAYER_COLUMNS = ['fav', 'snr', 'title', 'name', 'fideId', 'elo', 'country', 'team', 'board'];
export const TEAM_COLUMNS = ['fav', 'rank', 'name', 'points'];
export const PAIRING_COLUMNS = ['board', 'white', 'result', 'black'];

/** Generisches Sortieren nach `sort.active`/`sort.direction`; ohne aktive Sortierung unverändert. */
export function sortTableData<T>(data: T[], sort: Sort): T[] {
  if (!sort.active || sort.direction === '') return data;
  const dir = sort.direction === 'asc' ? 1 : -1;
  const key = sort.active === 'team' ? 'teamName' : sort.active === 'board' ? 'boardNumber' : sort.active;
  return [...data].sort((a: any, b: any) => {
    const valA = a[key] ?? '';
    const valB = b[key] ?? '';
    if (typeof valA === 'number' && typeof valB === 'number') return (valA - valB) * dir;
    return String(valA).localeCompare(String(valB)) * dir;
  });
}

/**
 * Wandelt die Crawler-Paarungs-Antwort in {@link DisplayPairing}[] um. Das Format
 * ist je Turniertyp unterschiedlich: Team-Paarungen tragen `homeTeam`, Einzel-
 * Paarungen `boardNumber`/`white`/`black`. `hasTeamPairings` meldet den erkannten Typ.
 */
export function toDisplayPairings(raw: any[]): { pairings: DisplayPairing[]; hasTeamPairings: boolean } {
  if (raw.length > 0 && raw[0].homeTeam !== undefined) {
    return {
      hasTeamPairings: true,
      pairings: raw.map((item): DisplayPairing => ({
        board: item.matchNumber,
        white: item.homeTeam,
        black: item.awayTeam,
        result: item.homeScore != null ? `${item.homeScore} : ${item.awayScore}` : '',
      })),
    };
  }
  return {
    hasTeamPairings: false,
    pairings: raw.map((item): DisplayPairing => ({
      board: item.boardNumber,
      white: item.white,
      black: item.black,
      result: item.result ?? '',
    })),
  };
}
