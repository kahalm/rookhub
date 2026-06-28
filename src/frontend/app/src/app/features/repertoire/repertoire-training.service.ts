import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** SM-2-Zustand einer Trainingskarte (Server). */
export interface RepertoireCardStateDto {
  cardKey: string;
  expectedMove: string;
  reps: number;
  lapses: number;
  intervalDays: number;
  ease: number;
  dueAt: string;            // ISO
  lastReviewedAt: string | null;
}

/** Grade: 0 again · 1 hard (geduldeter Alternativzug) · 2 good · 3 easy. */
export interface ReviewCardRequest {
  cardKey: string;
  expectedMove: string;
  grade: 0 | 1 | 2 | 3;
}

@Injectable({ providedIn: 'root' })
export class RepertoireTrainingService {
  constructor(private http: HttpClient) {}

  /** Kombiniertes Repertoire-PGN (alle Dateien). */
  getPgn(repertoireId: number): Observable<string> {
    return this.http.get(`/api/repertoires/${repertoireId}/pgn`, { responseType: 'text' });
  }

  getCards(repertoireId: number): Observable<RepertoireCardStateDto[]> {
    return this.http.get<RepertoireCardStateDto[]>(`/api/repertoires/${repertoireId}/training/cards`);
  }

  review(repertoireId: number, req: ReviewCardRequest): Observable<RepertoireCardStateDto> {
    return this.http.post<RepertoireCardStateDto>(`/api/repertoires/${repertoireId}/training/review`, req);
  }
}
