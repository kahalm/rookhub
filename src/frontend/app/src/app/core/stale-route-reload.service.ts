import { Injectable, inject } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

/**
 * Sperre im `sessionStorage`: Liste der Adressen (JSON), für die in diesem Tab schon einmal neu geladen wurde.
 * Der Eintrag im Register steht in `local-storage-keys.ts`.
 */
export const STALE_ROUTE_RELOAD_KEY = 'rookhub_stale_route_reload';

/** So lange darf die Suche dauern, bevor die normale Nicht-gefunden-Seite erscheint. */
// UI-Sweep 2026-11-10 (d10-rk-404-ladekreis-haengt-ohne-sw-steuerung): 10 s Ladekreis vor der Meldung waren zu lang.
export const STALE_ROUTE_CHECK_TIMEOUT_MS = 4_000;

/**
 * „Seite nicht gefunden“ auf einer ALTEN Fassung: Der geöffnete Link kann eine Route meinen, die es erst in einer
 * neueren Fassung gibt (real: `/g/:token/summary` auf 0.932.1). Dann einmal die neue Fassung aktivieren und dieselbe
 * Adresse neu laden.
 *
 * <p><b>Warum `activateUpdate` entscheidet und nicht das Ergebnis von `checkForUpdate`:</b> Der Service Worker
 * antwortet auf `checkForUpdate` nur dann mit `true`, wenn er die Fassung in DIESEM Moment neu entdeckt. Hat er sie
 * schon geladen — der Normalfall, `AppUpdateService` sucht beim Start —, kommt `false`, obwohl der Tab noch auf der
 * alten Fassung läuft. `activateUpdate` liefert dagegen genau dann `true`, wenn der Tab wirklich umgestellt wurde.</p>
 *
 * <p><b>Keine Schleife:</b> Jede Adresse wird je Tab höchstens einmal neu geladen; die Sperre steht, bevor aktiviert
 * wird. Lässt sie sich nicht lesen oder schreiben, wird gar nicht neu geladen (wie bei der Selbstheilung in
 * `AppUpdateService`, Vorfall 2026-07-15).</p>
 */
@Injectable({ providedIn: 'root' })
export class StaleRouteReloadService {
  // Optional: LeagueHub registriert keinen Service Worker, dort gibt es keinen Anbieter.
  private readonly swUpdate = inject(SwUpdate, { optional: true });

  /** Lohnt sich die Suche? Falls nein, zeigt die Seite sofort ihren Text, ohne Ladekreis. */
  canTry(): boolean {
    if (!this.swUpdate?.isEnabled) return false;
    // UI-Sweep 2026-11-10 (d10-rk-404-ladekreis-haengt-ohne-sw-steuerung): ohne steuernden Service Worker antwortet
    // checkForUpdate nie — dann gar nicht erst suchen.
    if (!this.swControlsTab()) return false;
    const done = this.readLocks();
    return done !== null && !done.includes(this.address());
  }

  /**
   * Sucht nach einer neueren Fassung und lädt, falls der Tab darauf umgestellt wurde, neu.
   *
   * @param cancelled Die Seite ist inzwischen verlassen: dann nichts mehr auslösen.
   * @returns `true`, wenn neu geladen wird (die Seite bleibt dann beim Ladekreis). Lehnt nie ab.
   */
  reloadIfNewer(cancelled: () => boolean = () => false): Promise<boolean> {
    if (!this.canTry()) return Promise.resolve(false);

    let timer: ReturnType<typeof setTimeout> | undefined;
    let expired = false;
    const timeout = new Promise<boolean>(resolve => {
      timer = setTimeout(() => { expired = true; resolve(false); }, STALE_ROUTE_CHECK_TIMEOUT_MS);
    });
    const attempt = this.attempt(() => expired || cancelled());
    return Promise.race([attempt, timeout]).finally(() => clearTimeout(timer));
  }

  private async attempt(stop: () => boolean): Promise<boolean> {
    const swUpdate = this.swUpdate!;
    const address = this.address();
    try {
      await swUpdate.checkForUpdate();   // Ergebnis bewusst ungenutzt, siehe Klassenkommentar
    } catch {
      return false;
    }
    if (stop() || !this.lock(address)) return false;

    let activated = false;
    try {
      activated = await swUpdate.activateUpdate();
    } catch { /* wie „keine neue Fassung“ */ }

    if (!activated) {
      this.unlock(address);   // Nicht neu geladen: Kommt später eine Fassung, darf diese Adresse es noch einmal versuchen.
      return false;
    }
    // Ab hier läuft der Tab auf der neuen Fassung, seine alten Lazy-Chunks passen nicht mehr: auf jeden Fall neu laden,
    // auch wenn die Zeitgrenze inzwischen abgelaufen ist.
    this.reloadApp();
    return true;
  }

  /** In eine Methode gekapselt, damit Tests den harten Reload abfangen können. */
  protected reloadApp(): void {
    document.location.reload();
  }

  /** Steuert ein Service Worker diesen Tab? (Methode, damit Tests ihn vortäuschen können.) */
  protected swControlsTab(): boolean {
    return !!navigator.serviceWorker?.controller;
  }

  private address(): string {
    return document.location.pathname + document.location.search;
  }

  /** Bisher gesperrte Adressen; `null`, wenn der Speicher nicht nutzbar oder der Inhalt unbrauchbar ist. */
  private readLocks(): string[] | null {
    try {
      const parsed: unknown = JSON.parse(sessionStorage.getItem(STALE_ROUTE_RELOAD_KEY) ?? '[]');
      return Array.isArray(parsed) ? parsed.filter((a): a is string => typeof a === 'string') : null;
    } catch {
      return null;
    }
  }

  private lock(address: string): boolean {
    const done = this.readLocks();
    if (done === null || done.includes(address)) return false;
    return this.writeLocks([...done, address]);
  }

  private unlock(address: string): void {
    const done = this.readLocks();
    if (done !== null) this.writeLocks(done.filter(a => a !== address));
  }

  private writeLocks(addresses: string[]): boolean {
    try {
      sessionStorage.setItem(STALE_ROUTE_RELOAD_KEY, JSON.stringify(addresses));
      return true;
    } catch {
      return false;
    }
  }
}
