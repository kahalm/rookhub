import { Contact, Member, MemberInput, MemberRow, Status } from './club.models';

/** Reine Regeln der ClubHub-Oberfläche (ohne Angular) — Altersklasse, Datumseingabe, Trainingstag, Telefonlink. */

const AGE_CLASSES = [8, 10, 12, 14, 16, 18];

/**
 * Altersklasse im Jugendschach: „U10" spielt, wer im laufenden Jahr höchstens 10 wird — es zählt der JAHRGANG, nicht der
 * Geburtstag. `null` ohne Jahrgang oder über 18.
 */
export function ageClass(birthYear: number | null | undefined, year: number): string | null {
  if (birthYear == null) return null;
  const age = year - birthYear;
  if (age < 0) return null;
  const cls = AGE_CLASSES.find(limit => age <= limit);
  return cls ? `U${cls}` : null;
}

export interface Birth {
  birthDate: string | null;
  birthYear: number | null;
}

/**
 * Die Eingabe „Geburtsdatum oder Jahrgang": leer, ein Jahr („2015"), ein Datum wie man es hier schreibt („12.3.2015")
 * oder ISO. `null` = so nicht lesbar (auch ein Datum, das es nicht gibt: 31.2.).
 */
export function parseBirth(text: string): Birth | null {
  const t = text.trim();
  if (!t) return { birthDate: null, birthYear: null };
  if (/^\d{4}$/.test(t)) return { birthDate: null, birthYear: Number(t) };
  const de = /^(\d{1,2})\.\s*(\d{1,2})\.\s*(\d{4})$/.exec(t);
  const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(t);
  const [y, m, d] = de ? [Number(de[3]), Number(de[2]), Number(de[1])] : iso ? [Number(iso[1]), Number(iso[2]), Number(iso[3])] : [0, 0, 0];
  if (!y) return null;
  const date = new Date(Date.UTC(y, m - 1, d));
  if (date.getUTCFullYear() !== y || date.getUTCMonth() !== m - 1 || date.getUTCDate() !== d) return null;
  return { birthDate: `${y}-${pad(m)}-${pad(d)}`, birthYear: y };
}

/** Wie `parseBirth` es liest, so steht es im Feld: „12.03.2015", sonst der Jahrgang, sonst leer. */
export function formatBirth(m: { birthDate?: string | null; birthYear?: number | null }): string {
  if (m.birthDate) {
    const [y, mo, d] = m.birthDate.split('-');
    return `${d}.${mo}.${y}`;
  }
  return m.birthYear != null ? String(m.birthYear) : '';
}

/** `tel:`-Adresse: nur Ziffern und ein führendes Plus — „0512/58 12 34" wählt sonst nicht jedes Telefon. */
export function telHref(value: string): string {
  const t = value.trim();
  return 'tel:' + (t.startsWith('+') ? '+' : '') + t.replace(/\D/g, '');
}

export function firstPhone(contacts: Contact[]): Contact | null {
  return contacts.find(c => c.kind === 'phone') ?? null;
}

export const WEEKDAYS = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const WEEKDAYS_SHORT = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];
const MONTHS = ['Jänner', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

/** 1 = Montag … 7 = Sonntag → Name; leer ohne festen Tag. */
export function weekdayName(weekday: number | null | undefined): string {
  return weekday != null && weekday >= 1 && weekday <= 7 ? WEEKDAYS[weekday - 1] : '';
}

/** ISO-Wochentag eines Datums (Ortszeit): 1 = Montag … 7 = Sonntag. */
export function isoWeekday(date: Date): number {
  return date.getDay() === 0 ? 7 : date.getDay();
}

/** yyyy-MM-dd in ORTSZEIT — `toISOString()` rechnet in UTC, kurz nach Mitternacht stünde dort noch der Vortag. */
export function isoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * Für welchen Tag die Anwesenheitsliste aufgeht: der jüngste Trainingstag, der nicht in der Zukunft liegt — am Freitag
 * also heute, am Montag darauf der vergangene Freitag (nachtragen). Ohne festen Tag: heute.
 */
export function trainingDate(weekday: number | null | undefined, today: Date): string {
  const d = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  if (weekday != null && weekday >= 1 && weekday <= 7) d.setDate(d.getDate() - ((isoWeekday(d) - weekday + 7) % 7));
  return isoDate(d);
}

/** Trainiert die Gruppe heute? */
export function trainsToday(weekday: number | null | undefined, today: Date): boolean {
  return weekday != null && isoWeekday(today) === weekday;
}

function parts(iso: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
}

/** „Fr 25.09." — für Tabellenköpfe. */
export function shortDate(iso: string): string {
  const d = parts(iso);
  return d ? `${WEEKDAYS_SHORT[isoWeekday(d) - 1]} ${pad(d.getDate())}.${pad(d.getMonth() + 1)}.` : iso;
}

/** „Freitag, 25. September 2026". */
export function longDate(iso: string): string {
  const d = parts(iso);
  return d ? `${WEEKDAYS[isoWeekday(d) - 1]}, ${d.getDate()}. ${MONTHS[d.getMonth()]} ${d.getFullYear()}` : iso;
}

/** Der Einmal-Code in zwei Fünfergruppen — so tippt er sich ab. */
export function formatLinkCode(code: string): string {
  return code.length > 5 ? `${code.slice(0, 5)}-${code.slice(5)}` : code;
}

export const STATUS_LABEL: Record<Status, string> = { present: 'da', absent: 'gefehlt' };

/** „8 von 10 Einheiten da" — ohne erfasste Einheit leer. */
export function attendanceText(present: number, recorded: number): string {
  if (!recorded) return '';
  return `${present} von ${recorded} ${recorded === 1 ? 'Einheit' : 'Einheiten'} da`;
}

export interface Register<T> {
  letter: string;
  items: T[];
}

type Named = { firstName: string; lastName: string };

/** Wonach die Kartei ordnet: der Nachname — und wo keiner eingetragen ist (er ist optional), der Vorname. */
export function sortName(m: Named): string {
  return m.lastName.trim() || m.firstName.trim();
}

/** Der fett gesetzte Teil des Namens in Listen: der Nachname, ohne ihn der Vorname. */
export function nameHead(m: Named): string {
  return sortName(m);
}

/** Der Rest dahinter (mit führendem Leerzeichen): der Vorname — leer, wenn er schon vorne steht. */
export function nameTail(m: Named): string {
  return m.lastName.trim() ? ` ${m.firstName.trim()}` : '';
}

/**
 * Die Kartei nach dem Anfangsbuchstaben des Nachnamens (ohne Nachnamen: des Vornamens) — wie die Register eines
 * Karteikastens. Umlaute stehen beim Grundbuchstaben (Ä bei A), alles andere unter „#".
 */
export function byInitial<T extends Named>(rows: T[]): Register<T>[] {
  const result: Register<T>[] = [];
  for (const row of rows) {
    const first = sortName(row).charAt(0).normalize('NFD').charAt(0).toUpperCase();
    const letter = /^[A-Z]$/.test(first) ? first : '#';
    const last = result[result.length - 1];
    if (last?.letter === letter) last.items.push(row);
    else result.push({ letter, items: [row] });
  }
  return result;
}

export function emptyInput(): MemberInput {
  return { firstName: '', lastName: '', birthDate: null, birthYear: null, level: null, archived: false, isTrainer: false, contacts: [], groupIds: [] };
}

export function toInput(m: Member): MemberInput {
  return { firstName: m.firstName, lastName: m.lastName, birthDate: m.birthDate ?? null, birthYear: m.birthYear ?? null,
    level: m.level ?? null, archived: m.archived, isTrainer: m.isTrainer, contacts: m.contacts.map(c => ({ ...c })),
    groupIds: m.groups.map(g => g.id) };
}

export function fullName(m: Pick<MemberRow, 'firstName' | 'lastName'>): string {
  return `${m.firstName} ${m.lastName}`.trim();
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}
