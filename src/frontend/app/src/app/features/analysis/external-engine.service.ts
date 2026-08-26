import { Injectable } from '@angular/core';
import { HttpClient, HttpDownloadProgressEvent, HttpEventType } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Eine auf dem Lichess-Konto registrierte External Engine (ohne clientSecret — bleibt serverseitig). */
export interface ExternalEngineInfo {
  id: string;
  name: string;
  maxThreads: number;
  maxHash: number;
}

export interface ExternalEnginesResponse {
  hasCredentials: boolean;
  /** Lichess hat den gespeicherten Token abgewiesen (ungültig/abgelaufen/falscher Scope). */
  tokenInvalid: boolean;
  engines: ExternalEngineInfo[];
  /** Im Profil gewählte Hintergrund-Engine für Analyseaufträge (der Live-Picker blendet sie aus). */
  backgroundEngineId?: string | null;
}

export interface EngineCredentialStatus {
  hasCredentials: boolean;
  maskedToken: string | null;
}

/** Work-Parameter der Analyse (Lichess-ExternalEngineWork, serverseitig geklemmt). */
export interface EngineAnalyseWork {
  sessionId: string;
  initialFen: string;
  moves: string[];
  multiPv: number;
  depth: number;
  threads?: number;
  hash?: number;
}

/** Eine ndjson-Zeile des Analyse-Streams. cp/mate sind laut Spez. aus Weiß-Sicht. */
export interface EngineAnalyseLine {
  time: number;
  depth: number;
  nodes: number;
  pvs: { depth?: number; cp?: number; mate?: number; moves?: string[] }[];
}

/**
 * External-Engine-Anbindung (Lichess-Client-Modus): Token-Verwaltung + Engine-Liste + Analyse-
 * Stream über den RookHub-Proxy (<c>/api/engine/*</c>). Der ndjson-Stream läuft über den normalen
 * HttpClient (XHR + <c>reportProgress</c> ⇒ <c>partialText</c> wächst mit) — damit greifen die
 * Interceptors (Auth!) und die Antwortgröße bleibt bei tiefen-limitierter Suche überschaubar.
 */
@Injectable({ providedIn: 'root' })
export class ExternalEngineService {
  constructor(private http: HttpClient) {}

  getCredentials(): Observable<EngineCredentialStatus> {
    return this.http.get<EngineCredentialStatus>('/api/engine/credentials');
  }

  saveToken(token: string): Observable<EngineCredentialStatus> {
    return this.http.post<EngineCredentialStatus>('/api/engine/credentials', { token });
  }

  deleteToken(): Observable<void> {
    return this.http.delete<void>('/api/engine/credentials');
  }

  listEngines(): Observable<ExternalEnginesResponse> {
    return this.http.get<ExternalEnginesResponse>('/api/engine/external');
  }

  /** Hintergrund-Engine festlegen (null = entfernen). */
  setBackgroundEngine(engineId: string | null): Observable<{ backgroundEngineId: string | null }> {
    return this.http.put<{ backgroundEngineId: string | null }>('/api/engine/background', { engineId });
  }

  /**
   * Startet eine Analyse und emittet jede vollständige ndjson-Zeile als geparstes Objekt;
   * complete = Suche regulär beendet (Tiefe erreicht). Unsubscribe bricht die Suche ab —
   * der Abbruch wandert über den Proxy zum Broker, der Provider stoppt die Engine.
   */
  analyse(engineId: string, work: EngineAnalyseWork): Observable<EngineAnalyseLine> {
    return new Observable<EngineAnalyseLine>(subscriber => {
      // Bereits geparster Präfix von partialText — nur NEUE vollständige Zeilen verarbeiten.
      let parsedUpTo = 0;
      const emitLines = (text: string, final: boolean) => {
        const end = final ? text.length : text.lastIndexOf('\n');
        if (end <= parsedUpTo) return;
        for (const raw of text.slice(parsedUpTo, end).split('\n')) {
          const line = raw.trim();
          if (!line) continue;
          try { subscriber.next(JSON.parse(line) as EngineAnalyseLine); } catch { /* halbe/kaputte Zeile ignorieren */ }
        }
        parsedUpTo = end;
      };

      const sub = this.http.post(`/api/engine/external/${encodeURIComponent(engineId)}/analyse`, work, {
        observe: 'events',
        responseType: 'text',
        reportProgress: true,
      }).subscribe({
        next: ev => {
          if (ev.type === HttpEventType.DownloadProgress) {
            emitLines((ev as HttpDownloadProgressEvent).partialText ?? '', false);
          } else if (ev.type === HttpEventType.Response) {
            emitLines((ev.body as string | null) ?? '', true);
            subscriber.complete();
          }
        },
        error: err => subscriber.error(err),
      });
      return () => sub.unsubscribe();
    });
  }
}
