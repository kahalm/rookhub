import { Injectable, NgZone, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from './snackbar.service';
import { localStore, readJson, removeKey, writeJson } from './local-json-store';

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
 * Nachsende-Takt im Online-Zustand (GL-FE1-01/F1-013): scheitert ein Schreibweg, obwohl das Gerät als online gilt
 * (API-Neustart/502, ngsw-504, DB-Stall), merkt der Aufrufer ihn vor — ohne eigenen Anstoß ginge er erst beim
 * nächsten 'online'-Ereignis oder App-Start raus, und Woodpecker lieferte bis dahin dieselbe Aufgabe in Schleife.
 * Deshalb plant {@link OfflineQueueService.enqueue} online einen Nachlauf nach dem ersten Wert; jeder weitere
 * gescheiterte Nachlauf wartet den nächsten Wert, ab dem letzten bleibt es bei ihm (Obergrenze, kein Sturm: es
 * gibt immer höchstens EINEN geplanten Nachlauf). Ein erfolgreicher Versand setzt den Takt zurück.
 */
export const OFFLINE_QUEUE_RETRY_BACKOFF_MS: readonly number[] = [5000, 15000, 60000];

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
export class OfflineQueueService implements OnDestroy {
  private flushing = false;
  private seq = 0;
  private readonly onOnline = () => this.zone.run(() => this.flush());
  private startTimer?: ReturnType<typeof setTimeout>;

  constructor(private http: HttpClient, private zone: NgZone,
              private snackbar: SnackbarService, private translate: TranslateService) {
    window.addEventListener('online', this.onOnline);
    // App-Start: falls online, gleich versuchen (kurz verzögert, damit Auth/Token steht).
    if (typeof navigator !== 'undefined' && navigator.onLine) {
      this.startTimer = setTimeout(() => { this.startTimer = undefined; this.flush(); }, 4000);
    }
  }

  /** Root-Dienst: räumt Listener und geplante Nachläufe ab (App-Ende, TestBed-Abbau) — sonst feuerten sie danach. */
  ngOnDestroy(): void {
    window.removeEventListener('online', this.onOnline);
    if (this.startTimer !== undefined) clearTimeout(this.startTimer);
    this.cancelRetry();
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
    // Online vorgemerkt = der Server hat gerade gestört, nicht das Netz: bald selbst nachsenden (GL-FE1-01).
    if (typeof navigator !== 'undefined' && navigator.onLine) this.scheduleRetry(OFFLINE_QUEUE_RETRY_BACKOFF_MS[0]);
    return true;
  }

  /** Wie {@link enqueue}, verwirft aber vorher frühere Einträge mit derselben Methode + URL des aktuellen Kontos —
   *  für Zustands-Schreibwege (ganzer Stand je PUT), bei denen nur der LETZTE zählt (Endless-Spielstand, G3-002). */
  enqueueLatest(method: 'POST' | 'PUT', url: string, body: unknown): boolean {
    this.drop(method, url);
    return this.enqueue(method, url, body);
  }

  /** Vorgemerkte Einträge mit dieser Methode + URL (des aktuellen Kontos bzw. anonym) verwerfen — z. B. nachdem ein
   *  jüngerer Stand direkt angekommen ist; sonst überschriebe ihn der spätere Nachlauf mit dem älteren. */
  drop(method: 'POST' | 'PUT', url: string): void {
    const q = this.read();
    const uid = this.currentUserId();
    const kept = q.filter(r => !(r.method === method && r.url === url && this.eligible(r, uid)));
    if (kept.length !== q.length) this.write(kept);
  }

  /** Liegt für Methode + URL (aktuelles Konto bzw. anonym) noch etwas in der Warteschlange? */
  has(method: 'POST' | 'PUT', url: string): boolean {
    const uid = this.currentUserId();
    return this.read().some(r => r.method === method && r.url === url && this.eligible(r, uid));
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
      const id = readJson<{ userId?: unknown }>(localStore(), 'rookhub_user')?.userId;
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
  /** Gescheiterte Nachläufe seit dem letzten Erfolg; die erste Stufe des Takts gehört dem Vormerken. */
  private retryStep = 0;

  /** Einmaligen Backoff-Retry planen (für „online, aber Server gerade 5xx/weg/drosselt"). Ist schon einer
   *  geplant, bleibt es bei ihm — höchstens EIN Timer, egal wie oft vorgemerkt wird. */
  private scheduleRetry(delayMs: number): void {
    if (this.retryTimer !== undefined) return;
    this.retryTimer = setTimeout(() => { this.retryTimer = undefined; this.flush(); }, delayMs);
  }

  /** Nächste Wartezeit des Takts nach einem gescheiterten Nachlauf (steigt bis zur Obergrenze). */
  private nextBackoffMs(): number {
    const steps = OFFLINE_QUEUE_RETRY_BACKOFF_MS;
    const ms = steps[Math.min(this.retryStep + 1, steps.length - 1)];
    this.retryStep++;
    return ms;
  }

  private cancelRetry(): void {
    if (this.retryTimer !== undefined) { clearTimeout(this.retryTimer); this.retryTimer = undefined; }
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
    removeKey(localStore(), OFFLINE_QUEUE_KEY);
  }

  /** Vorgemerkte Requests der Reihe nach erneut senden. Nur Einträge des aktuell eingeloggten
   *  Users (bzw. anonyme) gehen raus; fremde bleiben liegen, bis IHR User wieder eingeloggt ist. */
  flush(): void {
    if (this.flushing) return;
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    const q = this.read();
    if (q.length === 0) return;
    // Läuft jetzt ohnehin: ein geplanter Nachlauf ist überholt (scheitert dieser, plant er neu).
    this.cancelRetry();
    this.flushing = true;
    this.sendNext(q, 0, this.currentUserId());
  }

  private sendNext(q: PendingRequest[], i: number, currentUserId: number | null): void {
    if (i >= q.length) { this.flushing = false; return; }
    const r = q[i];
    // Fremd-User-Eintrag: NICHT senden und NICHT entfernen — zum nächsten weitergehen.
    if (!this.eligible(r, currentUserId)) { this.sendNext(q, i + 1, currentUserId); return; }
    this.http.request(r.method, r.url, { body: r.body }).subscribe({
      next: () => { this.retryStep = 0; this.remove(r.id); this.sendThrottled(q, i + 1, currentUserId); },
      error: (e: { status?: number; headers?: { get(name: string): string | null } }) => {
        const status = e?.status ?? 0;
        // 401/403 = ABGELAUFENE ANMELDUNG, kein dauerhaft fehlerhafter Eintrag. Sie mussten hier
        // heraus, weil der authInterceptor beim ersten 401 ausloggt: die folgenden Wiederholungen
        // gingen dann token-los raus, kassierten ebenfalls 401 — und die ganze Warteschlange war
        // verworfen. Ein Nutzer, der 30 Puzzles im Flugmodus gelöst hat, verlor sie alle ohne eine
        // einzige Meldung, genau den stillen Verlust, den die Queue verhindern soll. Deshalb: liegen
        // lassen und den Durchlauf SOFORT beenden (weitere Einträge scheitern ohnehin am selben
        // Token); nach dem nächsten Login greift der reguläre Flush.
        if (status === 401 || status === 403) {
          this.flushing = false;
          return;
        }
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
          this.scheduleRetry(status === 429 ? this.retryAfterMs(e) : this.nextBackoffMs());
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
    const arr = readJson<PendingRequest[]>(localStore(), OFFLINE_QUEUE_KEY);
    return Array.isArray(arr) ? arr : [];
  }

  /** Queue schreiben; `false` = nichts geschrieben (Quota/Privatmodus) — der Aufrufer entscheidet,
   *  ob das ein stiller Zustand (remove nach Erfolg) oder ein meldepflichtiger Verlust (enqueue) ist. */
  private write(q: PendingRequest[]): boolean {
    // `writeJson` meldet den Fehlschlag, statt ihn zu schlucken — genau die Unterscheidung, die
    // der Aufrufer hier braucht (stilles `remove` gegen meldepflichtigen Verlust beim Vormerken).
    return writeJson(localStore(), OFFLINE_QUEUE_KEY, q);
  }

  private remove(id: string): void {
    this.write(this.read().filter(r => r.id !== id));
  }
}
