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
  /** Das Jahr, auch wenn das PGN nur das Jahr kennt (nur die Meisterpartien-Suche liefert es). */
  playedYear?: number | null;
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

/** Filter der Meisterpartien-Suche (`GET /api/library-games/search`, 1.038.0) — alle optional. */
export interface MasterGameQuery {
  q?: string;
  /** Spielername, egal welche Farbe. */
  player?: string;
  event?: string;
  yearFrom?: number | null;
  yearTo?: number | null;
  result?: '' | 'white' | 'black' | 'draw';
  /** ECO-Code, -Präfix oder -Bereich („B90“, „B9“, „B20-B99“). */
  eco?: string;
  sort?: 'best' | 'newest' | 'oldest';
  page?: number;
  pageSize?: number;
}

export interface MasterGamePage {
  items: LibraryGame[];
  total: number;
  page: number;
  pageSize: number;
  /** Eingaben, die nicht gefiltert haben (zu kurz, Stoppwort, unlesbarer ECO-Code). */
  ignored: string[];
  /** Weiter blättert die Suche nicht. */
  maxPage: number;
}

/** „Frag die Kommentare" (0.536.0): eine passende Stelle — erster kommentierter Halbzug, Auszug, Ähnlichkeit 0..1. */
export interface LibrarySemanticMatch {
  fromPly: number;
  text: string;
  score: number;
}

export interface LibrarySemanticHit {
  game: LibraryGame;
  matches: LibrarySemanticMatch[];
}

export interface LibrarySemanticPage {
  /** Ein Embedding-Modell ist eingerichtet. */
  available: boolean;
  /** So viele Textstücke sind eingebettet — 0 heißt, der Bestand wurde noch nicht vorbereitet. */
  indexed: number;
  items: LibrarySemanticHit[];
}

/** Eine Meisterpartie zum Anschauen: Hauptvariante + Anmerkungen in `language` (fehlende Stellen aus dem Original). */
export interface LibraryGameView {
  id: number;
  pgn: string;
  language: string | null;
  languages: string[];
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

  /** Meisterpartien-Suche (1.038.0): nur gesetzte Filter gehen mit. */
  searchMasters(query: MasterGameQuery): Observable<MasterGamePage> {
    let params = new HttpParams();
    for (const key of ['q', 'player', 'event', 'eco'] as const) {
      const v = query[key]?.trim();
      if (v) params = params.set(key, v);
    }
    if (query.yearFrom != null) params = params.set('yearFrom', query.yearFrom);
    if (query.yearTo != null) params = params.set('yearTo', query.yearTo);
    if (query.result) params = params.set('result', query.result);
    if (query.sort && query.sort !== 'best') params = params.set('sort', query.sort);
    if (query.page && query.page > 1) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    return this.http.get<MasterGamePage>('/api/library-games/search', { params });
  }

  /** „Frag die Kommentare" (0.536.0): semantische Suche über die Anmerkungen. Ohne `q` nur, ob es sie gibt. */
  semantic(q: string, take = 20): Observable<LibrarySemanticPage> {
    let params = new HttpParams().set('take', take);
    if (q.trim()) params = params.set('q', q.trim());
    return this.http.get<LibrarySemanticPage>('/api/library-games/semantic', { params });
  }

  /** Diese Partie rechnen lassen — oder, wenn sie schon spielbar ist, die vorhandene bekommen. */
  /** „Anschauen" (0.567.0): die Partie als PGN mit den Anmerkungen in `lang` — ohne Rechnen, nur angemeldet. */
  view(id: number, lang?: string): Observable<LibraryGameView> {
    const params = lang ? new HttpParams().set('lang', lang) : undefined;
    return this.http.get<LibraryGameView>(`/api/library-games/${id}/view`, { params });
  }

  request(id: number): Observable<LibraryRequestResult> {
    return this.http.post<LibraryRequestResult>(`/api/library-games/${id}/request`, {});
  }
}
