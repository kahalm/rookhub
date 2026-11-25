/**
 * Restzeit auf der Uhr als PGN-Auszeichnung `[%clk h:mm:ss]` (1.015.0): lesen, schreiben, aus Kommentaren lösen und eine
 * handschriftlich notierte Zeit („1:12", „72'") lesen. Das Gegenstück im Backend: `PgnClock` (PGN) und `ScoresheetClock`
 * (notierte Zeiten) — die Lese-Regeln sind dort und hier dieselben, beide Seiten haben Tests mit denselben Werten.
 */

const CLK = /\[%clk\s+(\d+):(\d{1,2}):(\d{1,2})(?:\.\d+)?\]/;
const CLK_ALL = /\[%clk\s+\d+:\d{1,2}:\d{1,2}(?:\.\d+)?\]/g;

/** Mehr als 6 Stunden ist kein Uhrenstand (wie `ScoresheetClock.MaxSeconds`). */
export const MAX_CLOCK_SECONDS = 6 * 3600;

/** `[%clk 1:12:00]` in einem Kommentar → Sekunden; `null` = keine. */
export function clockOf(comment: string | null | undefined): number | null {
  const m = comment ? CLK.exec(comment) : null;
  return m ? Number(m[1]) * 3600 + Number(m[2]) * 60 + Number(m[3]) : null;
}

/** Der Kommentar ohne `[%clk]`; `null`, wenn nichts übrig bleibt. */
export function stripClock(comment: string | null | undefined): string | null {
  if (!comment) return null;
  const rest = comment.replace(CLK_ALL, ' ').replace(/\s+/g, ' ').trim();
  return rest || null;
}

/** Sekunden → `h:mm:ss` (die PGN-Form). */
export function formatClock(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  const two = (n: number) => String(n).padStart(2, '0');
  return `${Math.floor(s / 3600)}:${two(Math.floor(s / 60) % 60)}:${two(s % 60)}`;
}

/** Kurzform für die Anzeige: `1:12` (h:mm) — Sekunden nur, wenn welche da sind (`0:05:30`) oder unter einer Minute. */
export function shortClock(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  const two = (n: number) => String(n).padStart(2, '0');
  if (s % 60 !== 0) return formatClock(s);
  return `${Math.floor(s / 3600)}:${two(Math.floor(s / 60) % 60)}`;
}

/** `h:mm:ss` → Sekunden; `null` bei allem anderen. */
export function secondsOf(hms: string | null | undefined): number | null {
  const m = /^(\d+):(\d{2}):(\d{2})$/.exec(hms ?? '');
  return m ? Number(m[1]) * 3600 + Number(m[2]) * 60 + Number(m[3]) : null;
}

/** Kommentar mit der Uhr vorn (`[%clk 1:12:00] Idee`); eine Uhr im Kommentar wird ersetzt. */
export function withClock(comment: string | null | undefined, seconds: number | null): string | null {
  const rest = stripClock(comment);
  if (seconds == null) return rest;
  const tag = `[%clk ${formatClock(seconds)}]`;
  return rest ? `${tag} ${rest}` : tag;
}

/**
 * Eine notierte Restzeit lesen — dieselben Formen wie `ScoresheetClock.Parse`: `1:12`/`1.12`/`1,12`/`1h12` (Stunden:Minuten,
 * solange vorn höchstens 9 steht, sonst Minuten:Sekunden wie `45:30`), `0:05:30`, `72`/`72'`/`72min` (Minuten),
 * `72'30` (Minuten und Sekunden). `null` = keine lesbare Zeit.
 */
export function parseClockInput(raw: string | null | undefined): number | null {
  if (!raw || !raw.trim()) return null;
  const t = raw.trim().toLowerCase().replace(/\s+/g, '')
    .replace(/[’′]/g, "'").replace(/″/g, '"').replace(/''/g, '"');
  const time = (h: number, m: number, s: number) => s > 59 || (h > 0 && m > 59) ? null : h * 3600 + m * 60 + s;
  let seconds: number | null = null;
  let m: RegExpExecArray | null;
  if ((m = /^(\d{1,2})[:.,h](\d{2})[:.,'](\d{2})"?$/.exec(t))) seconds = time(+m[1], +m[2], +m[3]);
  else if ((m = /^(\d{1,3})([:.,h])(\d{2})(['"]*|min|m)?$/.exec(t))) {
    const x = +m[1], y = +m[3];
    const hours = m[2] === 'h' || (x <= 9 && !(m[4] ?? '').includes('"'));
    seconds = hours ? time(x, y, 0) : time(0, x, y);
  } else if ((m = /^(\d{1,3})'(\d{1,2})"?$/.exec(t))) seconds = time(0, +m[1], +m[2]);
  else if ((m = /^(\d{1,3})(?:'|min|m)?$/.exec(t))) seconds = +m[1] * 60;
  return seconds != null && seconds <= MAX_CLOCK_SECONDS ? seconds : null;
}
