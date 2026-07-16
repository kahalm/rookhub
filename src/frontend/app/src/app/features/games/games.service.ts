import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Listeneintrag einer gespeicherten Partie (ohne PGN). */
export interface SavedGame {
  id: number;
  source: string;
  white?: string | null;
  black?: string | null;
  result?: string | null;
  playedAt?: string | null;
  sourceUrl?: string | null;
  shareToken: string;
  moveCount: number;
  createdAt: string;
}

/** Detail inkl. PGN (zum Nachspielen/Analysieren). */
export interface SavedGameDetail extends SavedGame {
  pgn: string;
  whiteElo?: number | null;
  blackElo?: number | null;
}

/** Öffentliche Sicht auf eine geteilte Partie (ohne Besitzer-Daten). */
export interface SharedGame {
  source: string;
  white?: string | null;
  black?: string | null;
  result?: string | null;
  playedAt?: string | null;
  sourceUrl?: string | null;
  pgn: string;
  createdAt: string;
  whiteElo?: number | null;
  blackElo?: number | null;
  /** "white"/"black", wenn der Teilende einer Seite zuordenbar ist — initiale Brett-Orientierung. */
  ownerSide?: 'white' | 'black' | null;
}

@Injectable({ providedIn: 'root' })
export class GamesService {
  constructor(private http: HttpClient) {}

  list(take = 200): Observable<SavedGame[]> {
    return this.http.get<SavedGame[]>(`/api/games?take=${take}`);
  }

  get(id: number): Observable<SavedGameDetail> {
    return this.http.get<SavedGameDetail>(`/api/games/${id}`);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/games/${id}`);
  }

  getShared(token: string): Observable<SharedGame> {
    return this.http.get<SharedGame>(`/api/games/shared/${encodeURIComponent(token)}`);
  }

  /** Absolute Teilen-URL einer Partie (für Copy-to-Clipboard). */
  shareUrl(shareToken: string): string {
    return `${window.location.origin}/g/${shareToken}`;
  }
}
