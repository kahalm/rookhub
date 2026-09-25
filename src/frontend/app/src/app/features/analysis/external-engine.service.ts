import { Injectable } from '@angular/core';
import { HttpClient, HttpDownloadProgressEvent, HttpEventType } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Woher eine Engine kommt: direkt bei RookHub angemeldet (`rhe_…`, eigener Broker) oder über das
 *  Lichess-Konto (`eei_…`). */
export type ExternalEngineSource = 'rookhub' | 'lichess';

/** Eine External Engine des Kontos (ohne clientSecret — bleibt serverseitig). */
export interface ExternalEngineInfo {
  id: string;
  name: string;
  maxThreads: number;
  maxHash: number;
  /** Fehlt bei Antworten älterer Server → aus der Kennung abgeleitet (`engineSourceOf`). */
  source?: ExternalEngineSource;
  /** Nur für `rookhub`: hat der Provider in den letzten 30 s abgefragt? Für Lichess `null` (unbekannt). */
  online?: boolean | null;
}

/** Quelle einer Engine — aus dem Feld, sonst aus der Kennung (`rhe_` = RookHub direkt). */
export function engineSourceOf(e: Pick<ExternalEngineInfo, 'id' | 'source'>): ExternalEngineSource {
  return e.source ?? (e.id.startsWith('rhe_') ? 'rookhub' : 'lichess');
}

/** Direkt angemeldete Engine, deren Provider gerade nicht abfragt (Rechner aus). Bei Lichess wissen wir
 *  es nicht — dort nie „offline". */
export function isEngineOffline(e: Pick<ExternalEngineInfo, 'id' | 'source' | 'online'>): boolean {
  return engineSourceOf(e) === 'rookhub' && e.online === false;
}

/** Zusatz hinter dem Namen in den Engine-Auswahlen (i18n-Schlüssel oder `null`): „offline" für eine direkt
 *  angemeldete Engine ohne laufenden Provider, „über Lichess" für eine Lichess-Engine — die Auswahl mischt
 *  beide Quellen, und ein gleichnamiger Eintrag aus beiden wäre sonst nicht zu unterscheiden. */
export function engineTagKey(e: Pick<ExternalEngineInfo, 'id' | 'source' | 'online'>): string | null {
  if (isEngineOffline(e)) return 'analysis.engineOffline';
  return engineSourceOf(e) === 'lichess' ? 'analysis.engineViaLichess' : null;
}

export interface ExternalEnginesResponse {
  /** Ein LICHESS-Token ist hinterlegt. Direkt angemeldete Engines stehen auch ohne ihn in `engines`. */
  hasCredentials: boolean;
  /** Lichess hat den gespeicherten Token abgewiesen (ungültig/abgelaufen/falscher Scope). */
  tokenInvalid: boolean;
  engines: ExternalEngineInfo[];
  /** Im Profil gewählte Hintergrund-Engine für Analyseaufträge (der Live-Picker blendet sie aus). */
  /** Die hinterlegten Hintergrund-Engines. MEHRERE sind erlaubt: der Server rechnet je Engine
   *  einen Auftrag, es laufen also so viele nebeneinander, wie hier stehen. */
  backgroundEngineIds?: string[] | null;
  /** Stehen die eigenen Hintergrund-Engines auch fremden eingeworfenen Partien offen? */
  shareAsHouseEngine?: boolean;
  /** Darf dieses Konto das ueberhaupt entscheiden (nur Admin)? */
  canShareHouseEngine?: boolean;
  /** Lichess hat nicht geantwortet — `engines` enthält nur die direkt angemeldeten. */
  lichessUnreachable?: boolean;
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
 * External-Engine-Anbindung — zwei Quellen, eine Liste: direkt bei RookHub angemeldete Engines und die
 * des Lichess-Kontos. Token-Verwaltung (Lichess) + Engine-Liste + Analyse-Stream über den RookHub-Proxy
 * (<c>/api/engine/*</c>) — der Strom sieht für beide Quellen gleich aus. Der ndjson-Stream läuft über den normalen
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

  /** Registrierung einer direkt angemeldeten Engine entfernen (`rhe_…`). Läuft ihr Provider noch, meldet
   *  er sich beim nächsten Start wieder an. */
  deleteDirectEngine(id: string): Observable<void> {
    return this.http.delete<void>(`/api/external-engine/${encodeURIComponent(id)}`);
  }

  /** Hintergrund-Engine festlegen (null = entfernen). */
  /** Haus-Engine: die eigenen Hintergrund-Engines auch fremden Partien oeffnen, die jemand auf der
   *  Punktepartie-Seite einwirft. Nur ein Admin darf das setzen (der Server prueft es nochmal). */
  setHouseEngine(share: boolean): Observable<{ shareAsHouseEngine: boolean }> {
    return this.http.put<{ shareAsHouseEngine: boolean }>('/api/engine/house', { share });
  }

  setBackgroundEngines(engineIds: string[]): Observable<{ backgroundEngineIds: string[] }> {
    return this.http.put<{ backgroundEngineIds: string[] }>('/api/engine/background', { engineIds });
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
          let parsed: unknown;
          try { parsed = JSON.parse(line); } catch { continue; /* halbe/kaputte Zeile ignorieren */ }
          // Steuerzeilen des Brokers tragen keine Bewertung: der offizielle Provider schickt seit
          // d0eeb242 alle 15 s `{"keepalive":true}`, und der Broker reicht es als eigene Zeile durch.
          // Als Ergebnis weitergegeben, leerte jede davon während einer tiefen Suche die Linien.
          if (Array.isArray((parsed as Partial<EngineAnalyseLine> | null)?.pvs)) subscriber.next(parsed as EngineAnalyseLine);
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
