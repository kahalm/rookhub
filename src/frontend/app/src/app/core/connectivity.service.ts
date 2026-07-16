import { Injectable, NgZone, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, interval } from 'rxjs';

/** Aktuelles Verbindungsproblem: Gerät offline > Server nicht erreichbar > keins. */
export type ConnectivityProblem = 'offline' | 'unreachable' | null;

/** Intervall der automatischen Wiederverbindungs-Pings, solange der Server unerreichbar ist. */
const RECHECK_INTERVAL_MS = 30000;

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
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  private offline = signal(typeof navigator !== 'undefined' ? !navigator.onLine : false);
  private unreachable = signal(false);
  private downSince: number | null = null;
  private recheck?: Subscription;

  /** Hook (setzt AppComponent): meldet die Wiederherstellung via ClientLogService. */
  reportRecovery?: (kind: string, detail?: string) => void;

  constructor(private zone: NgZone, private http: HttpClient) {
    window.addEventListener('online', () => zone.run(() => this.offline.set(false)));
    window.addEventListener('offline', () => zone.run(() => this.offline.set(true)));
  }

  /** Aktuelles Problem für den Banner ('offline' hat Vorrang) oder null. */
  problem(): ConnectivityProblem {
    if (this.offline()) return 'offline';
    if (this.unreachable()) return 'unreachable';
    return null;
  }

  /** Vom Interceptor: /api-Request scheiterte mit Status 0, obwohl der Browser online ist. */
  reportApiFailure(): void {
    if (this.downSince === null) this.downSince = Date.now();
    if (!this.unreachable()) {
      this.unreachable.set(true);
      this.startRecheck();
    }
  }

  /** Vom Interceptor: ein /api-Request kam erfolgreich durch → Zustand aufheben. */
  reportApiSuccess(): void {
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
}
