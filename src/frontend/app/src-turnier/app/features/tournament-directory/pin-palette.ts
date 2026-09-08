import { DirectoryEntry, TournamentAgeGroup } from './tournament-directory.model';

/**
 * Wonach die Karten-Pins eingefaerbt werden. IMMER nur EINES auf einmal: eine Flaeche hat eine
 * Farbe, und zwei Bedeutungen auf einem Kanal sind keine Auskunft, sondern ein Raetsel.
 *
 * <p>Warum ueberhaupt umschaltbar und nicht fest auf die Bedenkzeit: „wo ist ein
 * Schnellschachturnier" ist die haeufigste dieser Fragen, aber nicht die einzige — „wo spielt der
 * Nachwuchs" ist dieselbe Frage mit einem anderen Merkmal. Ein zweites festes Merkmal braeuchte
 * einen zweiten Kanal, und davon gibt es keinen freien mehr (siehe `pinStyle`).</p>
 *
 * <p><b>Nur EINWERTIGE Merkmale stehen hier.</b> Bedenkzeit, Turnierart und Geschlechtsklasse
 * haben je genau einen Wert. Die ALTERSKLASSEN nicht — ein Turnier traegt oft mehrere
 * gleichzeitig (U8, U10, U12 …), und eine Menge laesst sich nicht als eine Farbe zeichnen.
 * Deshalb steht hier `youth` (Nachwuchs / Senioren / Erwachsene) und nicht die Klassenliste: das
 * ist die Reduktion auf einen Wert, die noch stimmt.</p>
 */
export type PinColourBy = 'speed' | 'kind' | 'youth' | 'gender';

/** Die angebotenen Merkmale in der Reihenfolge der Auswahl. */
export const PIN_COLOUR_SCHEMES: PinColourBy[] = ['speed', 'kind', 'youth', 'gender'];

/** Eine Klasse des gewaehlten Merkmals: Farbe, Rand und der Schluessel ihrer Beschriftung. */
export interface PinCategory {
  /** Der Wert des Merkmals; `mixed` ist der Sonderfall eines gebuendelten Punktes. */
  key: string;
  /** Fuellung des Pins. */
  fill: string;
  /** Rand des Pins — je Fuellung von Hand gewaehlt statt gerechnet, damit er sicher traegt. */
  stroke: string;
  /** i18n-Schluessel der Beschriftung in der Legende. */
  label: string;
  /**
   * Die Zahl im Kopf muss auf HELLEN Fuellungen dunkel sein. Ohne das steht die Anzahl eines
   * gebuendelten Punktes weiss auf hellgrau und ist unlesbar.
   */
  darkLabel?: boolean;
}

/**
 * Ein gebuendelter Punkt, dessen Turniere NICHT in dieselbe Klasse fallen. Er darf keine
 * behaupten — auf der Tiroler Landesmitte liegen fuenf Turniere, und wenn davon eines Blitz ist
 * und vier Turnierschach, waere jede der beiden Farben dort eine Falschaussage.
 */
export const MIXED: PinCategory = {
  key: 'mixed', fill: '#79808a', stroke: '#3c4043',
  label: 'tournamentDirectory.map.legend.mixed',
};

/**
 * Die Farbtafel. Gewaehlt nach Okabe-Ito (blau / gruen / orange / violett / hellblau): diese
 * Toene bleiben auch bei den haeufigen Farbsehschwaechen unterscheidbar. Grau heisst durchgehend
 * „nicht eingeordnet" und ist bewusst der einzige unbunte Ton — es soll nicht wie eine Klasse
 * aussehen.
 */
const Blue = { fill: '#4285f4', stroke: '#1a56c4' };
const Green = { fill: '#00a37a', stroke: '#00654c' };
const Orange = { fill: '#e8710a', stroke: '#a04b00' };
const Purple = { fill: '#cc79a7', stroke: '#8f4f74' };
const SkyBlue = { fill: '#56b4e9', stroke: '#1f78a8' };
const Grey = { fill: '#c8ccd0', stroke: '#9aa0a6', darkLabel: true };

const SCHEMES: Record<PinColourBy, PinCategory[]> = {
  speed: [
    { key: 'Standard', ...Blue, label: 'tournamentDirectory.speed.Standard' },
    { key: 'Rapid', ...Green, label: 'tournamentDirectory.speed.Rapid' },
    { key: 'Blitz', ...Orange, label: 'tournamentDirectory.speed.Blitz' },
    { key: 'Unknown', ...Grey, label: 'tournamentDirectory.speed.Unknown' },
  ],
  kind: [
    { key: 'Individual', ...Blue, label: 'tournamentDirectory.kind.Individual' },
    { key: 'Team', ...Purple, label: 'tournamentDirectory.kind.Team' },
    { key: 'Unknown', ...Grey, label: 'tournamentDirectory.kind.Unknown' },
  ],
  youth: [
    { key: 'youth', ...Green, label: 'tournamentDirectory.map.youth.youth' },
    { key: 'senior', ...Purple, label: 'tournamentDirectory.map.youth.senior' },
    { key: 'adult', ...Blue, label: 'tournamentDirectory.map.youth.adult' },
  ],
  gender: [
    { key: 'Open', ...Blue, label: 'tournamentDirectory.gender.Open' },
    { key: 'Female', ...Purple, label: 'tournamentDirectory.gender.Female' },
    { key: 'Male', ...SkyBlue, label: 'tournamentDirectory.gender.Male' },
  ],
};

/** Die Klassen eines Merkmals in der Reihenfolge der Legende. */
export function legendOf(colourBy: PinColourBy): PinCategory[] {
  return SCHEMES[colourBy];
}

/** Alle Jugend-Merkmale; `Senior` gehoert ausdruecklich NICHT dazu. */
const YouthGroups: TournamentAgeGroup[] =
  ['U8', 'U10', 'U12', 'U14', 'U16', 'U18', 'U20', 'YouthUnspecified'];

/**
 * In welche Klasse des gewaehlten Merkmals ein Turnier faellt.
 *
 * <p>Beim Nachwuchs zaehlt IRGENDEINE Jugendklasse, nicht eine bestimmte: ein Turnier, das U8 bis
 * U18 zusammenfasst, ist Nachwuchsschach, und welche der acht Klassen die Farbe bekaeme, waere
 * eine willkuerliche Wahl. Senioren stehen daneben und nicht darunter — Seniorenschach ist
 * Erwachsenenschach, aber eben eine eigene Klasse.</p>
 */
export function categoryKeyOf(entry: DirectoryEntry, colourBy: PinColourBy): string {
  switch (colourBy) {
    case 'speed': return entry.speed;
    case 'kind': return entry.kind;
    case 'gender': return entry.gender;
    case 'youth':
      if (entry.ageGroups.some(g => YouthGroups.includes(g))) return 'youth';
      return entry.ageGroups.includes('Senior') ? 'senior' : 'adult';
  }
}

/**
 * Die Klasse zu einem Schluessel. Ein unbekannter Schluessel — eine Klasse, die der Server neu
 * vergibt, bevor diese Tafel sie kennt — wird zu `mixed` und damit grau: lieber „keine Aussage"
 * als eine falsche Farbe.
 */
export function categoryOf(colourBy: PinColourBy, key: string): PinCategory {
  return SCHEMES[colourBy].find(c => c.key === key) ?? MIXED;
}

/**
 * Die gemeinsame Klasse mehrerer Turniere auf EINEM Punkt — `mixed`, sobald sie sich
 * unterscheiden.
 */
export function commonCategory(entries: DirectoryEntry[], colourBy: PinColourBy): PinCategory {
  if (entries.length === 0) return MIXED;
  const first = categoryKeyOf(entries[0], colourBy);
  return entries.every(e => categoryKeyOf(e, colourBy) === first)
    ? categoryOf(colourBy, first)
    : MIXED;
}
