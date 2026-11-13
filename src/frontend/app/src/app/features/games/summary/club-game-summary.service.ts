import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';

/** Stand der Zusammenfassung einer Vereinspartie (0.913.0, `GET /api/league/club/games/{id}/summary`). */
export interface ClubGameSummary {
  status: 'none' | 'requested' | 'running' | 'done' | 'failed';
  /** Letzte fertige Fassung — bleibt stehen, während eine neue angefordert ist. */
  markdown: string | null;
  doneAt: string | null;
  requestedAt: string | null;
  /** Solange Claude schreibt: seit wie vielen Sekunden (vom Server gerechnet). */
  runningSeconds?: number | null;
  /** Die Partie wurde nach dem Schreiben korrigiert. */
  stale: boolean;
  model: string | null;
  error: string | null;
  /** Nur bei offener Anforderung (0.914.0): wie weit die Hintergrund-Rechnung ist; Claude schreibt erst bei `ready`. */
  readiness?: ClubGameSummaryReadiness | null;
  /** Eigene Partie (0.923.0): `club` = der Text gehört zur verbundenen Vereinspartie. */
  source?: 'own' | 'club';
  /** Eigene Partie (0.954.0): der Stand kommt von der verbundenen Vereinspartie und man darf sie sehen — der Link führt dorthin. */
  clubGameId?: number | null;
  /** Eigene Partie: heute noch mögliche Anforderungen; `null` = unbegrenzt (Admin). */
  quotaLeft?: number | null;
  quotaLimit?: number;
}

export interface ClubGameSummaryReadiness {
  ready: boolean;
  hasAnalysis: boolean;
  analysisDone: boolean;
  refining: boolean;
  refinedPositions: number;
  totalPositions: number;
  comparisonsPlanned: boolean;
  comparisonsDone: number;
  comparisonsTotal: number;
  /** Stellungen, die der schnelle Durchgang schon gerechnet hat (0.919.3). */
  analyzedPositions?: number;
}

/** Eine Lichess-Aufgabe der „Empfohlenen Übungen“ (0.960.0): `fen` VOR dem Zug des Gegners, `moves` = Gegnerzug, dann abwechselnd Löser/Gegner (UCI). */
export interface SummaryExercise { id: number; fen: string; moves: string[]; rating: number; themes: string[]; match: 'strict' | 'family' | 'theme'; }

/** Eine Stelle der Partie mit ihren Aufgaben; `ply` = Stellung VOR dem Halbzug, `reason` → `games.summary.exercises.reason.*`. */
export interface SummaryExerciseGroup {
  ply: number; white: boolean; reason: 'missedMate' | 'playedMate' | 'missedTactic' | 'allowedTactic'; mateIn: number | null;
  alsoPlies: number[]; exercises: SummaryExercise[];
  /** Taktische Stelle (0.961.0): Lichess-Thema wie `fork` (Anzeige über `puzzleTheme`). */
  theme?: string | null;
}

/** Woher die Seite kommt — dieselben fünf Wege wie die Zusammenfassung selbst. */
export type SummarySource =
  | { kind: 'club'; id: number } | { kind: 'own'; id: number } | { kind: 'club-share'; token: string; id: number }
  | { kind: 'club-link'; token: string } | { kind: 'own-share'; token: string };

/** Ein Buch der „Empfohlenen Literatur“ (`GET /api/ebook-suggestions`). */
export interface EbookSuggestionInfo { id: number; title: string; author: string | null; year: string | null; accessible: boolean; blocked: boolean; }

@Injectable({ providedIn: 'root' })
export class ClubGameSummaryService {
  private http = inject(HttpClient);

  suggestions(ids: number[]): Observable<EbookSuggestionInfo[]> {
    return this.http.get<EbookSuggestionInfo[]>('/api/ebook-suggestions', { params: { ids: ids.join(',') } });
  }
  /** „Nicht mehr vorschlagen“ setzen bzw. zurücknehmen. */
  setSuggestionBlocked(id: number, blocked: boolean): Observable<void> {
    const url = `/api/ebook-suggestions/${id}/block`;
    return blocked ? this.http.put<void>(url, {}) : this.http.delete<void>(url);
  }

  /** Empfohlene Übungen (0.960.0, `GET …/summary/exercises`): Lichess-Aufgaben zu den Mattbildern der Partie. */
  exercises(src: SummarySource): Observable<SummaryExerciseGroup[]> {
    const t = (x: string) => encodeURIComponent(x);
    const url = src.kind === 'club' ? `/api/league/club/games/${src.id}/summary/exercises`
      : src.kind === 'own' ? `/api/games/${src.id}/summary/exercises`
      : src.kind === 'club-share' ? `/api/league/s/${t(src.token)}/club/games/${src.id}/summary/exercises`
      : src.kind === 'club-link' ? `/api/league/s-club-game/${t(src.token)}/summary/exercises`
      : `/api/games/shared/${t(src.token)}/summary/exercises`;
    return this.http.get<{ groups: SummaryExerciseGroup[] }>(url).pipe(map(r => r.groups ?? []));
  }

  get(gameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/league/club/games/${gameId}/summary`);
  }

  /** Vereinspartie über den Teilen-Link, nur lesend (Wunsch 2026-11-09): nur der fertige Text. */
  getShared(token: string, gameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/league/s/${encodeURIComponent(token)}/club/games/${gameId}/summary`);
  }

  /** Vereinspartie über ihren eigenen Link `/cg/{token}`, nur lesend: nur der fertige Text. */
  getLinked(token: string): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/league/s-club-game/${encodeURIComponent(token)}/summary`);
  }

  /** Eigene Partie über ihren Teilen-Link `/g/{token}`, nur lesend (Wunsch 2026-11-09): nur der fertige Text. */
  getSharedOwn(token: string): Observable<ClubGameSummary> {
    return this.http.get<Pick<ClubGameSummary, 'status' | 'markdown' | 'doneAt' | 'stale' | 'model'>>(`/api/games/shared/${encodeURIComponent(token)}/summary`)
      .pipe(map(s => ({ ...s, requestedAt: null, error: null })));
  }

  /** Eigene Partie (0.923.0, `GET /api/games/{id}/summary`). */
  getOwn(savedGameId: number): Observable<ClubGameSummary> {
    return this.http.get<ClubGameSummary>(`/api/games/${savedGameId}/summary`);
  }

  /** Eigene Partie anfordern — Kontingent je Tag, Server 429 wenn verbraucht. */
  requestOwn(savedGameId: number, lang: string): Observable<ClubGameSummary> {
    return this.http.post<ClubGameSummary>(`/api/games/${savedGameId}/summary`, { lang });
  }

  /** Anfordern bzw. neu anfordern — nur Admins (Server: 403 sonst). */
  request(gameId: number): Observable<ClubGameSummary> {
    return this.http.post<ClubGameSummary>(`/api/league/club/games/${gameId}/summary`, {});
  }
}
