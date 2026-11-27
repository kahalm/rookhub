import { Injectable, InjectionToken, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { HttpHeaders } from '@angular/common/http';
import { ExternalRoundView, LiveState, LiveStatus, LiveStreamEvent, LiveViewResponse, LiveViewState } from './live-boards.models';

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

  /** Fremde Runde + Partien zu einem Lichess-Link (Dialog „Brett hinzufügen“, 1.025.0; ohne Anmeldung). */
  /** PGN einer Live-Partie (alle bisherigen Züge, Uhren) — Zugliste im Vollbild (1.036.0). */
  pgn(roundId: string, gameId: string): Observable<string> {
    return this.http.get(`/api/live-boards/pgn/${encodeURIComponent(roundId)}/${encodeURIComponent(gameId)}`, { responseType: 'text' });
  }

  external(url: string): Observable<ExternalRoundView> {
    return this.http.get<ExternalRoundView>('/api/live-boards/external', { params: { url } });
  }

  /**
   * `extra` = fremde Bretter `roundId.gameId` — laufen im SELBEN Strom mit (eine EventSource je Seite). `view` = Filter der
   * Ansicht (1.027.0): der Server bewertet nur die Bretter, die dieser Zuschauer sieht (Mannschaft/Spieler + hinzugefügte).
   */
  stream(id: string, round: number, withEval: boolean, extra: readonly string[] = [],
         view: { team?: string | null; player?: string | null; add?: readonly string[] } = {}): Observable<LiveStreamEvent> {
    const filter = [
      view.team ? `team=${encodeURIComponent(view.team)}` : '',
      view.player ? `player=${encodeURIComponent(view.player)}` : '',
      view.add?.length ? `add=${view.add.map(encodeURIComponent).join(',')}` : '',
    ];
    // `ngsw-bypass` (1.025.0): sonst läuft der Strom im Prod-Build durch den Service Worker (ngsw reicht jede nicht
    // gecachte Anfrage per respondWith durch) — Chrome beendet einen untätigen Worker nach ~30 s, die Verbindung riss
    // alle 29–60 s ab und die Seite zeigte „Verbindung unterbrochen“, obwohl der Server-Strom hielt (Prod-Log 26.11.).
    const q = [withEval ? 'eval=true' : '', ...filter, extra.length ? `extra=${extra.map(encodeURIComponent).join(',')}` : '', 'ngsw-bypass=1']
      .filter(Boolean).join('&');
    return this.events(`/api/live-boards/${encodeURIComponent(id)}/rounds/${round}/stream?${q}`);
  }

  // ── Geteilte, lebende Ansicht (1.029.0) ──────────────────────────────────────────────────────────────────────────

  private keyHeaders(key: string | null | undefined): { headers?: HttpHeaders } {
    return key ? { headers: new HttpHeaders({ 'X-Live-View-Key': key }) } : {};
  }

  createView(tournamentId: string, view: LiveViewState): Observable<LiveViewResponse> {
    return this.http.post<LiveViewResponse>('/api/live-boards/views', { tournamentId, view });
  }

  getView(token: string, key: string | null): Observable<LiveViewResponse> {
    return this.http.get<LiveViewResponse>(`/api/live-boards/views/${encodeURIComponent(token)}`, this.keyHeaders(key));
  }

  updateView(token: string, view: LiveViewState, key: string | null): Observable<LiveViewResponse> {
    return this.http.put<LiveViewResponse>(`/api/live-boards/views/${encodeURIComponent(token)}`, { view }, this.keyHeaders(key));
  }

  /**
   * Strom einer geteilten Ansicht: der Server schickt `view` und hängt bei jeder Änderung des Besitzers selbst um (neuer
   * `view` + `snapshot`) — der Browser verbindet NICHT neu. `evalOverride` = eigener Balken-Schalter dieses Zuschauers.
   */
  viewStream(token: string, evalOverride: boolean | null): Observable<LiveStreamEvent> {
    const q = [evalOverride === null ? '' : `eval=${evalOverride}`, 'ngsw-bypass=1'].filter(Boolean).join('&');
    return this.events(`/api/live-boards/views/${encodeURIComponent(token)}/stream?${q}`);
  }

  private events(url: string): Observable<LiveStreamEvent> {
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
        es.addEventListener('view', (e: Event) => {
          const d = parse(e as MessageEvent) as Omit<Extract<LiveStreamEvent, { type: 'view' }>, 'type'> | null;
          if (d?.view) sub.next({ type: 'view', ...d });
        });
        es.addEventListener('view-gone', () => {
          sub.next({ type: 'view-gone' });
          closed = true;
          es.close();
        });
        // Fremde Runden (1.025.0): je Ereignis mit `round` = Lichess-Runde.
        for (const type of ['ext-snapshot', 'ext-game', 'ext-state', 'ext-error'] as const) {
          es.addEventListener(type, (e: Event) => {
            const d = parse(e as MessageEvent) as { round?: unknown } | null;
            if (d && typeof d.round === 'string') sub.next({ type, ...d } as LiveStreamEvent);
          });
        }
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
