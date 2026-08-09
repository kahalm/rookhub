import { Injectable, NgZone } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from './snackbar.service';

/** Ein aufgeschobener (offline fehlgeschlagener) schreibender Request. */
interface PendingRequest {
  id: string;
  method: 'POST' | 'PUT';
  url: string;
  body: unknown;
  ts: number;
  /** User, unter dem der Request entstand (aus dem Login-State). null = anonym (Session-basiert,
   *  darf unter jedem Login rausgehen). Verhindert, dass A's gemerkte Lösungen unter B's Bearer
   *  gesendet werden, wenn auf einem geteilten Gerät der Nutzer wechselt. */
  userId?: number | null;
}

export const OFFLINE_QUEUE_KEY = 'rookhub_offline_queue';

/**
 * Abstand zwischen zwei Replays. FALLE: nach einer längeren Offline-Session liegen leicht 40+
 * Einträge in der Queue; ungebremst hintereinander gefeuert reißen sie das serverseitige
 * Rate-Limit (100 Req/min pro IP, anonyme Puzzle-Routen 30/min) und der Server antwortet 429.
 */
export const OFFLINE_QUEUE_THROTTLE_MS = 300;

/**
 * Harte Obergrenze der Queue. Begründung: jeder enqueue schreibt die GANZE Queue neu in den
 * (auf wenige MB gedeckelten) localStorage — unbegrenztes Wachstum macht jedes weitere
 * Vormerken teurer und frisst den Platz der Offline-Caches. 200 Einträge sind deutlich mehr
 * als eine realistische Offline-Session (ein Eintrag je gelöstem Puzzle). Läuft die Queue
 * dennoch voll, werden NEUE Einträge abgewiesen statt die ältesten still zu verdrängen:
 * Verdrängen wäre exakt der stille Verlust, den die Queue verhindern soll — beim Abweisen
 * weiß der Nutzer nach der (einmaligen) Warnung, dass ab jetzt nichts mehr gemerkt wird,
 * während beim Verdrängen längst gemerkte Lösungen unbemerkt verschwänden.
 */
export const OFFLINE_QUEUE_MAX = 200;

/**
 * Merkt sich schreibende Requests (Lösungs-/Versuchs-Aufzeichnungen), die offline nicht
 * rausgehen konnten, im localStorage und spielt sie bei Reconnect (window 'online' bzw.
 * App-Start) erneut über den HttpClient ein. Idempotent gegenüber Mehrfach-Flush: ein Eintrag
 * wird erst nach erfolgreicher Antwort entfernt; 4xx (dauerhaft fehlerhaft) wird verworfen,
 * Netzwerk-/5xx-Fehler lassen die Queue stehen (nächster Reconnect versucht es erneut).
 *
 * Replays laufen über den normalen authInterceptor → der aktuelle Bearer-Token wird angehängt.
 */
@Injectable({ providedIn: 'root' })
export class OfflineQueueService {
  private flushing = false;
  private seq = 0;

  constructor(private http: HttpClient, private zone: NgZone,
              private snackbar: SnackbarService, private translate: TranslateService) {
    window.addEventListener('online', () => this.zone.run(() => this.flush()));
    // App-Start: falls online, gleich versuchen (kurz verzögert, damit Auth/Token steht).
    if (typeof navigator !== 'undefined' && navigator.onLine) {
      setTimeout(() => this.flush(), 4000);
    }
  }

  /** Einen Request für später vormerken (wenn offline / Netzwerkfehler). Wird mit der aktuellen
   *  User-Id gestempelt, damit er beim Reconnect nur unter DEMSELBEN Konto rausgeht.
   *  Liefert `false`, wenn NICHTS gemerkt wurde (Queue voll / Speicher wirft) — der Verlust wird
   *  dann EINMAL sichtbar gemeldet statt still verschluckt (siehe {@link OFFLINE_QUEUE_MAX}). */
  enqueue(method: 'POST' | 'PUT', url: string, body: unknown): boolean {
    const q = this.read();
    if (q.length >= OFFLINE_QUEUE_MAX) { this.warnLossOnce(); return false; }
    q.push({ id: this.newId(), method, url, body, ts: Date.now(), userId: this.currentUserId() });
    if (!this.write(q)) { this.warnLossOnce(); return false; }
    return true;
  }

  /** Schon einmal sichtbar vor Verlust gewarnt (einmal je App-Lauf reicht — sonst nervt die
   *  Snackbar bei jeder weiteren Lösung, ohne neue Information zu liefern)? */
  private lossWarned = false;

  /** EINMALIGE sichtbare Warnung: neue Offline-Ergebnisse werden nicht mehr gemerkt. */
  private warnLossOnce(): void {
    if (this.lossWarned) return;
    this.lossWarned = true;
    this.snackbar.warn(this.translate.instant('app.offlineQueueFull'));
  }

  /** Aktuelle Login-User-Id aus dem gespeicherten Auth-State (ohne AuthService-Abhängigkeit, um
   *  keinen DI-Zyklus zu erzeugen). null = nicht eingeloggt / unlesbar. */
  private currentUserId(): number | null {
    try {
      const raw = localStorage.getItem('rookhub_user');
      const id = raw ? JSON.parse(raw)?.userId : null;
      return typeof id === 'number' ? id : null;
    } catch { return null; }
  }

  /** Darf dieser Eintrag jetzt gesendet werden? Anonyme (userId null/fehlt) immer; ein an einen
   *  Login gebundener Eintrag nur, wenn genau dieser User eingeloggt ist. */
  private eligible(r: PendingRequest, currentUserId: number | null): boolean {
    return r.userId == null || r.userId === currentUserId;
  }

  private newId(): string {
    try { return crypto.randomUUID(); } catch { return `${Date.now()}-${this.seq++}`; }
  }

  private retryTimer?: ReturnType<typeof setTimeout>;

  /** Einmaligen Backoff-Retry planen (für „online, aber Server gerade 5xx/weg/drosselt"). */
  private scheduleRetry(delayMs = 30000): void {
    if (this.retryTimer !== undefined) return;
    this.retryTimer = setTimeout(() => { this.retryTimer = undefined; this.flush(); }, delayMs);
  }

  /** Wartezeit aus dem `Retry-After`-Header (Sekunden ODER HTTP-Datum) des 429; gedeckelt auf
   *  1 s–5 min, damit ein kaputter Header den Nachlauf weder verschluckt noch ewig aufschiebt. */
  private retryAfterMs(e: { headers?: { get(name: string): string | null } }): number {
    const raw = e?.headers?.get?.('Retry-After');
    if (!raw) return 30000;
    const secs = Number(raw);
    const ms = Number.isFinite(secs) ? secs * 1000 : Date.parse(raw) - Date.now();
    if (!Number.isFinite(ms) || ms <= 0) return 30000;
    return Math.min(Math.max(ms, 1000), 300000);
  }

  /** Anzahl noch ausstehender Requests. */
  pendingCount(): number {
    return this.read().length;
  }

  /** Alle vorgemerkten Requests verwerfen. */
  clear(): void {
    try { localStorage.removeItem(OFFLINE_QUEUE_KEY); } catch { /* ignore */ }
  }

  /** Vorgemerkte Requests der Reihe nach erneut senden. Nur Einträge des aktuell eingeloggten
   *  Users (bzw. anonyme) gehen raus; fremde bleiben liegen, bis IHR User wieder eingeloggt ist. */
  flush(): void {
    if (this.flushing) return;
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    const q = this.read();
    if (q.length === 0) return;
    this.flushing = true;
    this.sendNext(q, 0, this.currentUserId());
  }

  private sendNext(q: PendingRequest[], i: number, currentUserId: number | null): void {
    if (i >= q.length) { this.flushing = false; return; }
    const r = q[i];
    // Fremd-User-Eintrag: NICHT senden und NICHT entfernen — zum nächsten weitergehen.
    if (!this.eligible(r, currentUserId)) { this.sendNext(q, i + 1, currentUserId); return; }
    this.http.request(r.method, r.url, { body: r.body }).subscribe({
      next: () => { this.remove(r.id); this.sendThrottled(q, i + 1, currentUserId); },
      error: (e: { status?: number; headers?: { get(name: string): string | null } }) => {
        const status = e?.status ?? 0;
        // FALLE: 429 (Rate-Limit) und 408 sind KEINE dauerhaften Fehler — würden sie wie 4xx
        // verworfen, löschte ein großer Nachlauf genau die Lösungen, die die Queue schützen soll.
        const permanent = status >= 400 && status < 500 && status !== 429 && status !== 408;
        if (permanent) {
          // Dauerhaft fehlerhaft (z.B. Puzzle weg / nicht mehr berechtigt) → verwerfen.
          this.remove(r.id);
          this.sendThrottled(q, i + 1, currentUserId);
        } else {
          // Netzwerk (0), 429/408 oder 5xx → Queue stehen lassen + Backoff-Retry planen (sonst
          // bliebe sie bei durchgehend „online" bis zum nächsten App-Start/online-Event liegen).
          this.flushing = false;
          this.scheduleRetry(status === 429 ? this.retryAfterMs(e) : 30000);
        }
      },
    });
  }

  /** Nächsten Eintrag mit Abstand senden (siehe OFFLINE_QUEUE_THROTTLE_MS). */
  private sendThrottled(q: PendingRequest[], i: number, currentUserId: number | null): void {
    if (i >= q.length) { this.flushing = false; return; }
    setTimeout(() => this.sendNext(q, i, currentUserId), OFFLINE_QUEUE_THROTTLE_MS);
  }

  private read(): PendingRequest[] {
    try {
      const raw = localStorage.getItem(OFFLINE_QUEUE_KEY);
      const arr = raw ? JSON.parse(raw) : [];
      return Array.isArray(arr) ? arr : [];
    } catch { return []; }
  }

  /** Queue schreiben; `false` = nichts geschrieben (Quota/Privatmodus) — der Aufrufer entscheidet,
   *  ob das ein stiller Zustand (remove nach Erfolg) oder ein meldepflichtiger Verlust (enqueue) ist. */
  private write(q: PendingRequest[]): boolean {
    try { localStorage.setItem(OFFLINE_QUEUE_KEY, JSON.stringify(q)); return true; }
    catch { return false; /* Quota */ }
  }

  private remove(id: string): void {
    this.write(this.read().filter(r => r.id !== id));
  }
}
