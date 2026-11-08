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
  /** Die Partie wurde nach dem Schreiben korrigiert. */
  stale: boolean;
  model: string | null;
  error: string | null;
  /** Nur bei offener Anforderung (0.914.0): wie weit die Hintergrund-Rechnung ist; Claude schreibt erst bei `ready`. */
  readiness?: ClubGameSummaryReadiness | null;
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
}

@Injectable({ providedIn: 'root' })
export class ClubGameSummaryService {
  private http = inject(HttpClient);

  get(gameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/league/club/games/${gameId}/summary`);
  }

  /** Anfordern bzw. neu anfordern — nur Admins (Server: 403 sonst). */
  request(gameId: number): Observable<ClubGameSummary> {
    return this.http.post<ClubGameSummary>(`/api/league/club/games/${gameId}/summary`, {});
  }
}
