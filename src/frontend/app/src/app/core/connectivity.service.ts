import { Injectable, NgZone, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, interval } from 'rxjs';

/** Aktuelles Verbindungsproblem: Gerät offline > Server nicht erreichbar > keins. */
export type ConnectivityProblem = 'offline' | 'unreachable' | null;

/** Intervall der automatischen Wiederverbindungs-Pings, solange der Server unerreichbar ist. */
const RECHECK_INTERVAL_MS = 30000;

/** Entprellung: so lange muss ein Problem ununterbrochen bestehen, bevor der Banner erscheint.
 * Kurze Blips (ein einzelner endgültig gescheiterter Request, Netzwechsel WLAN→Mobilfunk) würden
 * den Banner sonst für einen Moment aufblitzen lassen. 2500 ms reichten nicht: die Gegenprobe
 * läuft durch den Retry-Interceptor und braucht bei einem trägen Server selbst länger — der Banner
 * erschien, und Sekunden später räumte die gelungene Gegenprobe ihn wieder ab. */
export const SHOW_DELAY_MS = 5000;

/** Einmal sichtbar, bleibt der Banner mindestens so lange stehen. Sonst blinkt er bei einer
 * Erholung kurz nach dem Erscheinen nur auf — gelesen hat ihn dann niemand, bemerkt jeder. */
export const MIN_VISIBLE_MS = 4000;

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
 * <p><b>Wann der Banner erscheint und verschwindet</b> (0.478.4, gemeldet als „blinkt rein und
 * raus"): 'unreachable' zeigt sich erst, wenn (1) das Problem {@link SHOW_DELAY_MS} überdauert
 * UND (2) die sofort gestartete Gegenprobe tatsächlich GESCHEITERT ist. Ist sie nach Ablauf der
 * Frist noch unterwegs, entscheidet ihr Ergebnis — ein langsamer Server ist kein Ausfall. Ist der
 * Banner einmal da, verschwindet er frühestens nach {@link MIN_VISIBLE_MS}. Für 'offline' gelten
 * Frist und Mindestdauer ebenso (ohne Gegenprobe — der Browser meldet den Zustand selbst).</p>
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  private offline = signal(typeof navigator !== 'undefined' ? !navigator.onLine : false);
  private unreachable = signal(false);
  /** Roher Browser-Zustand (online/offline-Events) — Banner-Signal folgt entprellt. */
  private offlineRaw = typeof navigator !== 'undefined' ? !navigator.onLine : false;
  private offlineTimer: ReturnType<typeof setTimeout> | null = null;
  private offlineHideTimer: ReturnType<typeof setTimeout> | null = null;
  private offlineShownAt = 0;

  private unreachableTimer: ReturnType<typeof setTimeout> | null = null;
  private unreachableHideTimer: ReturnType<typeof setTimeout> | null = null;
  private unreachableShownAt = 0;
  /** Ein Ausfall ist gemeldet und wird gerade geprüft (Frist läuft ODER Gegenprobe steht aus). */
  private pendingShow = false;
  /** Die Frist ist abgelaufen, die Gegenprobe war da aber noch unterwegs. */
  private delayElapsed = false;
  private probePending = false;

  private downSince: number | null = null;
  private recheck?: Subscription;

  /** Hook (setzt AppComponent): meldet die Wiederherstellung via ClientLogService. */
  reportRecovery?: (kind: string, detail?: string) => void;

  constructor(private zone: NgZone, private http: HttpClient) {
    window.addEventListener('online', () => zone.run(() => {
      this.offlineRaw = false;
      this.clearOfflineTimer();
      if (!this.offline() || this.offlineHideTimer !== null) return;
      const wait = MIN_VISIBLE_MS - (Date.now() - this.offlineShownAt);
      if (wait <= 0) { this.offline.set(false); return; }
      this.offlineHideTimer = setTimeout(() => {
        this.offlineHideTimer = null;
        if (!this.offlineRaw) this.offline.set(false);
      }, wait);
    }));
    window.addEventListener('offline', () => zone.run(() => {
      this.offlineRaw = true;
      this.clearOfflineHideTimer();   // wieder offline, bevor der Banner weg war → er bleibt
      if (this.offline() || this.offlineTimer !== null) return;
      this.offlineTimer = setTimeout(() => {
        this.offlineTimer = null;
        if (!this.offlineRaw) return;
        this.offline.set(true);
        this.offlineShownAt = Date.now();
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
   * Startet die Frist und sofort eine Gegenprobe (/api/menu-Ping) — war der Fehlschlag ein
   * Einzelfall, räumt deren Erfolg den schwebenden Zustand ab, BEVOR etwas sichtbar wird
   * (wichtig auf idlen Seiten, wo sonst kein weiterer Request käme). */
  reportApiFailure(): void {
    if (this.downSince === null) this.downSince = Date.now();
    this.clearUnreachableHideTimer();   // Problem ist wieder da → geplantes Ausblenden abbrechen
    if (this.unreachable() || this.pendingShow) return;
    this.pendingShow = true;
    this.delayElapsed = false;
    this.unreachableTimer = setTimeout(() => {
      this.unreachableTimer = null;
      this.delayElapsed = true;
      // Gegenprobe noch unterwegs → ihr Ergebnis entscheidet. Sonst ist sie schon gescheitert:
      // ein Erfolg hätte über reportApiSuccess die Frist abgebrochen.
      if (!this.probePending) this.showUnreachable();
    }, SHOW_DELAY_MS);
    this.probe();
  }

  /** Vom Interceptor: ein /api-Request kam erfolgreich durch → Zustand aufheben. */
  reportApiSuccess(): void {
    this.clearUnreachableTimer();   // schwebende Anzeige (Blip) abbrechen
    this.pendingShow = false;
    this.delayElapsed = false;
    if (!this.unreachable()) { this.downSince = null; return; }
    if (this.unreachableHideTimer !== null) return;   // Ausblenden ist schon geplant
    const wait = MIN_VISIBLE_MS - (Date.now() - this.unreachableShownAt);
    if (wait <= 0) { this.hideUnreachable(); return; }
    this.unreachableHideTimer = setTimeout(() => {
      this.unreachableHideTimer = null;
      this.hideUnreachable();
    }, wait);
  }

  /** Manueller „Erneut versuchen"-Ping (Banner-Button). Erfolg räumt via Interceptor auf. */
  checkNow(): void {
    this.http.get('/api/menu').subscribe({ error: () => { /* Zustand bleibt, Banner steht schon */ } });
  }

  /** Gegenprobe eines gemeldeten Ausfalls — höchstens eine gleichzeitig. Ihr ERFOLG meldet sich
   * über den Interceptor (reportApiSuccess); hier zählt nur, ob ein Fehlschlag die Anzeige auslöst. */
  private probe(): void {
    if (this.probePending) return;
    this.probePending = true;
    this.http.get('/api/menu').subscribe({
      next: () => { this.probePending = false; },
      error: () => {
        this.probePending = false;
        if (this.pendingShow && this.delayElapsed) this.showUnreachable();
      },
    });
  }

  private showUnreachable(): void {
    this.clearUnreachableTimer();
    this.pendingShow = false;
    this.delayElapsed = false;
    this.unreachable.set(true);
    this.unreachableShownAt = Date.now();
    this.startRecheck();
  }

  private hideUnreachable(): void {
    const seconds = this.downSince !== null ? Math.round((Date.now() - this.downSince) / 1000) : 0;
    this.downSince = null;
    this.unreachable.set(false);
    this.stopRecheck();
    this.reportRecovery?.('connectivity_restored', `api unreachable for ${seconds}s`);
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

  private clearOfflineHideTimer(): void {
    if (this.offlineHideTimer !== null) { clearTimeout(this.offlineHideTimer); this.offlineHideTimer = null; }
  }

  private clearUnreachableTimer(): void {
    if (this.unreachableTimer !== null) { clearTimeout(this.unreachableTimer); this.unreachableTimer = null; }
  }

  private clearUnreachableHideTimer(): void {
    if (this.unreachableHideTimer !== null) { clearTimeout(this.unreachableHideTimer); this.unreachableHideTimer = null; }
  }
}
