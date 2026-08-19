import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Eine Kalkulations-Ausgabe (Verwaltungs- + Betrachtersicht; Spiegel von CalcEditionDto). */
export interface CalcEdition {
  id: number;
  bookId: number;
  chapter: string;
  title?: string | null;
  videoUrl?: string | null;
  publishAt: string;          // ISO (UTC)
  testerPreviewAt?: string | null;
  released: boolean;
}

export interface CalcEditionInput {
  chapter: string;
  title?: string | null;
  videoUrl?: string | null;
  publishAt: string;          // ISO (UTC)
  testerPreviewAt?: string | null;
}

@Injectable({ providedIn: 'root' })
export class CalcEditionsService {
  private http = inject(HttpClient);

  /** Freigegebene Ausgaben (Betrachter). */
  visible(bookId: number): Observable<CalcEdition[]> {
    return this.http.get<CalcEdition[]>(`/api/calc-editions/${bookId}`);
  }
  /** ALLE Ausgaben inkl. Entwürfe (nur Besitzer/Admin). */
  manage(bookId: number): Observable<CalcEdition[]> {
    return this.http.get<CalcEdition[]>(`/api/calc-editions/${bookId}/manage`);
  }
  upsert(bookId: number, dto: CalcEditionInput): Observable<CalcEdition> {
    return this.http.put<CalcEdition>(`/api/calc-editions/${bookId}`, dto);
  }
  remove(bookId: number, editionId: number): Observable<void> {
    return this.http.delete<void>(`/api/calc-editions/${bookId}/${editionId}`);
  }
}
