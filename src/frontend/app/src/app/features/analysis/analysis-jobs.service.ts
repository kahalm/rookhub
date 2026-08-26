import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type AnalysisJobStatus = 'queued' | 'running' | 'paused' | 'done' | 'failed';

/** Hintergrund-Analyseauftrag (Server: AnalysisJobs). `resultJson` = letzte Broker-Zeile, opak —
 *  wird mit `mapBrokerLine` genauso abgebildet wie der Live-Stream. */
export interface AnalysisJob {
  id: number;
  fen: string;
  title: string | null;
  engineId: string;
  targetDepth: number;
  multiPv: number;
  status: AnalysisJobStatus;
  reachedDepth: number;
  resultJson: string | null;
  secondsSpent: number;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
  lastRunAt: string | null;
  finishedAt: string | null;
}

export interface CreateAnalysisJobRequest {
  fen: string;
  targetDepth: number;
  multiPv: number;
  title?: string | null;
  engineId?: string | null;
}

export interface UpdateAnalysisJobRequest {
  targetDepth?: number;
  multiPv?: number;
  title?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AnalysisJobsService {
  constructor(private http: HttpClient) {}

  list(): Observable<AnalysisJob[]> {
    return this.http.get<AnalysisJob[]>('/api/analysis-jobs');
  }

  create(req: CreateAnalysisJobRequest): Observable<AnalysisJob> {
    return this.http.post<AnalysisJob>('/api/analysis-jobs', req);
  }

  update(id: number, req: UpdateAnalysisJobRequest): Observable<AnalysisJob> {
    return this.http.put<AnalysisJob>(`/api/analysis-jobs/${id}`, req);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/analysis-jobs/${id}`);
  }
}
