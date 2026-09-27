import { ActivatedRouteSnapshot, BaseRouteReuseStrategy } from '@angular/router';

/**
 * Baut eine Seite NEU auf, wenn sich nur ihre Parameter aendern — fuer Routen mit
 * `data: { reloadOnParamChange: true }`.
 *
 * <p><b>Warum.</b> Angular benutzt eine Komponente wieder, solange die Route dieselbe bleibt:
 * von `/tournaments/26` nach `/tournaments/27` aendert sich nur der Parameter, und die Turnierseite
 * las ihre Id beim Start aus dem Snapshot — sie bliebe beim alten Turnier stehen. Die Seite traegt
 * viel Zustand (Spieler, Paarungen, Favoriten, Monitor-Takt, Aktualisieren-Takt); ihn beim
 * Parameterwechsel von Hand zurueckzusetzen waere die Stelle, an der beim naechsten Feld etwas
 * vergessen wird. Neu aufbauen ist dasselbe wie ein frischer Aufruf.</p>
 *
 * <p>Gebraucht seit der Gruppen-Umschaltung (Schachrallye: Gruppe A/B/Maedchen/Schnellschach).</p>
 */
export class ReloadOnParamChangeStrategy extends BaseRouteReuseStrategy {
  override shouldReuseRoute(future: ActivatedRouteSnapshot, curr: ActivatedRouteSnapshot): boolean {
    if (future.routeConfig !== curr.routeConfig) return false;
    if (future.routeConfig?.data?.['reloadOnParamChange'] !== true) return true;
    return sameParams(future.params, curr.params);
  }
}

function sameParams(a: Record<string, unknown>, b: Record<string, unknown>): boolean {
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  return [...keys].every(k => a[k] === b[k]);
}
