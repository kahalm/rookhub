import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  SwUpdate, VersionInstallationFailedEvent, VersionReadyEvent,
} from '@angular/service-worker';
import { TranslateService } from '@ngx-translate/core';
import { filter, interval } from 'rxjs';
import { ClientLogService } from './client-log.service';
import { SnackbarService } from './snackbar.service';
import { APP_VERSION } from '../../environments/changelog';
import { fetchServerVersion, isNewerVersion } from './app-version-check';

/**
 * „Es gibt eine neue Fassung" — der Umgang mit dem Service Worker, fuer BEIDE Oberflaechen.
 *
 * <p>Warum das ein geteilter Dienst ist und keine zwei Fassungen: der Service Worker liefert die
 * App aus seinem Zwischenspeicher aus. Ohne diesen Hinweis laeuft ein offener Tab nach einem
 * Deploy unbegrenzt auf der ALTEN Fassung weiter, und zwar ohne jedes Anzeichen — der Nutzer
 * sieht eine Seite, die es so nicht mehr gibt, und meldet Fehler, die laengst behoben sind.
 * Genau das war auf der Turnierseite der Fall: sie registriert seit ihrem ersten Tag einen
 * Service Worker, hatte aber nie einen Hinweis und nie eine aktive Suche nach neuen Fassungen.</p>
 *
 * <p>Der Dienst macht vier Dinge, und das dritte ist das wichtigste:</p>
 * <ol>
 *   <li>Neue Fassung bereit → Hinweis mit „Neu laden".</li>
 *   <li>Gescheiterte Installation → Meldung an die API (nur Telemetrie).</li>
 *   <li>Kaputter Zustand (UNRECOVERABLE_STATE) → Selbstheilung, siehe unten.</li>
 *   <li>AKTIV nachsehen: beim Start, alle 15 Minuten und immer, wenn der Tab wieder in den
 *       Vordergrund kommt. Ohne das merkt ein lange offener Tab ein Deploy nie — der Service
 *       Worker prueft von sich aus nur beim (Neu-)Start.</li>
 * </ol>
 */
/** Ergebnis von {@link AppUpdateService.checkNow}: gibt es eine neuere Fassung, und wenn bekannt, welche. */
export interface AppUpdateCheck {
  available: boolean;
  version: string | null;
}

/** Höchstens so lange wartet {@link AppUpdateService.checkNow} auf den Service Worker. */
export const SW_CHECK_TIMEOUT_MS = 3000;

@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  // Optional: LeagueHub (und ClubHub) registrieren keinen Service Worker, nutzen aber die geteilte Nicht-gefunden-Seite,
  // die checkNow() ruft (U1). Ohne Anbieter ist der Dienst dort einfach „ohne Service Worker“.
  private readonly swUpdate = inject(SwUpdate, { optional: true });
  private readonly clientLog = inject(ClientLogService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  /** Guard-Key: pro Tab-Sitzung hoechstens EIN automatischer Selbstheilungs-Reload. */
  static readonly RecoveryGuardKey = 'rookhub_sw_recovery_reload';

  /** Wie oft von selbst nach einer neuen Fassung gesehen wird. */
  private static readonly CheckIntervalMs = 15 * 60 * 1000;

  /**
   * Laufende Selbstheilung — als Feld, damit Tests den asynchronen Ablauf deterministisch
   * abwarten koennen, statt auf Zeitspannen zu hoffen.
   */
  recovery?: Promise<void>;

  /**
   * Anhaengen. Der `destroyRef` kommt vom Aufrufer (der App-Huelle): so endet alles mit ihr,
   * auch der Horcher am Sichtbarkeitswechsel, der sonst je Testlauf einen weiteren hinterliesse.
   */
  start(destroyRef: DestroyRef): void {
    if (!this.swUpdate?.isEnabled) return;
    const swUpdate = this.swUpdate;

    swUpdate.versionUpdates
      .pipe(filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'),
            takeUntilDestroyed(destroyRef))
      .subscribe(() => {
        // duration 0: der Hinweis bleibt stehen, bis jemand ihn wegklickt oder neu laedt — ein
        // nach drei Sekunden verschwundener Hinweis ist derselbe wie keiner.
        const ref = this.snackbar.show(
          this.translate.instant('app.updateAvailable'), { action: 'app.reload', duration: 0 });
        ref.onAction().subscribe(() => this.reloadApp());
      });

    swUpdate.versionUpdates
      .pipe(filter((e): e is VersionInstallationFailedEvent =>
              e.type === 'VERSION_INSTALLATION_FAILED'),
            takeUntilDestroyed(destroyRef))
      .subscribe(e => this.clientLog.report('sw_install_failed', e.error));

    swUpdate.unrecoverable
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(ev => { this.recovery = this.recoverFromBrokenServiceWorker(ev.reason); });

    this.checkForUpdate();
    interval(AppUpdateService.CheckIntervalMs)
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.checkForUpdate());

    const onVisible = () => {
      if (document.visibilityState === 'visible') this.checkForUpdate();
    };
    document.addEventListener('visibilitychange', onVisible);
    destroyRef.onDestroy(() => document.removeEventListener('visibilitychange', onVisible));
  }

  /**
   * Selbstheilung bei UNRECOVERABLE_STATE (ein gecachtes Stueck fehlt im Zwischenspeicher UND
   * ist nach einem Deploy auch am Server weg): Ereignis melden, alle Service-Worker-
   * Registrierungen entfernen, die ngsw-Zwischenspeicher loeschen, danach genau EINMAL neu laden.
   *
   * <p>Der Guard im `sessionStorage` ist der Kern: ein blinder Reload heilt den Zustand nicht,
   * er feuerte sofort wieder, und die App hing in einer Endlos-Reload-Schleife (Prod-Vorfall
   * 2026-07-15). Laesst sich der Guard nicht setzen, wird ABSICHTLICH gar nicht neu geladen —
   * lieber ohne Service Worker weiterlaufen als in der Schleife stecken.</p>
   */
  private async recoverFromBrokenServiceWorker(reason: string): Promise<void> {
    this.clientLog.report('sw_unrecoverable', reason);

    let alreadyReloaded = true;   // Vorgabe: NICHT neu laden, ausser der Guard laesst sich setzen
    try {
      alreadyReloaded = sessionStorage.getItem(AppUpdateService.RecoveryGuardKey) === '1';
      if (!alreadyReloaded) sessionStorage.setItem(AppUpdateService.RecoveryGuardKey, '1');
    } catch { /* Speicher nicht verfuegbar → kein automatischer Reload */ }

    try {
      const regs = await navigator.serviceWorker?.getRegistrations?.() ?? [];
      await Promise.all(regs.map(r => r.unregister()));
      if (typeof caches !== 'undefined') {
        const keys = await caches.keys();
        await Promise.all(keys.filter(k => k.startsWith('ngsw:')).map(k => caches.delete(k)));
      }
    } catch { /* best effort — der Reload registriert den SW ohnehin frisch */ }

    if (!alreadyReloaded) this.reloadApp();
  }

  /** In eine Methode gekapselt, damit Tests den harten Reload abfangen koennen. */
  protected reloadApp(): void {
    document.location.reload();
  }

  private checkForUpdate(): void {
    if (!this.swUpdate?.isEnabled) return;
    this.swUpdate.checkForUpdate()
      .catch(() => { /* SW noch nicht bereit oder offline → ignorieren */ });
  }

  /**
   * SOFORT nachsehen, ob der Server eine neuere Fassung hat (U1: die Nicht-gefunden-Seite — eine alte, im Service Worker
   * gehaltene Fassung kennt neue Routen nicht; die 15-Minuten-Prüfung käme zu spät). Erst `SwUpdate.checkForUpdate()`,
   * dann (auch ohne Service Worker) die Version des Server-Changelogs gegen {@link APP_VERSION}. Fehler → „nichts“.
   */
  async checkNow(): Promise<AppUpdateCheck> {
    let swFound = false;
    // UI-Sweep 2026-11-10 (d10-rk-404-ladekreis-haengt-ohne-sw-steuerung): ohne steuernden Service Worker (Erstbesuch,
    // Strg+Umschalt+R, SW blockiert) löst checkForUpdate nie auf — dann gar nicht fragen, sonst höchstens 3 s warten.
    if (this.swUpdate?.isEnabled && this.swControlsTab()) {
      let timer: ReturnType<typeof setTimeout> | undefined;
      const timeout = new Promise<boolean>(resolve => { timer = setTimeout(() => resolve(false), SW_CHECK_TIMEOUT_MS); });
      try { swFound = await Promise.race([this.swUpdate.checkForUpdate(), timeout]); }
      catch { /* offline / SW nicht bereit */ }
      finally { clearTimeout(timer); }
    }
    const server = await this.serverVersion();
    if (server && isNewerVersion(server, this.currentVersion)) return { available: true, version: server };
    return { available: swFound, version: null };
  }

  /** „Aktualisieren“ (U1): bereitliegende Fassung aktivieren und dieselbe Adresse frisch laden — ohne Service Worker
   *  ein harter Reload. */
  async activateAndReload(): Promise<void> {
    if (this.swUpdate?.isEnabled) {
      try { await this.swUpdate.activateUpdate(); } catch { /* nichts bereit → der Reload holt sie */ }
      this.replaceLocation(document.location.href);
      return;
    }
    this.reloadApp();
  }

  /** Eigene Fassung — als Feld, damit Tests sie festlegen können. */
  protected currentVersion = APP_VERSION;

  /** In Methoden gekapselt, damit Tests Netz und Navigation abfangen können. */
  protected serverVersion(): Promise<string | null> { return fetchServerVersion(); }
  /** Steuert ein Service Worker diesen Tab? Nur dann antwortet `checkForUpdate` überhaupt (Methode für Tests). */
  protected swControlsTab(): boolean { return !!navigator.serviceWorker?.controller; }
  protected replaceLocation(url: string): void { document.location.replace(url); }
}
