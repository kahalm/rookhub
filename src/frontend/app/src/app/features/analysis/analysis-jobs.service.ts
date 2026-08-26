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
  /** Bewertung der Hauptvariante („+0.35"), serverseitig aus der Ergebniszeile abgeleitet. */
  evalText?: string | null;
  /** Tiefe der zuletzt empfangenen Zeile des LAUFENDEN Laufs (0 = rechnet gerade nicht). Nach einer
   *  Fortsetzung liegt sie unter `reachedDepth`, bis die Engine wieder aufgeholt hat. */
  currentDepth?: number;
  /** Suchtempo des laufenden Laufs in Knoten/Sekunde (0 = kein Messwert). */
  currentNps?: number;
  secondsSpent: number;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
  lastRunAt: string | null;
  finishedAt: string | null;
}

/** Laufender Stand EINES rechnenden Auftrags. Kommt aus dem Arbeitsspeicher der API (keine DB), damit
 *  die Liste ihn im Sekundentakt holen kann: `seconds` wächst aus der Startzeit des Laufs — also auch
 *  dann, wenn die Engine gerade schweigt und keine neue Zeile liefert. */
export interface AnalysisJobLive {
  id: number;
  depth: number;
  nps: number;
  seconds: number;
}

export interface CreateAnalysisJobRequest {
  fen: string;
  targetDepth: number;
  multiPv: number;
  title?: string | null;
  engineId?: string | null;
}

export interface CreateAnalysisJobsBatchRequest {
  fens: string[];
  targetDepth: number;
  multiPv: number;
  engineId?: string | null;
}

/** Batch-Ergebnis: angelegte Aufträge + übersprungene Stellungen (invalid / duplicate / limit). */
export interface AnalysisJobBatchResult {
  created: AnalysisJob[];
  skipped: { fen: string; reason: 'invalid' | 'duplicate' | 'limit' }[];
}

export interface UpdateAnalysisJobRequest {
  targetDepth?: number;
  multiPv?: number;
  title?: string | null;
  /** Andere Engine — bricht den laufenden Lauf ab und reiht neu ein (Ergebnis bleibt). */
  engineId?: string;
}

@Injectable({ providedIn: 'root' })
export class AnalysisJobsService {
  constructor(private http: HttpClient) {}

  list(): Observable<AnalysisJob[]> {
    return this.http.get<AnalysisJob[]>('/api/analysis-jobs');
  }

  /** Nur Tiefe/Tempo/Zeit der gerade rechnenden Aufträge (winzige Antwort, für den Sekundentakt). */
  live(): Observable<AnalysisJobLive[]> {
    return this.http.get<AnalysisJobLive[]>('/api/analysis-jobs/live');
  }

  create(req: CreateAnalysisJobRequest): Observable<AnalysisJob> {
    return this.http.post<AnalysisJob>('/api/analysis-jobs', req);
  }

  createMany(req: CreateAnalysisJobsBatchRequest): Observable<AnalysisJobBatchResult> {
    return this.http.post<AnalysisJobBatchResult>('/api/analysis-jobs/batch', req);
  }

  update(id: number, req: UpdateAnalysisJobRequest): Observable<AnalysisJob> {
    return this.http.put<AnalysisJob>(`/api/analysis-jobs/${id}`, req);
  }

  /** Wieder einreihen (nach „gescheitert" oder gefühltem Stillstand); das Ergebnis bleibt erhalten. */
  restart(id: number): Observable<AnalysisJob> {
    return this.http.post<AnalysisJob>(`/api/analysis-jobs/${id}/restart`, {});
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/analysis-jobs/${id}`);
  }
}
