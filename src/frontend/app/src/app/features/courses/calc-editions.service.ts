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

/** Ein Mitglied des Serien-Verteilers (Verwaltungssicht; Spiegel von CalcSeriesMemberDto). */
export interface CalcSeriesMember {
  userId: number;
  username: string;
  isTester: boolean;
  createdAt: string;          // ISO (UTC)
}

export interface CalcSeriesMemberInput {
  username: string;
  isTester: boolean;
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

  // ===== Privater Verteiler (Phase 2) =====================================

  /** Verteiler-Mitglieder eines Serien-Buchs (nur Besitzer/Admin). */
  members(bookId: number): Observable<CalcSeriesMember[]> {
    return this.http.get<CalcSeriesMember[]>(`/api/calc-editions/${bookId}/members`);
  }
  /** Mitglied hinzufügen/ändern (per Benutzername). 404, wenn es den Nutzer nicht gibt. */
  upsertMember(bookId: number, dto: CalcSeriesMemberInput): Observable<CalcSeriesMember> {
    return this.http.put<CalcSeriesMember>(`/api/calc-editions/${bookId}/members`, dto);
  }
  /** Mitglied entfernen. */
  removeMember(bookId: number, userId: number): Observable<void> {
    return this.http.delete<void>(`/api/calc-editions/${bookId}/members/${userId}`);
  }
}
