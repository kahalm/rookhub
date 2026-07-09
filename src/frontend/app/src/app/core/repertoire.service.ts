import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Repertoire } from './models';

/**
 * Kapselt die Repertoire-CRUD-HTTP-Calls (`/api/repertoires`), damit die Komponenten nicht direkt
 * den `HttpClient` ansprechen (Service-Layer, vgl. Audit-Fund „14 Komponenten rufen HttpClient direkt").
 */
/** Ergebnis eines Teilen-Vorgangs (Batch). */
export interface RepertoireShareResult {
  shared: number;
  skipped: { userId: number; reason: string }[];
}

/** Ein Nutzer, mit dem ein Repertoire aktuell geteilt ist. */
export interface RepertoireShareRecipient {
  userId: number;
  username: string;
  displayName: string | null;
  sharedAt: string;
}

/** Öffentliche Sicht einer geteilten Einzel-Linie (Nur-Ansehen-Link `/l/{token}`). */
export interface SharedLine {
  shareToken: string;
  title: string | null;
  repertoireName: string | null;
  pgn: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class RepertoireService {
  private readonly apiUrl = '/api/repertoires';

  constructor(private http: HttpClient) {}

  /** Teilt ein eigenes Repertoire mit ausgewählten (befreundeten) Nutzern. */
  share(id: number, recipientUserIds: number[]): Observable<RepertoireShareResult> {
    return this.http.post<RepertoireShareResult>(`${this.apiUrl}/${id}/share`, { recipientUserIds });
  }

  /** Mit welchen Nutzern ist dieses eigene Repertoire aktuell geteilt? */
  getShareRecipients(id: number): Observable<RepertoireShareRecipient[]> {
    return this.http.get<RepertoireShareRecipient[]>(`${this.apiUrl}/${id}/shares`);
  }

  /** Nimmt die Freigabe des eigenen Repertoires für einen Empfänger zurück. */
  unshare(id: number, recipientId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/share/${recipientId}`);
  }

  /** Erzeugt einen öffentlichen Nur-Ansehen-Link für eine einzelne Linie (liefert das Token). */
  shareLine(id: number, body: { pgn: string; title?: string }): Observable<{ shareToken: string }> {
    return this.http.post<{ shareToken: string }>(`${this.apiUrl}/${id}/share-line`, body);
  }

  /** Öffentliche Sicht einer geteilten Linie über ihr Token (kein Login). */
  getSharedLine(token: string): Observable<SharedLine> {
    return this.http.get<SharedLine>(`${this.apiUrl}/shared-line/${token}`);
  }

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
