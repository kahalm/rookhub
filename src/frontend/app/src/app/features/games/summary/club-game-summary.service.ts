import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Stand der Zusammenfassung einer Vereinspartie (0.913.0, `GET /api/league/club/games/{id}/summary`). */
export interface ClubGameSummary {
  status: 'none' | 'requested' | 'running' | 'done' | 'failed';
  /** Letzte fertige Fassung — bleibt stehen, während eine neue angefordert ist. */
  markdown: string | null;
  doneAt: string | null;
  requestedAt: string | null;
  /** Solange Claude schreibt: seit wie vielen Sekunden (vom Server gerechnet). */
  runningSeconds?: number | null;
  /** Die Partie wurde nach dem Schreiben korrigiert. */
  stale: boolean;
  model: string | null;
  error: string | null;
  /** Nur bei offener Anforderung (0.914.0): wie weit die Hintergrund-Rechnung ist; Claude schreibt erst bei `ready`. */
  readiness?: ClubGameSummaryReadiness | null;
  /** Eigene Partie (0.923.0): `club` = der Text gehört zur verbundenen Vereinspartie. */
  source?: 'own' | 'club';
  /** Eigene Partie: heute noch mögliche Anforderungen; `null` = unbegrenzt (Admin). */
  quotaLeft?: number | null;
  quotaLimit?: number;
}

export interface ClubGameSummaryReadiness {
  ready: boolean;
  hasAnalysis: boolean;
  analysisDone: boolean;
  refining: boolean;
  refinedPositions: number;
  totalPositions: number;
  comparisonsPlanned: boolean;
  comparisonsDone: number;
  comparisonsTotal: number;
  /** Stellungen, die der schnelle Durchgang schon gerechnet hat (0.919.3). */
  analyzedPositions?: number;
}

@Injectable({ providedIn: 'root' })
export class ClubGameSummaryService {
  private http = inject(HttpClient);

  get(gameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/league/club/games/${gameId}/summary`);
  }

  /** Vereinspartie über den Teilen-Link, nur lesend (Wunsch 2026-11-09): nur der fertige Text. */
  getShared(token: string, gameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/league/s/${encodeURIComponent(token)}/club/games/${gameId}/summary`);
  }

  /** Eigene Partie (0.923.0, `GET /api/games/{id}/summary`). */
  getOwn(savedGameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/games/${savedGameId}/summary`);
  }

  /** Eigene Partie anfordern — Kontingent je Tag, Server 429 wenn verbraucht. */
  requestOwn(savedGameId: number, lang: string): Observable<ClubGameSummary> {
    return this.http.post<ClubGameSummary>(`/api/games/${savedGameId}/summary`, { lang });
  }

  /** Anfordern bzw. neu anfordern — nur Admins (Server: 403 sonst). */
  request(gameId: number): Observable<ClubGameSummary> {
    return this.http.post<ClubGameSummary>(`/api/league/club/games/${gameId}/summary`, {});
  }
}
