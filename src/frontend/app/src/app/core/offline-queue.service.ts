import { Injectable, NgZone } from '@angular/core';
import { HttpClient } from '@angular/common/http';

/** Ein aufgeschobener (offline fehlgeschlagener) schreibender Request. */
interface PendingRequest {
  id: string;
  method: 'POST' | 'PUT';
  url: string;
  body: unknown;
  ts: number;
}

export const OFFLINE_QUEUE_KEY = 'rookhub_offline_queue';

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

  constructor(private http: HttpClient, private zone: NgZone) {
    window.addEventListener('online', () => this.zone.run(() => this.flush()));
    // App-Start: falls online, gleich versuchen (kurz verzögert, damit Auth/Token steht).
    if (typeof navigator !== 'undefined' && navigator.onLine) {
      setTimeout(() => this.flush(), 4000);
    }
  }

  /** Einen Request für später vormerken (wenn offline / Netzwerkfehler). */
  enqueue(method: 'POST' | 'PUT', url: string, body: unknown): void {
    const q = this.read();
    q.push({ id: `${Date.now()}-${this.seq++}`, method, url, body, ts: Date.now() });
    this.write(q);
  }

  /** Anzahl noch ausstehender Requests. */
  pendingCount(): number {
    return this.read().length;
  }

  /** Alle vorgemerkten Requests verwerfen. */
  clear(): void {
    try { localStorage.removeItem(OFFLINE_QUEUE_KEY); } catch { /* ignore */ }
  }

  /** Vorgemerkte Requests der Reihe nach erneut senden. */
  flush(): void {
    if (this.flushing) return;
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    const q = this.read();
    if (q.length === 0) return;
    this.flushing = true;
    this.sendNext(q, 0);
  }

  private sendNext(q: PendingRequest[], i: number): void {
    if (i >= q.length) { this.flushing = false; return; }
    const r = q[i];
    this.http.request(r.method, r.url, { body: r.body }).subscribe({
      next: () => { this.remove(r.id); this.sendNext(q, i + 1); },
      error: (e: { status?: number }) => {
        const status = e?.status ?? 0;
        if (status >= 400 && status < 500) {
          // Dauerhaft fehlerhaft (z.B. Puzzle weg / nicht mehr berechtigt) → verwerfen.
          this.remove(r.id);
          this.sendNext(q, i + 1);
        } else {
          // Netzwerk (0) oder 5xx → Queue stehen lassen, später erneut.
          this.flushing = false;
        }
      },
    });
  }

  private read(): PendingRequest[] {
    try {
      const raw = localStorage.getItem(OFFLINE_QUEUE_KEY);
      const arr = raw ? JSON.parse(raw) : [];
      return Array.isArray(arr) ? arr : [];
    } catch { return []; }
  }

  private write(q: PendingRequest[]): void {
    try { localStorage.setItem(OFFLINE_QUEUE_KEY, JSON.stringify(q)); } catch { /* Quota */ }
  }

  private remove(id: string): void {
    this.write(this.read().filter(r => r.id !== id));
  }
}
