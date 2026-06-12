import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ChessableCredential {
  hasCredentials: boolean;
  maskedBearer: string | null;
}

export interface ChessableTestResult {
  uid: string;
  courseCount: number;
}

export interface ChessableCourse {
  bid: string;
  name: string;
  importedRepertoire?: boolean;
  importedBook?: boolean;
}

export interface ChessableCoursesResult {
  courses: ChessableCourse[];
  cachedAt: string | null;
}

export type ChessableImportTarget = 'repertoire' | 'book';

export interface ChessableImport {
  id: number;
  bid: string;
  courseName: string;
  target: string;
  status: 'running' | 'completed' | 'failed' | 'cancelled' | 'paused';
  phase: string;
  error: string | null;
  resultId: number | null;
  imported: number;
  skipped: number;
  invalid: number;
  chaptersDone: number;
  chaptersTotal: number;
  linesDone: number;
  queuedAhead: number;
}

@Injectable({ providedIn: 'root' })
export class ChessableService {
  private readonly apiUrl = '/api/chessable';

  constructor(private http: HttpClient) {}

  getDisclaimer(): Observable<{ accepted: boolean }> {
    return this.http.get<{ accepted: boolean }>(`${this.apiUrl}/disclaimer`);
  }

  acceptDisclaimer(): Observable<{ accepted: boolean }> {
    return this.http.post<{ accepted: boolean }>(`${this.apiUrl}/disclaimer`, {});
  }

  getCredentials(): Observable<ChessableCredential> {
    return this.http.get<ChessableCredential>(`${this.apiUrl}/credentials`);
  }

  saveCredentials(bearer: string): Observable<ChessableCredential> {
    return this.http.post<ChessableCredential>(`${this.apiUrl}/credentials`, { bearer });
  }

  deleteCredentials(): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/credentials`);
  }

  test(): Observable<ChessableTestResult> {
    return this.http.post<ChessableTestResult>(`${this.apiUrl}/test`, {});
  }

  /** Kursliste — aus dem DB-Cache, oder mit refresh=true frisch von piratechess (+ Cache-Update). */
  getCourses(refresh = false): Observable<ChessableCoursesResult> {
    return this.http.get<ChessableCoursesResult>(`${this.apiUrl}/courses`,
      refresh ? { params: { refresh: 'true' } } : {});
  }

  /** Startet einen async Kurs-Import (Repertoire oder Buch). Liefert den Import-Satz (status "running"). */
  startImport(bid: string, target: ChessableImportTarget, name: string): Observable<ChessableImport> {
    return this.http.post<ChessableImport>(
      `${this.apiUrl}/courses/${encodeURIComponent(bid)}/import`, { target, name });
  }

  /** Pollt den Status eines Imports. */
  getImport(id: number): Observable<ChessableImport> {
    return this.http.get<ChessableImport>(`${this.apiUrl}/imports/${id}`);
  }

  /** Letzte Importe des Users (z. B. um beim Laden der Seite einen laufenden Import zu erkennen). */
  getImports(): Observable<ChessableImport[]> {
    return this.http.get<ChessableImport[]>(`${this.apiUrl}/imports`);
  }

  cancelImport(id: number): Observable<ChessableImport> {
    return this.http.post<ChessableImport>(`${this.apiUrl}/imports/${id}/cancel`, {});
  }

  pauseImport(id: number): Observable<ChessableImport> {
    return this.http.post<ChessableImport>(`${this.apiUrl}/imports/${id}/pause`, {});
  }

  resumeImport(id: number): Observable<ChessableImport> {
    return this.http.post<ChessableImport>(`${this.apiUrl}/imports/${id}/resume`, {});
  }
}
