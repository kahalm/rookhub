import { Injectable, NgZone, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, interval } from 'rxjs';

/** Aktuelles Verbindungsproblem: Gerät offline > Server nicht erreichbar > keins. */
export type ConnectivityProblem = 'offline' | 'unreachable' | null;

/** Intervall der automatischen Wiederverbindungs-Pings, solange der Server unerreichbar ist. */
const RECHECK_INTERVAL_MS = 30000;

/** Entprellung: so lange muss ein Problem ununterbrochen bestehen, bevor der Banner erscheint.
 * Kurze Blips (ein einzelner endgültig gescheiterter Request, Netzwechsel WLAN→Mobilfunk) würden
 * den Banner sonst für einen Moment aufblitzen lassen. Die Erholung blendet weiterhin SOFORT aus. */
const SHOW_DELAY_MS = 2500;

/**
 * Erkennt Verbindungsprobleme für den globalen Hinweis-Banner in der App-Shell:
 * - 'offline'     = Browser meldet keine Netzverbindung (window online/offline-Events).
 * - 'unreachable' = Gerät ist laut Browser online, aber API-Requests scheitern auf
 *   Netzwerkebene (Status 0 = „Failed to fetch": DNS-Blockade durch VPN/Filter,
 *   hängender Tunnel, Server weg). Gesetzt vom connectivityInterceptor, gelöscht
 *   beim nächsten erfolgreichen API-Response.
 *
 * Solange 'unreachable' aktiv ist, pingt der Service alle 30 s /api/menu an — der
 * Erfolgsfall läuft durch den Interceptor und hebt den Zustand automatisch auf.
 * Die Wiederherstellung wird (inkl. Ausfalldauer) als ClientLog gemeldet, damit
 * solche Client-seitigen Ausfälle in Kibana sichtbar werden.
 *
 * Beide Zustände sind ENTPRELLT (SHOW_DELAY_MS): der Banner erscheint erst, wenn das
 * Problem den Moment überdauert — sonst blitzt er bei jedem transienten Fehlschlag auf.
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  private offline = signal(typeof navigator !== 'undefined' ? !navigator.onLine : false);
  private unreachable = signal(false);
  /** Roher Browser-Zustand (online/offline-Events) — Banner-Signal folgt entprellt. */
  private offlineRaw = typeof navigator !== 'undefined' ? !navigator.onLine : false;
  private offlineTimer: ReturnType<typeof setTimeout> | null = null;
  private unreachableTimer: ReturnType<typeof setTimeout> | null = null;
  private downSince: number | null = null;
  private recheck?: Subscription;

  /** Hook (setzt AppComponent): meldet die Wiederherstellung via ClientLogService. */
  reportRecovery?: (kind: string, detail?: string) => void;

  constructor(private zone: NgZone, private http: HttpClient) {
    window.addEventListener('online', () => zone.run(() => {
      this.offlineRaw = false;
      this.clearOfflineTimer();
      this.offline.set(false);   // Erholung sofort
    }));
    window.addEventListener('offline', () => zone.run(() => {
      this.offlineRaw = true;
      if (this.offline() || this.offlineTimer !== null) return;
      this.offlineTimer = setTimeout(() => {
        this.offlineTimer = null;
        if (this.offlineRaw) this.offline.set(true);
      }, SHOW_DELAY_MS);
    }));
  }

  /** Aktuelles Problem für den Banner ('offline' hat Vorrang) oder null. */
  problem(): ConnectivityProblem {
    if (this.offline()) return 'offline';
    if (this.unreachable()) return 'unreachable';
    return null;
  }

  /** Vom Interceptor: /api-Request scheiterte mit Status 0, obwohl der Browser online ist.
   * Zeigt den Banner erst, wenn das Problem SHOW_DELAY_MS überdauert; zusätzlich läuft sofort
   * eine Gegenprobe (/api/menu-Ping) — war der Fehlschlag ein Einzelfall, räumt deren Erfolg den
   * schwebenden Zustand ab, BEVOR etwas sichtbar wird (wichtig auf idlen Seiten, wo sonst kein
   * weiterer Request käme). */
  reportApiFailure(): void {
    if (this.downSince === null) this.downSince = Date.now();
    if (this.unreachable() || this.unreachableTimer !== null) return;
    this.unreachableTimer = setTimeout(() => {
      this.unreachableTimer = null;
      this.unreachable.set(true);
      this.startRecheck();
    }, SHOW_DELAY_MS);
    this.checkNow();
  }

  /** Vom Interceptor: ein /api-Request kam erfolgreich durch → Zustand aufheben. */
  reportApiSuccess(): void {
    this.clearUnreachableTimer();   // schwebende Anzeige (Blip) abbrechen
    if (!this.unreachable()) { this.downSince = null; return; }
    const seconds = this.downSince !== null ? Math.round((Date.now() - this.downSince) / 1000) : 0;
    this.downSince = null;
    this.unreachable.set(false);
    this.stopRecheck();
    this.reportRecovery?.('connectivity_restored', `api unreachable for ${seconds}s`);
  }

  /** Manueller „Erneut versuchen"-Ping (Banner-Button). Erfolg räumt via Interceptor auf. */
  checkNow(): void {
    this.http.get('/api/menu').subscribe({ error: () => { /* Zustand bleibt, Banner steht schon */ } });
  }

  private startRecheck(): void {
    this.stopRecheck();
    this.recheck = interval(RECHECK_INTERVAL_MS).subscribe(() => this.checkNow());
  }

  private stopRecheck(): void {
    this.recheck?.unsubscribe();
    this.recheck = undefined;
  }

  private clearOfflineTimer(): void {
    if (this.offlineTimer !== null) { clearTimeout(this.offlineTimer); this.offlineTimer = null; }
  }

  private clearUnreachableTimer(): void {
    if (this.unreachableTimer !== null) { clearTimeout(this.unreachableTimer); this.unreachableTimer = null; }
  }
}
