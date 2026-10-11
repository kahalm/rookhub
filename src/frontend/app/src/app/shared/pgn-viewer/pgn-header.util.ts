/**
 * UI-Sweep 2026-10-11 (r2-rg-rep-question): PGN-Kopfzeilen, die nur einen PLATZHALTER tragen
 * (`[Event "?"]`, `[White "?"]`, `"-"`, `"??"` oder leer), zählen in der Anzeige als fehlend. Ein
 * Repertoire aus einem Werkzeug, das die Seven-Tag-Roster mit „?" füllt, hieß sonst überall nur „?".
 *
 * <p>NUR für die Anzeige: als Schlüssel (Kapitel-Gruppierung, Trainer-Filter, Kapitelfarben) bleibt der
 * rohe Wert, sonst passen gemerkte Einstellungen nicht mehr.</p>
 */
export function isPgnPlaceholder(value: string | null | undefined): boolean {
  const v = (value ?? '').trim();
  return v === '' || /^[?\-]+$/.test(v);
}

/** Der Wert einer Kopfzeile für die Anzeige — '' für einen Platzhalter. */
export function pgnHeaderText(value: string | null | undefined): string {
  return isPgnPlaceholder(value) ? '' : (value ?? '').trim();
}
