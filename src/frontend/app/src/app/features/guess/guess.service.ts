import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { getOrCreateAnonSessionId } from '../../core/anon-session';

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

/** Ein bereits gespielter Halbzug — `fen` ist die Stellung NACH diesem Zug. */
export interface GuessHistoryMove {
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
  /** Stellung vor dem ersten Zug der Partie (nur wenn es etwas zum Blättern gibt). */
  startFen: string | null;
  /**
   * Die Partie BIS HIERHIN: Eröffnungsvorlauf plus alles seither Gespielte. Der letzte Eintrag
   * erzeugt die Aufgabenstellung — die Liste endet also genau vor der Lösung.
   */
  history: GuessHistoryMove[];
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
  /** Bester Zug der Engine samt Bewertung; `null`, wenn die Stellung keine Kandidatenliste hat. */
  bestSan: string | null;
  bestEval: string | null;
  /** Bewertung des Partiezuges — zum Vergleich mit dem besten Zug. */
  gameEval: string | null;
}

/**
 * Zugang zur Punktepartie — angemeldet ODER nicht.
 *
 * Ohne Anmeldung laufen dieselben Aufrufe über `…/anonymous` und nehmen die Sitzungskennung des
 * Browsers mit; der FORTSCHRITT liegt in beiden Fällen am Server. Das ist keine Bequemlichkeit,
 * sondern die eiserne Regel: der Partiezug kommt erst als Antwort auf den Rateversuch, und dafür
 * muss der Server wissen, bei welchem Halbzug die Sitzung steht. Ein Client, der selbst mitzählt,
 * könnte jeden Zug der Partie einzeln abfragen.
 */
@Injectable({ providedIn: 'root' })
export class GuessService {
  private http = inject(HttpClient);
  private auth = inject(AuthService);

  /** EIGENER Schlüssel, nicht der der anonymen Puzzle-Versuche: dort liegen bei Bestandsnutzern
   *  teils Kennungen aus einem älteren Generator, die das Muster des Servers nicht erfüllen —
   *  geteilt würde ein solcher Altwert hier jeden Aufruf mit 400 beenden. */
  private static readonly AnonKey = 'rookhub_guess_session';

  private get anonymous(): boolean { return !this.auth.isLoggedIn; }

  private sessionId(): string { return getOrCreateAnonSessionId(GuessService.AnonKey); }

  /** Basis-Pfad und die Abfrage-Parameter, die ohne Anmeldung dazugehören. */
  private base(): string {
    return this.anonymous ? '/api/guess-sessions/anonymous' : '/api/guess-sessions';
  }

  private params(): { params?: { sessionId: string } } {
    return this.anonymous ? { params: { sessionId: this.sessionId() } } : {};
  }

  private body<T extends object>(payload: T): T & { sessionId?: string } {
    return this.anonymous ? { ...payload, sessionId: this.sessionId() } : payload;
  }

  list(): Observable<GuessSession[]> {
    return this.http.get<GuessSession[]>(this.base(), this.params());
  }

  /**
   * Durchlauf starten. `guessWhite` WEGLASSEN heißt „die Seite des Gewinners" — genau das ist im
   * kuratierten Bestand gewollt, dort fragt die Auswahl nicht nach der Seite (der Server leitet sie
   * aus dem Ergebnis bzw. der Bewertung der letzten gerechneten Stellung ab).
   */
  start(gameAnalysisId: number, guessWhite?: boolean): Observable<GuessSession> {
    return this.http.post<GuessSession>(this.base(),
      this.body(guessWhite === undefined ? { gameAnalysisId } : { gameAnalysisId, guessWhite }));
  }

  get(id: number): Observable<GuessSession> {
    return this.http.get<GuessSession>(`${this.base()}/${id}`, this.params());
  }

  /** `uci` leer = passen: 0 Punkte, keine Strafe. */
  guess(id: number, uci: string | null, addSeconds: number): Observable<GuessResult> {
    return this.http.post<GuessResult>(`${this.base()}/${id}/guess`, this.body({ uci, addSeconds }));
  }

  review(id: number): Observable<GuessReviewMove[]> {
    return this.http.get<GuessReviewMove[]>(`${this.base()}/${id}/review`, this.params());
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base()}/${id}`, this.params());
  }
}
