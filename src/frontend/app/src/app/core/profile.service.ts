import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * Kapselt die Profil-HTTP-Calls (`/api/profile*`). Generisch gehalten, weil die Profil-/
 * Spielersuche-Response-Formen feature-lokal definiert sind (kein Rückwärts-Import core → feature).
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly apiUrl = '/api/profile';

  constructor(private http: HttpClient) {}

  getProfile<T>(): Observable<T> {
    return this.http.get<T>(this.apiUrl);
  }

  updateProfile<T>(dto: unknown): Observable<T> {
    return this.http.put<T>(this.apiUrl, dto);
  }

  searchPlayer<T>(lastName: string, firstName?: string): Observable<T> {
    let params = new HttpParams().set('lastName', lastName);
    if (firstName) params = params.set('firstName', firstName);
    return this.http.get<T>(`${this.apiUrl}/player-search`, { params });
  }
}
