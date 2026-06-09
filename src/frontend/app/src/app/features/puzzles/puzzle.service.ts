import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

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

  /** @param themesAny Leerzeichengetrennt; Puzzle muss mind. EINS enthalten (für „schwächste Themen trainieren"). */
  getRandom(minRating?: number, maxRating?: number, themes?: string, excludeSolved = false, themesAny?: string): Observable<PuzzleDto> {
    let params = new HttpParams();
    if (minRating != null) params = params.set('minRating', minRating);
    if (maxRating != null) params = params.set('maxRating', maxRating);
    if (themes) params = params.set('themes', themes);
    if (excludeSolved) params = params.set('excludeSolved', 'true');
    if (themesAny) params = params.set('themesAny', themesAny);
    return this.http.get<PuzzleDto>('/api/puzzles/random', { params });
  }

  /** Lädt je Rating-Fenster ein eindeutiges Zufalls-Puzzle (Offline-Vorab-Laden eines Runs). */
  getRandomBatch(windows: { minRating: number; maxRating: number }[], themes?: string, excludeSolved = false, themesAny?: string): Observable<PuzzleDto[]> {
    return this.http.post<PuzzleDto[]>('/api/puzzles/random-batch', { windows, themes: themes ?? null, excludeSolved, themesAny: themesAny ?? null });
  }

  /** Themen-Namen der schwächsten Themen des Users (niedrigste Lösungsquote), für „schwächste Themen trainieren". */
  getWorstThemes(count = 5, minAttempts = 3): Observable<string[]> {
    return this.http.get<ThemeStat[]>('/api/puzzles/stats/worst-themes', {
      params: new HttpParams().set('count', count).set('minAttempts', minAttempts)
    }).pipe(map(ts => ts.map(t => t.theme)));
  }

  getById(id: number): Observable<PuzzleDto> {
    return this.http.get<PuzzleDto>(`/api/puzzles/${id}`);
  }

  recordAttempt(id: number, solved: boolean, timeSpentSeconds: number, moveLog?: string, visualizationLevel = 0, evalShown = false, vizShowCount = 0): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt`, {
      solved, timeSpentSeconds, moveLog, visualizationLevel, evalShown, vizShowCount,
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

  /** Anonyme Puzzle-Session-Id (für das Offline-Vormerken anonymer Versuche). */
  ensureSessionId(): string {
    return this.getOrCreateSessionId();
  }

  recordAnonymousAttempt(id: number, solved: boolean, timeSpentSeconds: number, moveLog?: string, visualizationLevel = 0, evalShown = false, vizShowCount = 0): Observable<PuzzleAttemptDto> {
    return this.http.post<PuzzleAttemptDto>(`/api/puzzles/${id}/attempt/anonymous`, {
      sessionId: this.getOrCreateSessionId(), solved, timeSpentSeconds, moveLog, visualizationLevel, evalShown, vizShowCount,
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

  claimBookPuzzleSession(): Observable<{ transferred: number }> {
    return this.http.post<{ transferred: number }>('/api/book-puzzles/claim-session', {
      sessionId: this.getOrCreateSessionId()
    });
  }

  getBookPuzzleById(id: number): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/${id}`);
  }

  /** Tagespuzzle eines UTC-Datums (`yyyyMMdd` oder `today`) — stabil/teilbar via Datums-Link. */
  getDailyPuzzle(date: string): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/daily/${date}`);
  }

  /** Nächstes Puzzle im selben Buch (Standalone-Buch-Navigation). */
  getNextBookPuzzle(id: number): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/${id}/next`);
  }

  /** Zufälliges Puzzle aus demselben Buch. */
  getRandomBookPuzzle(id: number): Observable<BookPuzzleDto> {
    return this.http.get<BookPuzzleDto>(`/api/book-puzzles/${id}/random`);
  }

  /** Lösungsversuch an einem Buch-Puzzle melden (eingeloggt; Basis für Tagespuzzle-Anzeige). */
  recordBookAttempt(id: number, solved: boolean, timeSeconds: number): Observable<unknown> {
    return this.http.post(`/api/book-puzzles/${id}/attempt`, { solved, timeSeconds });
  }

  /** Anonymer Buch-Puzzle-Solve (nicht eingeloggt) — zählt fürs Tagespuzzle namenlos mit. */
  recordBookAttemptAnonymous(id: number, solved: boolean, timeSeconds: number): Observable<unknown> {
    return this.http.post(`/api/book-puzzles/${id}/attempt/anonymous`, {
      solved, timeSeconds, sessionId: this.ensureSessionId()
    });
  }

  getBookList(): Observable<BookInfoDto[]> {
    return this.http.get<BookInfoDto[]>('/api/book-puzzles/books');
  }
}
