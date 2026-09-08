import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  SwUpdate, VersionInstallationFailedEvent, VersionReadyEvent,
} from '@angular/service-worker';
import { TranslateService } from '@ngx-translate/core';
import { filter, interval } from 'rxjs';
import { ClientLogService } from './client-log.service';
import { SnackbarService } from './snackbar.service';

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
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly swUpdate = inject(SwUpdate);
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
    if (!this.swUpdate.isEnabled) return;

    this.swUpdate.versionUpdates
      .pipe(filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'),
            takeUntilDestroyed(destroyRef))
      .subscribe(() => {
        // duration 0: der Hinweis bleibt stehen, bis jemand ihn wegklickt oder neu laedt — ein
        // nach drei Sekunden verschwundener Hinweis ist derselbe wie keiner.
        const ref = this.snackbar.show(
          this.translate.instant('app.updateAvailable'), { action: 'app.reload', duration: 0 });
        ref.onAction().subscribe(() => this.reloadApp());
      });

    this.swUpdate.versionUpdates
      .pipe(filter((e): e is VersionInstallationFailedEvent =>
              e.type === 'VERSION_INSTALLATION_FAILED'),
            takeUntilDestroyed(destroyRef))
      .subscribe(e => this.clientLog.report('sw_install_failed', e.error));

    this.swUpdate.unrecoverable
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
    if (!this.swUpdate.isEnabled) return;
    this.swUpdate.checkForUpdate()
      .catch(() => { /* SW noch nicht bereit oder offline → ignorieren */ });
  }
}
