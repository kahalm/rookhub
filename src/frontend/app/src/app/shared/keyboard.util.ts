/**
 * Gehoert ein Tastendruck dem Brett der Seite? Die eine Regel fuer alle `window:keydown`-/`document:keydown`-
 * Kuerzel (Pfeile blaettern, Pos1/Ende, Leertaste/Enter = weiter) — vorher schrieb jede Seite ihre eigene, und die
 * meisten liessen offene Menues und Dialoge durch.
 *
 * <p>NEIN, wenn
 * <ul>
 *   <li>schon jemand die Taste verarbeitet hat (`defaultPrevented` — z. B. `mat-select`, `mat-menu`: sie bewegen
 *       ihre Auswahl mit `preventDefault`, ohne die Ausbreitung zu stoppen);</li>
 *   <li>Alt/Strg/Cmd mitgedrueckt ist (Alt+← ist „Seite zurueck" des Browsers, nicht „Zug zurueck");</li>
 *   <li>der Fokus in einem Eingabefeld steht (dort gehoeren die Tasten dem Cursor);</li>
 *   <li>der Fokus in einem offenen Menue/Dialog liegt (`.cdk-overlay-container`) — sonst verschob ein Tastendruck
 *       im Einstellungsdialog die Auswahl UND blaetterte das Brett dahinter, Enter lud das naechste Puzzle.</li>
 * </ul>
 * Shift bleibt erlaubt (Seiten mit eigener Shift-Regel pruefen sie selbst).</p>
 *
 * <p>`host`: das eigene Element einer Komponente, die SELBST in einem Dialog lebt (Partie-Betrachter). Ihre Tasten
 * im eigenen Overlay-Fenster gehoeren ihr; ein darueber geoeffnetes Menue (eigenes Fenster) bleibt aussen vor.</p>
 */
export function isBoardHotkey(event: KeyboardEvent, host?: Element | null): boolean {
  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return false;
  const target = event.target as Partial<HTMLElement> | null;
  if (!target || typeof target !== 'object') return true;
  if (target.isContentEditable) return false;
  if (typeof target.tagName === 'string' && /^(INPUT|TEXTAREA|SELECT)$/i.test(target.tagName)) return false;
  if (typeof target.closest !== 'function' || !target.closest('.cdk-overlay-container')) return true;
  const pane = target.closest('.cdk-overlay-pane');
  return !!host && !!pane && pane.contains(host);
}
