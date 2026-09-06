import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Stufen der Wertung — zugleich i18n-Schlüssel (`guess.grade.*`). */
export type GuessGrade =
  | 'muchWorse' | 'worse' | 'similar' | 'gameMove' | 'onlyMove' | 'better' | 'clearlyBetter';

export interface GuessPosition {
  ply: number;
  moveNumber: number;
  whiteToMove: boolean;
  fen: string;
  /** Der Zug DAVOR (Hervorhebung) — nicht der zu ratende. */
  lastMoveUci: string | null;
}

/** Ein Zug der Eröffnung vor dem Einstieg — `fen` ist die Stellung NACH diesem Zug. */
export interface GuessIntroMove {
  ply: number;
  moveNumber: number;
  white: boolean;
  san: string;
  uci: string;
  fen: string;
}

export interface GuessSession {
  id: number;
  gameAnalysisId: number;
  title: string | null;
  white: string | null;
  black: string | null;
  guessWhite: boolean;
  startPly: number;
  status: 'running' | 'done';
  points: number;
  maxPoints: number;
  movesPlayed: number;
  gameMoveHits: number;
  secondsSpent: number;
  /** `null`, wenn die Sitzung durch ist. */
  position: GuessPosition | null;
  totalGuesses: number;
  /** Stellung vor dem ersten Zug der Partie (nur wenn es eine Eröffnung zum Durchklicken gibt). */
  startFen: string | null;
  /** Die Züge vor dem Einstieg — leer, wenn ab dem ersten Zug geraten wird. */
  intro: GuessIntroMove[];
}

/** Antwort auf einen Rateversuch — HIER kommt der Partiezug zum ersten Mal mit. */
export interface GuessResult {
  grade: GuessGrade | null;
  points: number;
  playedSan: string | null;
  gameMoveSan: string;
  gameMoveUci: string;
  replySan: string | null;
  replyUci: string | null;
  diffCp: number | null;
  evalText: string | null;
  session: GuessSession;
}

export interface GuessReviewMove {
  ply: number;
  moveNumber: number;
  white: boolean;
  gameSan: string;
  playedSan: string | null;
  grade: GuessGrade | null;
  points: number;
  diffCp: number | null;
  secondsSpent: number;
}

@Injectable({ providedIn: 'root' })
export class GuessService {
  private http = inject(HttpClient);

  list(): Observable<GuessSession[]> {
    return this.http.get<GuessSession[]>('/api/guess-sessions');
  }

  start(gameAnalysisId: number, guessWhite: boolean): Observable<GuessSession> {
    return this.http.post<GuessSession>('/api/guess-sessions', { gameAnalysisId, guessWhite });
  }

  get(id: number): Observable<GuessSession> {
    return this.http.get<GuessSession>(`/api/guess-sessions/${id}`);
  }

  /** `uci` leer = passen: 0 Punkte, keine Strafe. */
  guess(id: number, uci: string | null, addSeconds: number): Observable<GuessResult> {
    return this.http.post<GuessResult>(`/api/guess-sessions/${id}/guess`, { uci, addSeconds });
  }

  review(id: number): Observable<GuessReviewMove[]> {
    return this.http.get<GuessReviewMove[]>(`/api/guess-sessions/${id}/review`);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/guess-sessions/${id}`);
  }
}
