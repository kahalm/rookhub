import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GameAnalysis } from '../analysis/game-analysis.service';
import { GameEvals } from './game-review.util';

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
  /** "white"/"black", wenn der Besitzer einer Seite zuordenbar ist — initiale Brett-Orientierung. */
  ownerSide?: 'white' | 'black' | null;
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

/** Antwort auf „Partie analysieren" (`POST …/analyze`): neu eingereiht oder wiederverwendet. */
export interface GameAnalyzeResult {
  analysis: GameAnalysis | null;
  reason?: string | null;
  /** Nichts neu eingereiht — die Partie war schon (oder wird gerade) gerechnet. */
  reused: boolean;
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

  // Die Adressen der Analyse stehen HIER an einer Stelle: Liste, Nachspiel-Dialog und geteilte
  // Seite reichen sie als fertige URL an Knopf und Kurve weiter, statt sie je dreimal zusammenzusetzen.

  /** „Partie analysieren" an einer eigenen Partie. */
  analyzeUrl(id: number): string { return `/api/games/${id}/analyze`; }
  /** Bewertungen einer eigenen Partie (Nachspiel-Dialog). */
  evalsUrl(id: number): string { return `/api/games/${id}/evals`; }
  /** „Partie analysieren" auf der geteilten Partie — jeder Angemeldete. */
  sharedAnalyzeUrl(token: string): string { return `/api/games/shared/${encodeURIComponent(token)}/analyze`; }
  /** Bewertungen der geteilten Partie — auch ohne Anmeldung (dann nur die des Teilenden). */
  sharedEvalsUrl(token: string): string { return `/api/games/shared/${encodeURIComponent(token)}/evals`; }

  analyze(url: string): Observable<GameAnalyzeResult> {
    return this.http.post<GameAnalyzeResult>(url, {});
  }

  evals(url: string): Observable<GameEvals> {
    return this.http.get<GameEvals>(url);
  }

  /** Absolute Teilen-URL einer Partie (für Copy-to-Clipboard). */
  shareUrl(shareToken: string): string {
    return `${window.location.origin}/g/${shareToken}`;
  }
}
