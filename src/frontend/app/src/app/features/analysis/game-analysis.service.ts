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
  /** Kuratierter Bestand: als Punktepartie für jeden spielbar, auch ohne Anmeldung. */
  isPublic: boolean;
  /** Trägt das Quell-PGN Kommentare? Grundlage des Filters „alle / nur kommentierte". */
  annotated: boolean;
  /** Welche Seite man übernimmt — nur im kuratierten Bestand gefüllt (dort wählt man sie nicht). */
  guessWhite?: boolean | null;
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

/** Warum ein Einwurf auf der Punktepartie-Seite abgelehnt wurde (Server-Grund, hier lokalisiert). */
export type GuessUploadReason = 'too-many-open' | 'no-engine' | 'invalid-pgn';

/** Ob und wie oft auf der Punktepartie-Seite noch eingeworfen werden darf. */
export interface GuessUploadStatus {
  /** Ohne Engine (eigene oder Haus) zeigt die Seite das Feld gar nicht erst. */
  engineAvailable: boolean;
  /** Nur für den Hinweistext — die Tiefe ist so oder so fest. */
  ownEngine: boolean;
  openGames: number;
  maxGames: number;
}

/** Tempo und Restdauer, vom Server aus den Zeitstempeln der gerechneten Stellungen gerechnet. */
export interface AnalysisThroughput {
  /** Stellungen je Minute; 0 = im Fenster ist nichts passiert. */
  perMinute: number;
  analyzedInWindow: number;
  /** Tatsächlich gemessene Spanne in Minuten (nicht die Fenster-Vorgabe). */
  windowMinutes: number;
  remaining: number;
  /** Hochgerechnete Restdauer in Minuten; null ohne Tempo oder ohne Rest. */
  etaMinutes: number | null;
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

  /** Der kuratierte Bestand — ohne Anmeldung abrufbar; nur SPIELBARE Partien (mindestens eine
   *  gerechnete Stellung). Liefert Kopfdaten und Fortschritt, nicht die Zugliste. */
  listPublic(): Observable<GameAnalysis[]> {
    return this.http.get<GameAnalysis[]>('/api/game-analyses/public');
  }

  /** Partie in den kuratierten Bestand aufnehmen/herausnehmen (Besitzer oder Admin). */
  setPublic(id: number, isPublic: boolean): Observable<{ isPublic: boolean }> {
    return this.http.put<{ isPublic: boolean }>(`/api/game-analyses/${id}/public`, { isPublic });
  }

  get(id: number): Observable<GameAnalysis> {
    return this.http.get<GameAnalysis>(`/api/game-analyses/${id}`);
  }

  create(req: CreateGameAnalysisRequest): Observable<GameAnalysis> {
    return this.http.post<GameAnalysis>('/api/game-analyses', req);
  }

  /** Eine Partie auf der PUNKTEPARTIE-Seite einwerfen. Bewusst ohne Tiefe und Linienzahl: beides
   *  setzt der Server. Gerechnet wird auf der eigenen Hintergrund-Engine, sonst auf der Haus-Engine. */
  createForGuess(pgn: string, title?: string): Observable<GameAnalysis> {
    return this.http.post<GameAnalysis>('/api/game-analyses/guess', { pgn, title });
  }

  guessUploadStatus(): Observable<GuessUploadStatus> {
    return this.http.get<GuessUploadStatus>('/api/game-analyses/guess/status');
  }

  /** Tempo und Restdauer aus der HISTORIE — steht sofort da, auch wenn der Reiter zu war. */
  throughput(): Observable<AnalysisThroughput> {
    return this.http.get<AnalysisThroughput>('/api/game-analyses/throughput');
  }

  /** Die Partie noch einmal anstossen: alles Ungerechnete kommt frisch in die Warteschlange, und
   *  zwar auf einer NEU gewaehlten Engine — ein Auftrag wechselt von sich aus nie die Engine. */
  restart(id: number): Observable<GameAnalysis> {
    return this.http.post<GameAnalysis>(`/api/game-analyses/${id}/restart`, {});
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/game-analyses/${id}`);
  }
}
