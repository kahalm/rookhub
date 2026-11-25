import { Injectable, InjectionToken, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { LiveState, LiveStatus, LiveStreamEvent } from './live-boards.models';

/** Baut die EventSource (Tests ersetzen sie). Chrome 111 (Boox) kann EventSource seit Jahren. */
export const EVENT_SOURCE_FACTORY = new InjectionToken<(url: string) => EventSource>('EVENT_SOURCE_FACTORY', {
  providedIn: 'root',
  factory: () => (url: string) => new EventSource(url),
});

/** Wartezeiten bis zum nächsten Versuch, wenn der Browser die SSE-Verbindung aufgibt (404/429/Server weg). */
export const LIVE_RETRY_MS = [3000, 10000, 30000];

/**
 * Live-Bretter: Verknüpfung lesen/setzen und der Live-Strom einer Runde (Server-Sent Events, `/api/live-boards/…/stream`).
 * Die EventSource verbindet sich nach einem Abriss selbst neu (der Server schickt `retry: 5000`); gibt sie auf
 * (`readyState` CLOSED — z. B. 429 bei zu vielen Verbindungen), meldet der Strom `offline` und versucht es nach
 * {@link LIVE_RETRY_MS} erneut. Kein Token im Strom: die Live-Bretter sind ohne Anmeldung lesbar.
 */
@Injectable({ providedIn: 'root' })
export class LiveBoardsService {
  private readonly http = inject(HttpClient);
  private readonly makeSource = inject(EVENT_SOURCE_FACTORY);

  status(id: string): Observable<LiveStatus> {
    return this.http.get<LiveStatus>(`/api/live-boards/${encodeURIComponent(id)}`);
  }

  link(id: string, url: string): Observable<LiveStatus> {
    return this.http.put<LiveStatus>(`/api/live-boards/${encodeURIComponent(id)}/link`, { url });
  }

  stream(id: string, round: number, withEval: boolean): Observable<LiveStreamEvent> {
    const url = `/api/live-boards/${encodeURIComponent(id)}/rounds/${round}/stream${withEval ? '?eval=1' : ''}`;
    return new Observable<LiveStreamEvent>(sub => {
      let source: EventSource | null = null;
      let timer: ReturnType<typeof setTimeout> | null = null;
      let attempt = 0;
      let closed = false;
      const parse = (e: MessageEvent): unknown => {
        try { return JSON.parse(e.data as string); } catch { return null; }
      };
      const open = () => {
        if (closed) return;
        const es = source = this.makeSource(url);
        es.addEventListener('snapshot', (e: Event) => {
          const d = parse(e as MessageEvent) as Omit<Extract<LiveStreamEvent, { type: 'snapshot' }>, 'type'> | null;
          attempt = 0;
          if (d) sub.next({ type: 'snapshot', ...d });
        });
        es.addEventListener('layout', (e: Event) => {
          const d = parse(e as MessageEvent);
          if (d) sub.next({ type: 'layout', layout: d as Extract<LiveStreamEvent, { type: 'layout' }>['layout'] });
        });
        es.addEventListener('game', (e: Event) => {
          const d = parse(e as MessageEvent);
          if (d) sub.next({ type: 'game', game: d as Extract<LiveStreamEvent, { type: 'game' }>['game'] });
        });
        es.addEventListener('state', (e: Event) => {
          const d = parse(e as MessageEvent) as { state: LiveState } | null;
          if (d?.state) sub.next({ type: 'state', state: d.state });
        });
        es.onerror = () => {
          if (closed) return;
          // CONNECTING = die EventSource versucht es selbst noch einmal; CLOSED = aufgegeben → eigener Versuch später.
          if (es.readyState === 2) {
            es.close();
            sub.next({ type: 'state', state: 'offline' });
            const wait = LIVE_RETRY_MS[Math.min(attempt++, LIVE_RETRY_MS.length - 1)];
            timer = setTimeout(open, wait);
          } else {
            sub.next({ type: 'state', state: 'reconnecting' });
          }
        };
      };
      open();
      return () => {
        closed = true;
        if (timer) clearTimeout(timer);
        source?.close();
      };
    });
  }
}
