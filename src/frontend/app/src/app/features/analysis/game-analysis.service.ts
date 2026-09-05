import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type GameAnalysisStatus = 'pending' | 'running' | 'done' | 'failed';

/** Eine Stellung der Partie. Die KANDIDATENLISTE kommt bewusst nicht mit — sie ist die Grundlage
 *  der späteren Punktepartie und bleibt serverseitig (wer sie ausliefert, liefert die Lösung mit). */
export interface GameAnalysisPosition {
  ply: number;
  moveNumber: number;
  white: boolean;
  san: string;
  uci: string;
  fen: string;
  evalText: string | null;
  depth: number;
  analyzed: boolean;
}

/** Eine ganze Partie, von der Hintergrund-Engine Stellung für Stellung durchgerechnet. */
export interface GameAnalysis {
  id: number;
  title: string | null;
  white: string | null;
  black: string | null;
  result: string | null;
  event: string | null;
  targetDepth: number;
  multiPv: number;
  engineId: string | null;
  status: GameAnalysisStatus;
  plyCount: number;
  /** Wie viele Stellungen schon fertig sind — der Fortschritt der Partie. */
  analyzedPlies: number;
  lastError: string | null;
  createdAt: string;
  finishedAt: string | null;
  /** Nur im Detail-Abruf gefüllt. */
  positions?: GameAnalysisPosition[];
}

export interface CreateGameAnalysisRequest {
  pgn: string;
  title?: string;
  targetDepth?: number;
  multiPv?: number;
}

@Injectable({ providedIn: 'root' })
export class GameAnalysisService {
  private http = inject(HttpClient);

  /** Vorgaben des Servers (GameAnalysisDefaults) — hier gespiegelt für die Formular-Vorbelegung. */
  static readonly DefaultDepth = 30;
  /** 5 = Protokoll-Maximum des Lichess-External-Engine-Protokolls. */
  static readonly MaxMultiPv = 5;
  static readonly MaxDepth = 60;

  list(): Observable<GameAnalysis[]> {
    return this.http.get<GameAnalysis[]>('/api/game-analyses');
  }

  get(id: number): Observable<GameAnalysis> {
    return this.http.get<GameAnalysis>(`/api/game-analyses/${id}`);
  }

  create(req: CreateGameAnalysisRequest): Observable<GameAnalysis> {
    return this.http.post<GameAnalysis>('/api/game-analyses', req);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/game-analyses/${id}`);
  }
}
