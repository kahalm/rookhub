import { Sort } from '@angular/material/sort';
import { DisplayPairing } from '@rh/core/models';

/**
 * Geteilte Tabellen-Logik für die Turnier-Ansichten (authentifiziert =
 * tournament-detail, öffentlich = public-tournament). Reine Funktionen/Konstanten
 * ohne Komponenten-State — die unterschiedliche Favoriten-Ablage der beiden
 * Komponenten (Server vs. localStorage) bleibt dort; Filtern + Sortieren für beide:
 * `displayedTables` in tournament-favorites.util.
 */

export const PLAYER_COLUMNS = ['fav', 'snr', 'title', 'name', 'fideId', 'elo', 'country', 'team', 'board'];
export const TEAM_COLUMNS = ['fav', 'rank', 'name', 'points'];
export const PAIRING_COLUMNS = ['board', 'white', 'result', 'black'];

/** Die Titel, die die Titelspalte zeigt (FIDE-Titel, offen und Frauen). */
export const CHESS_TITLES: ReadonlySet<string> = new Set(['GM', 'IM', 'FM', 'CM', 'WGM', 'WIM', 'WFM', 'WCM']);

/**
 * Der Titel eines Spielers, wenn es ein echter ist — sonst `null`. In manchen Startlisten (gemessen an einer
 * slowakischen Extraliga) steht im Titelfeld etwas anderes, z. B. „Z56", „Z41" oder „N"; das zeigte die Spalte
 * „Titel" und am Handy fett vor dem Namen (UI-Sweep 2026-10-10, t-title-col). Die Zuordnung im Crawler bleibt,
 * die Anzeige filtert.
 */
export function chessTitle(raw: string | null | undefined): string | null {
  const t = (raw ?? '').trim().toUpperCase();
  return CHESS_TITLES.has(t) ? t : null;
}

/** Generisches Sortieren nach `sort.active`/`sort.direction`; ohne aktive Sortierung unverändert. */
export function sortTableData<T>(data: T[], sort: Sort): T[] {
  if (!sort.active || sort.direction === '') return data;
  const dir = sort.direction === 'asc' ? 1 : -1;
  const key = sort.active === 'team' ? 'teamName' : sort.active === 'board' ? 'boardNumber' : sort.active;
  // Die Vereinsspalte zeigt ohne eigenen Verein den uebernommenen — sortiert wird nach dem GEZEIGTEN.
  // Ebenso der Titel: nur echte Titel zaehlen, der Rest sortiert wie „ohne Titel".
  const value = (row: any) => (key === 'teamName' ? row.teamName ?? row.club
    : key === 'title' ? chessTitle(row.title) : row[key]) ?? '';
  return [...data].sort((a: any, b: any) => {
    const valA = value(a);
    const valB = value(b);
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
