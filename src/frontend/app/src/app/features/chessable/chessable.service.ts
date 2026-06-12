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
}

export type ChessableImportTarget = 'repertoire' | 'book';

export interface ChessableImport {
  id: number;
  bid: string;
  courseName: string;
  target: string;
  status: 'running' | 'completed' | 'failed';
  phase: string;
  error: string | null;
  resultId: number | null;
  imported: number;
  skipped: number;
  invalid: number;
}

@Injectable({ providedIn: 'root' })
export class ChessableService {
  private readonly apiUrl = '/api/chessable';

  constructor(private http: HttpClient) {}

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

  getCourses(): Observable<ChessableCourse[]> {
    return this.http.get<ChessableCourse[]>(`${this.apiUrl}/courses`);
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
}
