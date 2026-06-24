import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Repertoire } from './models';

/**
 * Kapselt die Repertoire-CRUD-HTTP-Calls (`/api/repertoires`), damit die Komponenten nicht direkt
 * den `HttpClient` ansprechen (Service-Layer, vgl. Audit-Fund „14 Komponenten rufen HttpClient direkt").
 */
@Injectable({ providedIn: 'root' })
export class RepertoireService {
  private readonly apiUrl = '/api/repertoires';

  constructor(private http: HttpClient) {}

  list(): Observable<Repertoire[]> {
    return this.http.get<Repertoire[]>(this.apiUrl);
  }

  create(dto: unknown): Observable<unknown> {
    return this.http.post(this.apiUrl, dto);
  }

  update(id: number, dto: unknown): Observable<unknown> {
    return this.http.put(`${this.apiUrl}/${id}`, dto);
  }

  remove(id: number): Observable<unknown> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  /** Kombinierter PGN-Download (Blob). */
  downloadPgn(id: number): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${id}/pgn`, { responseType: 'blob' });
  }
}
