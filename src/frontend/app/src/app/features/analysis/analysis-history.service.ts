import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AnalysisTreeDto } from './analysis-tree';

/** Eine Analyse im Verlauf (0.603.0, seit 0.604.0 als Zugbaum) — siehe `AnalysisHistoryService` in der API.
 *  `moves` = Hauptlinie, `tree` fehlt in der Liste (nur beim Abruf eines Eintrags und nach dem Speichern). */
export interface AnalysisHistoryEntry {
  id: number; startFen: string; moves: string[]; ply: number; title: string | null;
  preview: string; moveCount: number; nodeCount: number; starCount: number;
  tree: AnalysisTreeDto | null; current: number; createdAt: string; updatedAt: string;
}

/** `current` = Index des Knotens in `tree.n`, an dem man steht (-1 = Ausgangsstellung). */
export interface SaveAnalysisHistoryRequest {
  id: number | null; startFen: string; title: string | null; tree: AnalysisTreeDto; current: number;
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
