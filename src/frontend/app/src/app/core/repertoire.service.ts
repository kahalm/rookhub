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

  /** Wandelt ein Repertoire in einen persönlichen Kurs um (nur Puzzle-PGN im Chessable-Stil). */
  convertToCourse(id: number): Observable<{ bookId: number; displayName: string }> {
    return this.http.post<{ bookId: number; displayName: string }>(`${this.apiUrl}/${id}/convert-to-course`, {});
  }

  /** Repertoire-Detail (inkl. Dateien); Form feature-lokal → generisch. */
  getDetail<T>(id: number): Observable<T> {
    return this.http.get<T>(`${this.apiUrl}/${id}`);
  }

  /** Kombinierter PGN-Text (zum Anzeigen, nicht als Blob-Download). */
  getPgnText(id: number): Observable<string> {
    return this.http.get(`${this.apiUrl}/${id}/pgn`, { responseType: 'text' });
  }

  /** PGN-Datei hochladen (multipart). */
  uploadFile(id: number, form: FormData): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/${id}/files`, form);
  }

  /** Einzelne PGN-Datei herunterladen (Blob). */
  downloadFile(id: number, fileId: number): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${id}/files/${fileId}`, { responseType: 'blob' });
  }

  /** Einzelne PGN-Datei löschen. */
  deleteFile(id: number, fileId: number): Observable<unknown> {
    return this.http.delete(`${this.apiUrl}/${id}/files/${fileId}`);
  }
}
