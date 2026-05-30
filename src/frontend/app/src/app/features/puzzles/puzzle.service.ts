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

export interface BookPuzzleDto {
  id: number;
  lineId: string;
  bookFileName: string;
  round: string;
  fen: string;
  moves: string;
  title?: string;
  chapter?: string;
  comment?: string;
  difficulty?: string;
  bookRating?: number;
  tags?: string;
}

export interface BookInfoDto {
  bookFileName: string;
  difficulty?: string;
  bookRating?: number;
  tags?: string;
  puzzleCount: number;
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

  recordAttempt(id: number, solved: boolean, timeSpentSeconds: number, moveLog?: string): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt`, {
      solved, timeSpentSeconds, moveLog,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight
    });
  }

  getStats(): Observable<PuzzleStatsDto> {
    return this.http.get<PuzzleStatsDto>('/api/puzzles/stats');
  }

  getHistory(page = 1, pageSize = 20): Observable<PuzzleAttemptDto[]> {
    return this.http.get<PuzzleAttemptDto[]>('/api/puzzles/history', {
      params: new HttpParams().set('page', page).set('pageSize', pageSize)
    });
  }

  private getOrCreateSessionId(): string {
    const key = 'rookhub_puzzle_session';
    let id = localStorage.getItem(key);
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(key, id);
    }
    return id;
  }

  recordAnonymousAttempt(id: number, solved: boolean, timeSpentSeconds: number, moveLog?: string): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt/anonymous`, {
      sessionId: this.getOrCreateSessionId(), solved, timeSpentSeconds, moveLog,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight
    });
  }

  getAnonymousStats(): Observable<PuzzleStatsDto> {
    const sessionId = this.getOrCreateSessionId();
    return this.http.get<PuzzleStatsDto>(`/api/puzzles/stats/anonymous`, {
      params: new HttpParams().set('sessionId', sessionId)
    });
  }

  claimSession(): Observable<{ claimed: number }> {
    return this.http.post<{ claimed: number }>('/api/puzzles/claim-session', {
      sessionId: this.getOrCreateSessionId()
    });
  }

  getBookPuzzleById(id: number): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/${id}`);
  }

  getBookList(): Observable<BookInfoDto[]> {
    return this.http.get<BookInfoDto[]>('/api/book-puzzles/books');
  }
}
