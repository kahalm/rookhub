import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Eine Analyse im Verlauf (0.603.0) — siehe `AnalysisHistoryService` in der API. */
export interface AnalysisHistoryEntry {
  id: number; startFen: string; moves: string[]; ply: number; title: string | null; starred: number[];
  preview: string; moveCount: number; createdAt: string; updatedAt: string;
}

export interface SaveAnalysisHistoryRequest {
  id: number | null; startFen: string; moves: string[]; ply: number; title: string | null; starred: number[];
}

@Injectable({ providedIn: 'root' })
export class AnalysisHistoryService {
  private readonly http = inject(HttpClient);

  list(): Observable<AnalysisHistoryEntry[]> {
    return this.http.get<AnalysisHistoryEntry[]>('/api/analysis-history');
  }

  get(id: number): Observable<AnalysisHistoryEntry> {
    return this.http.get<AnalysisHistoryEntry>(`/api/analysis-history/${id}`);
  }

  save(req: SaveAnalysisHistoryRequest): Observable<AnalysisHistoryEntry> {
    return this.http.post<AnalysisHistoryEntry>('/api/analysis-history', req);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/analysis-history/${id}`);
  }
}
