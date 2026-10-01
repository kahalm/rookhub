import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/**
 * Turniertermine fuer Menschen (Codereview F6-010). Die API liefert DateOnly als „yyyy-MM-dd",
 * und so stand es bisher ueberall auf der Turnierseite — gleich neben einem Kalender, der
 * „Sa., 3. Okt." schreibt. Hier wird daraus „18.–20. Dez. 2026" (de) bzw. „Dec 18 – 20, 2026"
 * (en). Sortieren, Vergleichen und der ICS-Export bleiben bei ISO; formatiert wird nur die Anzeige.
 *
 * Gerechnet in UTC: „2026-12-18" ist ein KALENDERTAG, kein Zeitpunkt — in der Ortszeit gelesen
 * rutschte er westlich von Greenwich auf den Vortag. Was nicht wie ein ISO-Tag aussieht, kommt
 * unveraendert zurueck, statt zu verschwinden.
 */

/** Ein Tag mit Jahr: „18. Dez. 2026". Auch die Form fuer Zeitraeume. */
const DAY: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' };
/** Ein Spieltermin: „Sa., 26. Sept." — der Wochentag zaehlt, das Jahr steht im Zeitraum darueber. */
const PLAY_DAY: Intl.DateTimeFormatOptions = { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' };

export type TournamentDayStyle = 'day' | 'playDay';

/** Die Formatierer sind teuer anzulegen und werden aus Gettern je Durchlauf gebraucht. */
const formatters = new Map<string, Intl.DateTimeFormat>();

function formatterFor(locale: string | null | undefined, style: TournamentDayStyle): Intl.DateTimeFormat {
  const key = `${locale || 'en'}|${style}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    const options = style === 'playDay' ? PLAY_DAY : DAY;
    try {
      formatter = new Intl.DateTimeFormat(locale || 'en', options);
    } catch {
      formatter = new Intl.DateTimeFormat('en', options);   // unbekannte Sprachkennung
    }
    formatters.set(key, formatter);
  }
  return formatter;
}

function parseDay(value: string | null | undefined): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value?.slice(0, 10) ?? '');
  if (!match) return null;
  const day = new Date(Date.UTC(+match[1], +match[2] - 1, +match[3]));
  return Number.isNaN(day.getTime()) ? null : day;
}

/** Ein Tag: „18. Dez. 2026" bzw. als Spieltermin „Fr., 18. Dez."; leer ohne Datum. */
export function formatTournamentDay(
  value: string | null | undefined, locale: string | null | undefined, style: TournamentDayStyle = 'day',
): string {
  const day = parseDay(value);
  return day ? formatterFor(locale, style).format(day) : (value ?? '');
}

/**
 * Ein Zeitraum: „18.–20. Dez. 2026"; nur ein Tag, wenn beide gleich sind oder einer fehlt;
 * leer, wenn chess-results gar kein Datum lieferte.
 */
export function formatTournamentDates(
  start: string | null | undefined, end: string | null | undefined, locale: string | null | undefined,
): string {
  if (!start || !end || start === end) return formatTournamentDay(start || end, locale);
  const from = parseDay(start);
  const to = parseDay(end);
  if (!from || !to) return `${start} – ${end}`;
  const formatter = formatterFor(locale, 'day');
  // formatRange fasst Gemeinsames zusammen („18.–20. Dez. 2026"); aeltere Browser kennen es nicht.
  return typeof formatter.formatRange === 'function'
    ? formatter.formatRange(from, to)
    : `${formatter.format(from)} – ${formatter.format(to)}`;
}

/**
 * `{{ start | tournamentDate: end }}` bzw. `{{ day | tournamentDate }}` in der aktuellen
 * Oberflaechensprache. UNREIN wie ngx-translates eigene Pipe: die Sprache ist kein Argument und
 * kann wechseln, ohne dass sich der Eingang aendert. Das Ergebnis wird je Bindung gemerkt, ein
 * Durchlauf ohne Aenderung vergleicht nur drei Zeichenketten.
 */
@Pipe({ name: 'tournamentDate', standalone: true, pure: false })
export class TournamentDatePipe implements PipeTransform {
  private readonly translate = inject(TranslateService);
  private last?: {
    start: string | null | undefined; end: string | null | undefined;
    style: TournamentDayStyle; lang: string; text: string;
  };

  transform(start: string | null | undefined, end?: string | null, style: TournamentDayStyle = 'day'): string {
    const lang = this.translate.currentLang() || 'en';
    const last = this.last;
    if (last && last.start === start && last.end === end && last.style === style && last.lang === lang) {
      return last.text;
    }
    const text = style === 'playDay'
      ? formatTournamentDay(start, lang, 'playDay')
      : formatTournamentDates(start, end, lang);
    this.last = { start, end, style, lang, text };
    return text;
  }
}
