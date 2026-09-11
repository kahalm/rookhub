import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GameAnalysis } from '../analysis/game-analysis.service';

/**
 * Eine Zeile des Rohbestands — bewusst OHNE die Züge. Die 130 000 Partien tragen zusammen 338 MB
 * Partietext; was die Auswahl braucht, steht in den Zahlen daneben.
 */
export interface LibraryGame {
  id: number;
  white: string | null;
  black: string | null;
  whiteElo: number | null;
  blackElo: number | null;
  result: string | null;
  event: string | null;
  playedOn: string | null;
  eco: string | null;
  plyCount: number | null;
  annotator: string | null;
  /** Wie viele HALBZÜGE einen Kommentar tragen — die Zahl, die über die Eignung entscheidet. */
  commentedPlies: number | null;
  commentChars: number | null;
  languages: string | null;
  /** Eignungsnote 0–100; Vorgabe-Sortierung der Suche. */
  score: number | null;
  sourceTitle: string | null;
  /** Liegt schon im kuratierten Bestand — jeder kann sie sofort spielen. */
  inPool: boolean;
  /** Der Aufrufer hat sie bereits angefordert. */
  requested: boolean;
  /** Die spielbare Analyse (eigene oder die des Bestands). */
  gameAnalysisId: number | null;
}

export interface LibraryGamePage {
  items: LibraryGame[];
  total: number;
  page: number;
  pageSize: number;
}

export interface LibraryQuery {
  q?: string;
  language?: string;
  minCommentedPlies?: number;
  page?: number;
  pageSize?: number;
}

/** Warum eine Anforderung abgelehnt wurde. */
export type LibraryRequestReason = 'not-found' | 'too-many-open' | 'no-engine' | 'invalid-pgn';

export interface LibraryRequestResult {
  analysis: GameAnalysis;
  /** Die Partie lag schon spielbar da — es wurde nichts neu gerechnet. */
  alreadyPlayable: boolean;
}

@Injectable({ providedIn: 'root' })
export class LibraryService {
  private http = inject(HttpClient);

  /** Gesucht wird am SERVER: 130 000 Zeilen lassen sich nicht ausliefern und im Browser filtern. */
  search(query: LibraryQuery): Observable<LibraryGamePage> {
    let params = new HttpParams();
    if (query.q?.trim()) params = params.set('q', query.q.trim());
    if (query.language) params = params.set('language', query.language);
    if (query.minCommentedPlies) params = params.set('minCommentedPlies', query.minCommentedPlies);
    if (query.page) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    return this.http.get<LibraryGamePage>('/api/library-games', { params });
  }

  /** Diese Partie rechnen lassen — oder, wenn sie schon spielbar ist, die vorhandene bekommen. */
  request(id: number): Observable<LibraryRequestResult> {
    return this.http.post<LibraryRequestResult>(`/api/library-games/${id}/request`, {});
  }
}
