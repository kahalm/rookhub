/**
 * Bedenkzeit lesbar machen — aus der PGN-Schreibweise der Plattformen wird das, was auf chess.com
 * neben der Partie steht: „3 + 2", „10 min", „30 s", „1 Tag".
 *
 * Die Quelle ist ein `[TimeControl]`-Header: Sekunden für die Partie, optional `+` Inkrement je Zug
 * (`600`, `180+2`); Fernschach schreibt `Tage/Sekunden je Zug` (`1/86400`), und `-` heißt „keine".
 * Rückgabe ist ein SCHLÜSSEL mit Zahlen, kein fertiger Satz: „min", „s" und „Tag" sind übersetzbar,
 * und die Liste soll nicht in jeder Sprache deutsch stehen.
 */
export interface TimeControlLabel {
  /** i18n-Schlüssel unter `games.tc.` — `plus` (3 + 2), `plusSec` (45 s + 1), `min`, `sec`, `days`. */
  key: 'plus' | 'plusSec' | 'min' | 'sec' | 'days';
  /** Parameter für den Schlüssel (`{ main, inc }` bzw. `{ count }`). */
  params: Record<string, number>;
}

/** Sekunden je Tag — die Fernschach-Schreibweise `1/86400` rechnet damit. */
const SECONDS_PER_DAY = 86400;

/**
 * `null` heißt: nichts anzeigen. Das gilt für fehlende, leere und unverständliche Werte ebenso wie
 * für `-` — eine Zeile ohne Bedenkzeit ist besser als eine mit geratener.
 */
export function formatTimeControl(raw: string | null | undefined): TimeControlLabel | null {
  const tc = (raw ?? '').trim();
  if (!tc || tc === '-') return null;

  // Fernschach: Tage je Zug. `3/259200` sind drei Tage — die Zahl VOR dem Schrägstrich ist nur die
  // Zugzahl des Abschnitts, verlässlich ist die Sekundenzahl dahinter.
  const daily = tc.match(/^(\d{1,3})\/(\d{1,7})$/);
  if (daily) {
    const days = Math.round(Number(daily[2]) / SECONDS_PER_DAY);
    return days >= 1 ? { key: 'days', params: { count: days } } : null;
  }

  const m = tc.match(/^(\d{1,6})(?:\+(\d{1,4}))?$/);
  if (!m) return null;
  const seconds = Number(m[1]);
  const inc = m[2] ? Number(m[2]) : 0;
  if (!seconds && !inc) return null;

  // Mit Inkrement die chess.com-Schreibweise „3 + 2". Das Inkrement geht dabei NIE verloren: eine
  // krumme Grundzeit (45+1) wird in Sekunden geschrieben statt auf eine Minute gerundet — gerundet
  // stünde dort eine Bedenkzeit, die es nicht gibt.
  if (inc > 0) {
    return seconds >= 60 && seconds % 60 === 0
      ? { key: 'plus', params: { main: seconds / 60, inc } }
      : { key: 'plusSec', params: { main: seconds, inc } };
  }
  if (seconds < 60) return { key: 'sec', params: { count: seconds } };
  return { key: 'min', params: { count: Math.round(seconds / 60) } };
}
