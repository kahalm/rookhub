/** Bedenkzeit-Kategorie, die der Server aus dem Bedenkzeit-Freitext ableitet. */
export type TournamentSpeed = 'Unknown' | 'Standard' | 'Rapid' | 'Blitz';

/** Herkunft der Koordinaten — `Region` heisst: nur ungefaehr (Bundesland-Mittelpunkt). */
export type GeoSourceKind =
  | 'None' | 'PostalCode' | 'City' | 'Region' | 'Manual' | 'Nominatim'
  /** Ortsname gefunden, aber mehrdeutig — bewusst OHNE Koordinaten (siehe Server). */
  | 'Ambiguous'
  /** Über die Vereinsnamen des Turniers entschieden (Abkürzungs-Fall, siehe Server). */
  | 'TeamHint'
  /** Die Quelle hat die Koordinaten selbst mitgeliefert — genauer geht es nicht. */
  | 'SourceProvided';

/** Einzel- oder Mannschaftsturnier. `Unknown` = noch nicht geklaert, nicht „keins von beidem". */
export type TournamentKind = 'Unknown' | 'Individual' | 'Team';

/** Geschlechtsklasse; `Open` ist der Normalfall. */
export type TournamentGender = 'Open' | 'Female' | 'Male';

/**
 * Alters-/Nachwuchsklassen, wie der Server sie benennt. `YouthUnspecified` = Nachwuchs ohne
 * genannte Klasse („Jugendmeisterschaft", „Schachrallye"), `Senior` ist ausdruecklich KEINE
 * Jugendklasse.
 */
export type TournamentAgeGroup =
  'U8' | 'U10' | 'U12' | 'U14' | 'U16' | 'U18' | 'U20' | 'YouthUnspecified' | 'Senior';

/** Die Klassen, die als Filter-Chips angeboten werden — in der Reihenfolge der Leiste. */
export const DIRECTORY_AGE_GROUPS: TournamentAgeGroup[] =
  ['U8', 'U10', 'U12', 'U14', 'U16', 'U18', 'U20', 'YouthUnspecified', 'Senior'];

export const DIRECTORY_GENDERS: TournamentGender[] = ['Female', 'Male'];

export const DIRECTORY_KINDS: TournamentKind[] = ['Individual', 'Team'];

/**
 * Turniere je Abruf der Liste: am Handy 25 statt 50 (Falz-Runde 16.10.: 50 Karten à ≈ 190 px waren mit offenem Filter
 * 12,8 Bildschirme — DESIGN 4.6 Regel 11); der Rest kommt über „Mehr anzeigen". Einmal beim Öffnen bestimmt.
 */
export const DIRECTORY_PAGE_SIZE = 50;
export const DIRECTORY_PAGE_SIZE_NARROW = 25;
export function directoryPageSize(
  narrow = typeof matchMedia === 'function' && matchMedia('(max-width: 599.98px)').matches,
): number {
  return narrow ? DIRECTORY_PAGE_SIZE_NARROW : DIRECTORY_PAGE_SIZE;
}

/**
 * Angebotene Umkreise in km. Bewusst gestufte Werte statt eines Schiebers: „112 km" ist keine
 * Angabe, die jemand meint, und ein Schieber laedt bei jedem Pixel neu.
 */
export const DIRECTORY_RADII: number[] = [10, 25, 50, 100, 200, 500];

export interface DirectoryEntry {
  /**
   * Die IDENTITAET dieses Turniers — der Wert in der Adresse, beim Ausblenden und beim Melden.
   * Fuer chess-results-Turniere die Nummer dort, fuer FIDE-Turniere `f<Nummer>`.
   */
  id: string;
  /**
   * Die chess-results-Nummer — `null`, wenn das Turnier dort nicht ausgeschrieben ist (der
   * FIDE-Kalender ist die zweite Quelle). Daran haengt, was chess-results BRAUCHT: merken (das
   * Abo traegt die Nummer), holen, Ergebnisse. Ohne sie duerfen diese Knoepfe nicht erscheinen.
   */
  chessResultsId: string | null;
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
  /**
   * ALLE Spielorte, sobald es mehr als einen gibt (Ligen: „Mayrhofen, St.Veit"). Leer bei einem
   * einzelnen Ort — der steht dann in lat/lon/geoPlaceName.
   */
  venues: DirectoryVenue[];
  /** Einzel oder Mannschaft — aus der chess-results-Turnierart, nicht geraten. */
  kind: TournamentKind;
  /** Vom aufrufenden Nutzer ausgeblendet — nur gesetzt, wenn der Filter Ausgeblendete mitanzeigt. */
  ignored: boolean;
  /** Die einzelnen Spieltermine, wenn bekannt. Leer = es gilt der Zeitraum start..ende. */
  roundDates: DirectoryRound[];
  /** Auf welchen Seiten dieses Turnier gefunden wurde. */
  sources: DirectorySource[];
  /** Saisonwettbewerb statt Turnier (abgeleitet). */
  isLeague: boolean;
  /** Aus dem NAMEN gelesene Klassen; leer = offenes Erwachsenenturnier. */
  ageGroups: TournamentAgeGroup[];
  gender: TournamentGender;
  /**
   * Laeuft bereits: begann VOR dem abgefragten Zeitraum (`from` bzw. Monatserster) und ragt hinein.
   * Die Liste fuehrt solche Eintraege als eigenen Block „laeuft bereits" nach denen, die im Zeitraum
   * beginnen — der Server liefert sie in dieser Reihenfolge (Codereview UX-039). Optional, weil eine
   * aeltere API das Feld nicht kennt.
   */
  ongoing?: boolean;
  /**
   * Unplausible Laufzeit laut Quelle (ueber ein Jahr und ohne Spieltermine oder schon ueber ein Jahr
   * vor dem Zeitraum begonnen, etwa „2007-12-26 bis 2026-12-10"). Kommt nur mit dem Schalter
   * `includeImplausible` in die Antwort und steht dann als Hinweis auf der Karte.
   */
  implausible?: boolean;
}

/**
 * Ein Spieltermin — eine Runde mit ihrem Datum. Bei einer Liga sind das elf Tage von September
 * bis April, nicht die 200 dazwischen.
 */
export interface DirectoryRound {
  round: number;
  date: string;
  time: string | null;
}

/** Die Quellarten des Servers (`DirectorySourceKind`, als Name serialisiert). */
export const DIRECTORY_SOURCE_KINDS = [
  'Unknown', 'ChessResults', 'Fide', 'Manual', 'ChessResultsCalendar', 'ItalianChessFederation',
  'SlovenianChessFederation', 'SlovakChessFederation', 'HungarianChessFederation', 'CzechChessFederation',
  'PolishChessFederation', 'GermanChessFederation', 'EnglishChessFederation', 'IrishChessUnion',
  'FrenchChessFederation', 'NorwegianChessFederation',
] as const;
export type DirectorySourceKind = typeof DIRECTORY_SOURCE_KINDS[number];

/** Eine Seite, auf der dieses Turnier gefunden wurde. */
export interface DirectorySource {
  kind: DirectorySourceKind;
  externalId: string;
  url: string | null;
}

/** Ein einzelner Spielort eines Turniers mit mehreren. */
export interface DirectoryVenue {
  name: string;
  lat: number;
  lon: number;
  geoSource: GeoSourceKind;
}

/** Eine Gruppe (A/B/C) innerhalb eines zusammengefassten Turniers. */
export interface DirectoryGroupMember {
  id: string;
  chessResultsId: string | null;
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

/**
 * Die Kartenmarker eines Ausschnitts (`GET /api/tournament-directory/map`). Bis 0.606.0 kam eine
 * nackte Liste: der Server kappt nach Startdatum, und ueber einen grossen Ausschnitt fehlten still
 * die spaeten Monate, waehrend unter der Karte „N Turniere im Ausschnitt" stand.
 */
export interface DirectoryMapResponse {
  items: DirectoryEntry[];
  /** true = der Ausschnitt hat mehr Turniere, als die Karte traegt; es fehlen die spaetesten. */
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

  /** Leer = alles. Sonst nur diese Turnierarten. */
  kinds: TournamentKind[];
  /**
   * Gesuchte Klassen. Der Server prueft UEBERSCHNEIDUNG: „U12" findet auch die
   * „U8-U18"-Meisterschaft, denn genau die ist gemeint.
   */
  ageGroups: TournamentAgeGroup[];
  genders: TournamentGender[];
  /** Nur Turniere ohne JUGENDmerkmal. Seniorenturniere bleiben sichtbar. */
  adultsOnly: boolean;
  hideLeagues: boolean;
  /**
   * Die selbst ausgeblendeten Turniere mitanzeigen. Ohne den Schalter waeren sie
   * unwiederbringlich weg, und niemand wuesste, was er einmal weggeklickt hat.
   */
  includeIgnored: boolean;
  /**
   * Auch Eintraege mit unplausibler Laufzeit zeigen. Vorgabe ist AUS: auf DEV standen sonst Dutzende
   * „2007 bis 2026"-Eintraege vor jedem echten Turnier (Codereview UX-039).
   */
  includeImplausible: boolean;
}

/** Benannte Zeitraeume der Filterleiste. `custom` blendet die beiden Datumsfelder ein. */
export type DirectoryRangePreset = 'quarter' | 'halfYear' | 'year' | 'all' | 'custom';

export const DIRECTORY_RANGE_PRESETS: DirectoryRangePreset[] =
  ['quarter', 'halfYear', 'year', 'all', 'custom'];

/**
 * Vorgabe sind die naechsten drei Monate ab heute (Kennung `quarter`, beschriftet „Naechste drei
 * Monate" — „Naechstes Quartal" las sich wie Q4, gemeint war nie das Kalenderquartal; die Kennung
 * bleibt, weil sie in gemerkten Ansichten steht): ohne Einschraenkung stehen ueber tausend Turniere bis weit
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
  kinds: [], ageGroups: [], genders: [], adultsOnly: false, hideLeagues: false,
  includeIgnored: false, includeImplausible: false,
};

/**
 * Was in der Umkreis-Auswahl steht, wenn kein Ort gewaehlt ist. Der Vorgabewert greift erst,
 * sobald ein Ort da ist — vorher waere ein Radius ohne Mittelpunkt eine Angabe ohne Wirkung.
 */
export const DEFAULT_RADIUS_KM = 100;

/**
 * „Falsches Event melden". JEDES Feld ist freiwillig — wer nur auf den Knopf drueckt, meldet
 * „hier stimmt etwas nicht", und auch das ist brauchbar.
 *
 * Strukturierte Vorschlagsfelder (Ort, Art, Klasse, Bedenkzeit, Liga) standen hier einmal und
 * sind wieder weg: sie verlangten genau die Wertetabelle, die ein Melder nicht kennen muss.
 *
 * `namePattern` ist die wertvollste Frage des Formulars: Alter und Publikum stehen nur im Namen,
 * und diese Namen sind REGIONAL („Schachrallye" ist in Tirol immer Nachwuchs). Wer eine solche
 * Kennung einmal nennt, verbessert die Einordnung aller kuenftigen Ausgaben derselben Reihe.
 */
export interface DirectoryReport {
  /** Was aus Sicht des Melders falsch ist — Freitext, freiwillig. */
  message: string | null;
  namePattern: string | null;
  sourceLink: string | null;
}

/**
 * Platzhalter statt eines Spielorts. chess-results fuehrt bei Ligen oft „various" (englisch) als Ort — kein Ort,
 * und auf der deutschen Seite mit Ortsnadel wirkte es wie ein Uebersetzungsfehler. Solche Werte zeigen Karte,
 * Kartenblase und Kalender als uebersetztes „verschiedene Spielorte" (UI-Sweep 2026-10-11, r2-t-various).
 */
const PLACEHOLDER_LOCATIONS: ReadonlySet<string> = new Set([
  'various', 'various venues', 'various places', 'div', 'diverse', 'diverses',
  'verschiedene', 'verschiedene orte', 'verschiedene spielorte',
]);

/** Steht statt eines Orts nur ein Platzhalter („various", „div.", „diverse", „verschiedene")? */
export function isPlaceholderLocation(location: string | null | undefined): boolean {
  const key = (location ?? '').trim().toLowerCase().replace(/\s+/g, ' ').replace(/[.\s]+$/, '');
  return key.length > 0 && PLACEHOLDER_LOCATIONS.has(key);
}

/** Der Ort zur Anzeige: Platzhalter werden zum uebersetzten „verschiedene Spielorte", sonst der Text selbst. */
export function displayLocation(location: string | null | undefined, translate: (key: string) => string): string {
  if (!location) return '';
  return isPlaceholderLocation(location) ? translate('tournamentDirectory.variousVenues') : location;
}

/**
 * Gleichnamige Turniere am selben Tag, die NICHT zu einer Karte zusammengefasst sind (UI-Sweep 2026-10-11,
 * r2-t-dup-cards). Liefert die Ids der Eintraege, die einen solchen Doppelgaenger in derselben Liste haben.
 *
 * Seit derselben Runde fasst der Server ueber den VERORTETEN Ort zusammen (`TournamentDirectoryService.GroupPlace`),
 * nicht mehr ueber den rohen Ortstext — „…Otáhalova 1260 40747" und „Varnsdorf Otáhalova 1260" sind damit eine Karte.
 * Der Hinweis bleibt als RUECKFALL fuer das, was der Server bewusst getrennt laesst: nur eine Gruppe verortet, beide
 * nur auf Bundesland-Ebene oder mehrdeutig verortet und verschieden geschrieben, Tippfehler im Ort („Cebu"/„Cebi").
 * Auf Prod (2026-10-11) waren das noch ~28 Turniere ab Oktober. Er erscheint nur, wo die Liste wirklich zwei gleich
 * aussehende Karten zeigt.
 */
export function sameNameSiblings(entries: readonly DirectoryEntry[]): Map<string, SameNameMark> {
  const result = new Map<string, SameNameMark>();
  for (const ids of sameNameGroups(entries).values()) {
    if (ids.length > 1) ids.forEach((id, i) => result.set(id, { index: i + 1, total: ids.length }));
  }
  return result;
}

/** Stelle einer Karte unter ihren Doppelgängern: „1 von 2 gleichen Namens" (z2-t-cal-gleichnamig). */
export interface SameNameMark { index: number; total: number }

function sameNameKey(entry: DirectoryEntry): string {
  return `${entry.name.trim().toLowerCase()}|${entry.startDate ?? ''}`;
}

/** Ids je Schlüssel Name + Starttag, in der Reihenfolge des ersten Vorkommens. */
function sameNameGroups(entries: readonly DirectoryEntry[]): Map<string, string[]> {
  const byKey = new Map<string, string[]>();
  for (const entry of entries) {
    const key = sameNameKey(entry);
    const ids = byKey.get(key);
    if (ids) ids.push(entry.id); else byKey.set(key, [entry.id]);
  }
  return byKey;
}

/**
 * Gleichnamige Einträge desselben Tags direkt hintereinander (z2-t-cal-gleichnamig: die Doppelkarte stand zwölf
 * Karten weiter unten, der Hinweis sagte „daneben"). Stabil: jede Gruppe steht dort, wo ihr erstes Mitglied stand;
 * alles andere behält seine Reihenfolge.
 */
export function groupSameName<T extends DirectoryEntry>(entries: readonly T[]): T[] {
  const byKey = new Map<string, T[]>();
  for (const entry of entries) {
    const key = sameNameKey(entry);
    const group = byKey.get(key);
    if (group) group.push(entry); else byKey.set(key, [entry]);
  }
  return [...byKey.values()].flat();
}
