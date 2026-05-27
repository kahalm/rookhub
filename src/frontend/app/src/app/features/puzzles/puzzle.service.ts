import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface PuzzleDto {
  id: number;
  lichessId: string;
  fen: string;
  moves: string;
  rating: number;
  themes?: string;
  gameUrl?: string;
}

export interface PuzzleRatingRange {
  min: number;
  max: number;
}

export interface PuzzleStatsDto {
  totalAttempts: number;
  solved: number;
  accuracy: number;
  currentStreak: number;
  bestStreak: number;
}

export interface PuzzleAttemptDto {
  id: number;
  puzzleId: number;
  lichessId: string;
  puzzleRating: number;
  solved: boolean;
  timeSpentSeconds: number;
  attemptedAt: string;
}

@Injectable({ providedIn: 'root' })
export class PuzzleService {
  constructor(private http: HttpClient) {}

  getRatingRange(): Observable<PuzzleRatingRange> {
    return this.http.get<PuzzleRatingRange>('/api/puzzles/rating-range');
  }

  getRandom(minRating?: number, maxRating?: number, themes?: string, excludeSolved = false): Observable<PuzzleDto> {
    let params = new HttpParams();
    if (minRating != null) params = params.set('minRating', minRating);
    if (maxRating != null) params = params.set('maxRating', maxRating);
    if (themes) params = params.set('themes', themes);
    if (excludeSolved) params = params.set('excludeSolved', 'true');
    return this.http.get<PuzzleDto>('/api/puzzles/random', { params });
  }

  getById(id: number): Observable<PuzzleDto> {
    return this.http.get<PuzzleDto>(`/api/puzzles/${id}`);
  }

  recordAttempt(id: number, solved: boolean, timeSpentSeconds: number): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt`, { solved, timeSpentSeconds });
  }

  getStats(): Observable<PuzzleStatsDto> {
    return this.http.get<PuzzleStatsDto>('/api/puzzles/stats');
  }

  getHistory(page = 1, pageSize = 20): Observable<PuzzleAttemptDto[]> {
    return this.http.get<PuzzleAttemptDto[]>('/api/puzzles/history', {
      params: new HttpParams().set('page', page).set('pageSize', pageSize)
    });
  }
}
