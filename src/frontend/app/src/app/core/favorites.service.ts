import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';

/** Puzzle-Quelle eines Favoriten (Backend-Enum <c>PuzzleSource</c>). */
export type FavoriteSource = 'standard' | 'book';

/** Ein geliebtes Puzzle inkl. Metadaten zum Nachspielen (Deep-Link) und Analysieren (Fen+Moves). */
export interface FavoritePuzzle {
  id: number;
  puzzleId: number;
  source: 'Standard' | 'Book';
  rating: number;
  themes?: string | null;
  title?: string | null;
  fen: string;
  moves: string;
  createdAt: string;
}

/**
 * Kapselt die Favoriten-HTTP-Calls (`/api/favorites/*`). „Geliebte" Puzzles sind polymorph
 * (Standard/Buch); add/remove/contains sind idempotent.
 */
@Injectable({ providedIn: 'root' })
export class FavoritesService {
  private readonly apiUrl = '/api/favorites';

  constructor(private http: HttpClient) {}

  /** Alle geliebten Puzzles (neueste zuerst). */
  list(take = 200): Observable<FavoritePuzzle[]> {
    return this.http.get<FavoritePuzzle[]>(`${this.apiUrl}?take=${take}`);
  }

  count(): Observable<number> {
    return this.http.get<{ count: number }>(`${this.apiUrl}/count`).pipe(map(r => r.count));
  }

  /** Ist ein konkretes Puzzle favorisiert? */
  contains(source: FavoriteSource, puzzleId: number): Observable<boolean> {
    return this.http
      .get<{ favorited: boolean }>(`${this.apiUrl}/contains?source=${source}&puzzleId=${puzzleId}`)
      .pipe(map(r => r.favorited));
  }

  /** Puzzle favorisieren (idempotent). */
  add(source: FavoriteSource, puzzleId: number): Observable<boolean> {
    return this.http
      .post<{ favorited: boolean }>(this.apiUrl, { source, puzzleId })
      .pipe(map(r => r.favorited));
  }

  /** Puzzle aus den Favoriten entfernen (idempotent). */
  remove(source: FavoriteSource, puzzleId: number): Observable<boolean> {
    return this.http
      .delete<{ favorited: boolean }>(`${this.apiUrl}?source=${source}&puzzleId=${puzzleId}`)
      .pipe(map(r => r.favorited));
  }
}
