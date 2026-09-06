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

export const EMPTY_FILTER: DirectoryFilter = {
  from: null, to: null, lat: null, lon: null, radiusKm: null,
  federation: null, speed: null, text: null, weekendOnly: false,
  minPlayers: null, profileId: null,
};
