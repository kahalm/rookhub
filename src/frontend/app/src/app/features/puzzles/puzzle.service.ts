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
  puzzleElo: number;
  puzzleEloPerLevel?: Record<number, number>;
}

export interface PuzzleAttemptDto {
  id: number;
  puzzleId: number;
  lichessId: string;
  puzzleRating: number;
  solved: boolean;
  timeSpentSeconds: number;
  attemptedAt: string;
  eloAfter?: number;
  eloChange?: number;
  visualizationLevel?: number;
}

export interface EloHistoryPoint {
  attemptedAt: string;
  elo: number;
  vizLevel: number;
  solved: boolean;
}

export interface ThemeStat { theme: string; attempts: number; solved: number; }
export interface RatingBand { from: number; to: number; attempts: number; solved: number; }
export interface ActivityDay { date: string; count: number; }
export interface PuzzleBreakdown { themes: ThemeStat[]; ratingBands: RatingBand[]; activity: ActivityDay[]; }

export interface BookPuzzleDto {
  id: number;
  lineId: string;
  bookFileName: string;
  round: string;
  fen: string;
  moves: string;
  /** Halbzug-Index des Trainingsstarts; lösen ab moves[startPly+1]. -1 = lösen ab moves[0] (FEN=Trainingsstellung), 0 = klassisch (moves[0] Setup). */
  startPly?: number;
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

  /** Lädt je Rating-Fenster ein eindeutiges Zufalls-Puzzle (Offline-Vorab-Laden eines Runs). */
  getRandomBatch(windows: { minRating: number; maxRating: number }[], themes?: string, excludeSolved = false): Observable<PuzzleDto[]> {
    return this.http.post<PuzzleDto[]>('/api/puzzles/random-batch', { windows, themes: themes ?? null, excludeSolved });
  }

  getById(id: number): Observable<PuzzleDto> {
    return this.http.get<PuzzleDto>(`/api/puzzles/${id}`);
  }

  recordAttempt(id: number, solved: boolean, timeSpentSeconds: number, moveLog?: string, visualizationLevel = 0): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt`, {
      solved, timeSpentSeconds, moveLog, visualizationLevel,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight
    });
  }

  getStats(vizLevel?: number): Observable<PuzzleStatsDto> {
    let params = new HttpParams();
    if (vizLevel != null) params = params.set('vizLevel', vizLevel);
    return this.http.get<PuzzleStatsDto>('/api/puzzles/stats', { params });
  }

  getHistory(page = 1, pageSize = 20): Observable<PuzzleAttemptDto[]> {
    return this.http.get<PuzzleAttemptDto[]>('/api/puzzles/history', {
      params: new HttpParams().set('page', page).set('pageSize', pageSize)
    });
  }

  getEloHistory(limit = 500): Observable<EloHistoryPoint[]> {
    return this.http.get<EloHistoryPoint[]>('/api/puzzles/elo-history', {
      params: new HttpParams().set('limit', limit)
    });
  }

  getBreakdown(): Observable<PuzzleBreakdown> {
    return this.http.get<PuzzleBreakdown>('/api/puzzles/stats/breakdown');
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

  recordAnonymousAttempt(id: number, solved: boolean, timeSpentSeconds: number, moveLog?: string, visualizationLevel = 0): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt/anonymous`, {
      sessionId: this.getOrCreateSessionId(), solved, timeSpentSeconds, moveLog, visualizationLevel,
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

  /** Nächstes Puzzle im selben Buch (Standalone-Buch-Navigation). */
  getNextBookPuzzle(id: number): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/${id}/next`);
  }

  /** Zufälliges Puzzle aus demselben Buch. */
  getRandomBookPuzzle(id: number): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/${id}/random`);
  }

  getBookList(): Observable<BookInfoDto[]> {
    return this.http.get<BookInfoDto[]>('/api/book-puzzles/books');
  }
}
