/**
 * Einen Termin als iCalendar-Datei (.ics, RFC 5545) erzeugen und ausliefern.
 *
 * <p><b>Warum eine Datei und kein Android-Intent:</b> ein `intent://`-Link mit
 * `android.intent.action.INSERT` oeffnet zwar den Kalender-Dialog, aber NUR in Chrome fuer
 * Android — auf iOS, im Firefox und am Rechner passiert nichts. Eine .ics-Datei nimmt dagegen
 * jedes System an: Android bietet beim Oeffnen den Kalender an, iOS uebergibt sie direkt an
 * Kalender, Windows/macOS/Linux an Outlook, Apple Kalender oder Thunderbird. Ein Weg statt vier,
 * ohne Erkennung des Systems — und ohne dass der Termin ueber einen fremden Dienst laeuft
 * (bei „privater Kalender" ist genau das der Punkt).</p>
 *
 * <p>Termine ohne Uhrzeit werden als GANZTAGES-Termine geschrieben (`VALUE=DATE`). Das ist nicht
 * nur einfacher, sondern die einzige ehrliche Form: chess-results liefert Datum OHNE Uhrzeit, und
 * ein erfundener Beginn um 00:00 waere in einer anderen Zeitzone der Vortag.</p>
 */
export interface CalendarEvent {
  /** Stabile Kennung — dieselbe Kennung aktualisiert den Termin statt ihn zu verdoppeln. */
  uid: string;
  title: string;
  /** Erster Tag, `yyyy-MM-dd`. */
  start: string;
  /** LETZTER Tag (einschliesslich), `yyyy-MM-dd`. Fehlt er, ist es ein Ein-Tages-Termin. */
  end?: string | null;
  location?: string | null;
  description?: string | null;
  url?: string | null;
  /** Erstellungszeitpunkt — als Parameter, damit Tests ein festes Ergebnis pruefen koennen. */
  stamp?: Date;
}

/** Baut den vollstaendigen Dateiinhalt. */
export function buildIcs(event: CalendarEvent): string {
  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//RookHub//Turnierkalender//DE',
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
    'BEGIN:VEVENT',
    `UID:${event.uid}`,
    `DTSTAMP:${stampOf(event.stamp ?? new Date())}`,
    `DTSTART;VALUE=DATE:${compactDate(event.start)}`,
    // DTEND ist bei Ganztages-Terminen AUSSCHLIESSLICH: ein Turnier vom 18. bis 20. endet fuer
    // den Kalender am 21. Ohne diesen Tag fehlt in jeder Kalender-App der letzte Turniertag.
    `DTEND;VALUE=DATE:${compactDate(dayAfter(event.end || event.start))}`,
    `SUMMARY:${escapeText(event.title)}`,
  ];
  if (event.location) lines.push(`LOCATION:${escapeText(event.location)}`);
  if (event.description) lines.push(`DESCRIPTION:${escapeText(event.description)}`);
  if (event.url) lines.push(`URL:${escapeText(event.url)}`);
  lines.push('TRANSP:TRANSPARENT', 'END:VEVENT', 'END:VCALENDAR');

  // CRLF ist in RFC 5545 vorgeschrieben — mit reinem \n weigern sich manche Kalender.
  return lines.map(fold).join('\r\n') + '\r\n';
}

/** Dateiname aus dem Titel — ohne Zeichen, die irgendein Dateisystem ablehnt. */
export function icsFileName(title: string): string {
  const base = title.normalize('NFKD').replace(/[^\w\s-]/g, '').trim().replace(/\s+/g, '-');
  return `${(base || 'termin').slice(0, 60)}.ics`;
}

/**
 * Legt die Datei dem Browser zum Speichern/Oeffnen vor. `false`, wenn das nicht ging (gesperrter
 * Speicher, Umgebung ohne Blob-URLs) — der Aufrufer sagt es dann, statt stumm nichts zu tun.
 */
export function downloadIcs(content: string, fileName: string): boolean {
  try {
    const blob = new Blob([content], { type: 'text/calendar;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    // In den Baum haengen: Firefox loest einen Klick auf ein loses Element nicht aus.
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Erst nach dem Klick freigeben — sofort wuerde der Download ins Leere greifen.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
    return true;
  } catch {
    return false;
  }
}

// ----- innere Helfer -------------------------------------------------------

/** `2026-12-18` → `20261218`. */
function compactDate(iso: string): string {
  return iso.replace(/-/g, '');
}

/** Der Tag NACH dem angegebenen (fuer das ausschliessliche DTEND). */
function dayAfter(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  // UTC rechnen: mit lokaler Zeit verschiebt eine Zeitzone das Datum um einen Tag.
  const next = new Date(Date.UTC(y, (m || 1) - 1, d || 1) + 86_400_000);
  return next.toISOString().slice(0, 10);
}

function stampOf(date: Date): string {
  return date.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}/, '');
}

/** RFC 5545: Backslash, Semikolon und Komma sind Steuerzeichen, Zeilenumbrueche werden `\n`. */
function escapeText(value: string): string {
  return value
    .replace(/\\/g, '\\\\')
    .replace(/;/g, '\;')
    .replace(/,/g, '\\,')
    .replace(/\r?\n/g, '\\n');
}

/**
 * Zeilen laenger als 75 OKTETTE muessen umgebrochen werden (Fortsetzung mit einem Leerzeichen).
 * Gezaehlt werden Bytes, nicht Zeichen — und umgebrochen wird nur zwischen Zeichen, damit keine
 * UTF-8-Folge zerrissen wird (ein „ö" mitten durchtrennt macht die Datei unlesbar).
 */
function fold(line: string): string {
  const encoder = new TextEncoder();
  const out: string[] = [];
  let current = '';
  let bytes = 0;
  let limit = 75;

  for (const char of line) {
    const size = encoder.encode(char).length;
    if (bytes + size > limit) {
      out.push(current);
      current = '';
      bytes = 1;          // das fuehrende Leerzeichen der Fortsetzungszeile
      limit = 75;
    }
    current += char;
    bytes += size;
  }
  out.push(current);
  return out.join('\r\n ');
}
