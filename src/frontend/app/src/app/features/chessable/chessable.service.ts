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
}
