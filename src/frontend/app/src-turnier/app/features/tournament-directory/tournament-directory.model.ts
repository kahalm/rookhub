/** Bedenkzeit-Kategorie, die der Server aus dem Bedenkzeit-Freitext ableitet. */
export type TournamentSpeed = 'Unknown' | 'Standard' | 'Rapid' | 'Blitz';

/** Herkunft der Koordinaten — `Region` heisst: nur ungefaehr (Bundesland-Mittelpunkt). */
export type GeoSourceKind = 'None' | 'PostalCode' | 'City' | 'Region' | 'Manual' | 'Nominatim';

export interface DirectoryEntry {
  chessResultsId: string;
  name: string;
  federation: string | null;
  state: string | null;
  startDate: string | null;
  endDate: string | null;
  location: string | null;
  timeControl: string | null;
  speed: TournamentSpeed;
  organizer: string | null;
  director: string | null;
  chiefArbiter: string | null;
  rounds: number | null;
  playerCount: number | null;
  lat: number | null;
  lon: number | null;
  geoSource: GeoSourceKind;
  geoPlaceName: string | null;
  /** Entfernung zum Suchmittelpunkt in km; nur bei einer Umkreissuche gesetzt. */
  distanceKm: number | null;
  cancelled: boolean;
  subscribed: boolean;
  /** Wie viele Gruppen desselben Turniers dieser Eintrag zusammenfasst (1 = einzelnes Turnier). */
  groupSize: number;
  groups: DirectoryGroupMember[];
}

/** Eine Gruppe (A/B/C) innerhalb eines zusammengefassten Turniers. */
export interface DirectoryGroupMember {
  chessResultsId: string;
  /** „A", „Gruppe 2" — leer, wenn chess-results den Zusatz im Namen abgeschnitten hat. */
  label: string;
  playerCount: number | null;
  rounds: number | null;
}

export interface DirectoryPage {
  items: DirectoryEntry[];
  total: number;
  /** true = der Umkreis-Vorfilter lief in seine Obergrenze; Radius verkleinern. */
  truncated: boolean;
}

export interface DirectoryCalendarDay {
  date: string;
  items: DirectoryEntry[];
}

/**
 * So kommt der Monat vom Server: die Turniere EINMAL, die Tage nur mit ihren Nummern. Ein
 * mehrtaegiges Turnier steht an jedem seiner Tage — voll ausgeschrieben waren das auf dem
 * Dev-Server 5962 Eintraege fuer 200 verschiedene Turniere, also 3 MB je Monat. Der Dienst setzt
 * daraus wieder `DirectoryCalendarDay[]` zusammen (dasselbe Objekt an mehreren Tagen, keine Kopie).
 */
export interface DirectoryCalendarResponse {
  tournaments: DirectoryEntry[];
  days: { date: string; ids: string[] }[];
}

export interface SearchProfile {
  id: number;
  name: string;
  placeQuery: string | null;
  lat: number;
  lon: number;
  radiusKm: number;
  federations: string[];
  speeds: string[];
  weekendOnly: boolean;
  minPlayers: number | null;
  notifyNew: boolean;
  sortOrder: number;
}

export type SearchProfileInput = Omit<SearchProfile, 'id'>;

export interface GeoPlaceSuggestion {
  label: string;
  country: string;
  postalCode: string | null;
  lat: number;
  lon: number;
}

/** Filterzustand der Verzeichnisseite — geteilt von Liste, Karte und Kalender. */
export interface DirectoryFilter {
  from: string | null;
  to: string | null;
  lat: number | null;
  lon: number | null;
  radiusKm: number | null;
  federation: string | null;
  speed: TournamentSpeed | null;
  text: string | null;
  weekendOnly: boolean;
  minPlayers: number | null;
  profileId: number | null;
}

/** Benannte Zeitraeume der Filterleiste. `custom` blendet die beiden Datumsfelder ein. */
export type DirectoryRangePreset = 'quarter' | 'halfYear' | 'year' | 'all' | 'custom';

export const DIRECTORY_RANGE_PRESETS: DirectoryRangePreset[] =
  ['quarter', 'halfYear', 'year', 'all', 'custom'];

/**
 * Vorgabe ist das kommende Quartal: ohne Einschraenkung stehen ueber tausend Turniere bis weit
 * ins naechste Jahr in der Liste, und die ersten Bildschirme davon sind nie die interessanten.
 */
export function rangeFor(preset: DirectoryRangePreset, today = new Date()): { from: string | null; to: string | null } {
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const plusMonths = (m: number) => {
    const d = new Date(today);
    d.setMonth(d.getMonth() + m);
    return d;
  };
  switch (preset) {
    case 'quarter': return { from: iso(today), to: iso(plusMonths(3)) };
    case 'halfYear': return { from: iso(today), to: iso(plusMonths(6)) };
    case 'year': return { from: iso(today), to: iso(plusMonths(12)) };
    case 'all': return { from: iso(today), to: null };
    case 'custom': return { from: null, to: null };
  }
}

function pad(value: number): string {
  return value < 10 ? `0${value}` : `${value}`;
}

export const EMPTY_FILTER: DirectoryFilter = {
  from: null, to: null, lat: null, lon: null, radiusKm: null,
  federation: null, speed: null, text: null, weekendOnly: false,
  minPlayers: null, profileId: null,
};
