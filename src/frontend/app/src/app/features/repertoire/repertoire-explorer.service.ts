import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EMPTY, Observable, catchError, expand, map, of, shareReplay, switchMap, timer } from 'rxjs';
import { Chess } from 'chess.js';
import { START_FEN } from '../../shared/pgn-viewer/pgn-parser';
import { startNumbering } from './repertoire-move-format.util';
import { TrainColor } from './repertoire-color.util';

/**
 * Lochfinder + Linien-Häufigkeiten aus dem Lichess-Explorer (`POST /api/repertoires/{id}/explorer-analysis`).
 * Gerechnet wird am Server (`RepertoireReach`); hier liegen die Typen, die gemerkte Auswahl und die
 * Schleife, die so lange nachfragt, bis alles ausgewertet ist — der Server antwortet nach einem
 * Zeitbudget mit dem bisherigen Stand, und das schon Abgefragte liegt dort im Speicher.
 */

export type ExplorerDatabase = 'lichess' | 'masters';

/** Woher die Zahlen kommen: explorer.lichess.ovh oder der eigene Explorer im Stack. */
export type ExplorerSource = 'online' | 'local';

/** Welche Quellen der Server anbietet (`GET /api/repertoires/explorer/sources`). */
export interface ExplorerSources {
  online: boolean;
  local: boolean;
  /** Elo-Stufen/Bedenkzeiten, für die der LOKALE Bestand Partien hat. */
  localRatings: number[];
  localSpeeds: string[];
}

const NO_LOCAL: ExplorerSources = { online: true, local: false, localRatings: [], localSpeeds: [] };

/** Die Elo-Stufen des Lichess-Explorers (Spiegel von `ExplorerQuery.AllowedRatings`). */
export const EXPLORER_RATINGS: readonly number[] = [0, 1000, 1200, 1400, 1600, 1800, 2000, 2200, 2500];

/** Die Bedenkzeiten des Lichess-Explorers (Spiegel von `ExplorerQuery.AllowedSpeeds`). */
export const EXPLORER_SPEEDS: readonly string[] = ['ultraBullet', 'bullet', 'blitz', 'rapid', 'classical', 'correspondence'];

export interface ExplorerSettings {
  source: ExplorerSource;
  database: ExplorerDatabase;
  ratings: number[];
  speeds: string[];
  /** Ab diesem Anteil (Prozent der Partien in der Stellung) ist ein fehlender Gegnerzug ein Loch. */
  thresholdPercent: number;
}

/** Vorgabe: der LOKALE Explorer mit den Meisterpartien (0.504.1) — ohne Token, ohne Drossel, und
 *  Meisterpartien sind die Referenz für Eröffnungen. Hat der Server keinen lokalen Explorer, gilt
 *  online (`effectiveSettings`/Komponenten), ohne dass dieser Rückfall gespeichert wird. */
export const DEFAULT_EXPLORER_SETTINGS: ExplorerSettings = {
  source: 'local',
  database: 'masters',
  ratings: [1600, 1800, 2000],
  speeds: ['blitz', 'rapid', 'classical'],
  thresholdPercent: 1,
};

export const MIN_THRESHOLD_PERCENT = 0.1;
export const MAX_THRESHOLD_PERCENT = 50;

export interface ExplorerAnalysisRequest {
  /** Nur die Kapitel dieser Farbe; null = alle. */
  color: TrainColor | null;
  /** Trainingsfarbe je Kapitel (`[Black]`-Header) — wie im Trainer. */
  chapterColors: Record<string, TrainColor>;
  source: ExplorerSource;
  database: ExplorerDatabase;
  ratings: number[];
  speeds: string[];
  thresholdPercent: number;
  includeHoles: boolean;
  includeLineFrequencies: boolean;
  /** Häufigkeit JEDER Repertoire-Stellung (für den Baum). */
  includePositionFrequencies?: boolean;
  /** Nichts abfragen, nur mit dem Gespeicherten rechnen (Nachschlag nach einer Lochsuche). */
  cachedOnly?: boolean;
}

export interface RepertoireHole {
  color: TrainColor;
  /** Stellung VOR dem fehlenden Gegnerzug. */
  fen: string;
  startFen: string | null;
  path: string[];
  san: string;
  uci: string;
  /** Anteil in der Stellung, 0…1. */
  share: number;
  games: number;
  positionGames: number;
  /** Wie oft man das Loch mit dem Repertoire erreicht, 0…1. */
  frequency: number;
  opening: string | null;
  eco: string | null;
}

export interface ExplorerAnalysisResult {
  complete: boolean;
  positionsAnalyzed: number;
  positionsPending: number;
  rateLimited: boolean;
  retryAfterSeconds: number | null;
  tokenMissing: boolean;
  tokenInvalid: boolean;
  fetchFailed: boolean;
  holes: RepertoireHole[];
  /** Endstellung einer Linie (erste drei FEN-Felder) → Häufigkeit 0…1. */
  lineFrequencies: Record<string, number> | null;
  /** Jede Stellung (erste drei FEN-Felder) → Häufigkeit 0…1 — nur auf Wunsch. */
  positionFrequencies?: Record<string, number> | null;
}

/** Ein Zug im Eröffnungs-Explorer des Analysebretts (`GET /api/explorer/position`). */
export interface ExplorerPositionMove {
  uci: string;
  san: string;
  games: number;
  white: number;
  draws: number;
  black: number;
  averageRating: number | null;
  /** Eröffnung NACH diesem Zug. */
  opening: string | null;
  eco: string | null;
}

export type ExplorerPositionStatus = 'ok' | 'tokenMissing' | 'tokenInvalid' | 'rateLimited' | 'failed';

/** Zugstatistik EINER Stellung. */
export interface ExplorerPosition {
  status: ExplorerPositionStatus;
  retryAfterSeconds: number | null;
  source: ExplorerSource;
  database: ExplorerDatabase;
  total: number;
  white: number;
  draws: number;
  black: number;
  opening: string | null;
  eco: string | null;
  moves: ExplorerPositionMove[];
}

/** Eine Partie, die eine Stellung erreicht hat (`GET /api/explorer/games`). */
export interface ExplorerGame {
  id: string;
  white: string;
  whiteRating: number | null;
  black: string;
  blackRating: number | null;
  /** 'white' | 'black' | null (Remis). */
  winner: string | null;
  date: string | null;
  speed: string | null;
  /** Link auf lichess.org — fehlt bei den lokalen Meisterpartien. */
  url: string | null;
}

export interface ExplorerGames {
  status: ExplorerPositionStatus;
  retryAfterSeconds: number | null;
  games: ExplorerGame[];
}

const SETTINGS_KEY = 'rookhub_explorer_settings';

/** Gemerkte Auswahl (je Gerät). Unbrauchbares fällt auf die Vorgabe zurück. */
export function readExplorerSettings(): ExplorerSettings {
  try {
    const raw = localStorage.getItem(SETTINGS_KEY);
    if (!raw) return { ...DEFAULT_EXPLORER_SETTINGS };
    const s = JSON.parse(raw) as Partial<ExplorerSettings>;
    const ratings = Array.isArray(s.ratings) ? s.ratings.filter(r => EXPLORER_RATINGS.includes(r)) : [];
    const speeds = Array.isArray(s.speeds) ? s.speeds.filter(x => EXPLORER_SPEEDS.includes(x)) : [];
    const t = Number(s.thresholdPercent);
    return {
      // Fehlt das Feld (gespeichert vor 0.503.0), gilt die Vorgabe — nicht stillschweigend online.
      source: s.source === 'online' || s.source === 'local' ? s.source : DEFAULT_EXPLORER_SETTINGS.source,
      database: s.database === 'lichess' || s.database === 'masters' ? s.database : DEFAULT_EXPLORER_SETTINGS.database,
      ratings: ratings.length ? ratings : [...DEFAULT_EXPLORER_SETTINGS.ratings],
      speeds: speeds.length ? speeds : [...DEFAULT_EXPLORER_SETTINGS.speeds],
      thresholdPercent: Number.isFinite(t) ? clampThreshold(t) : DEFAULT_EXPLORER_SETTINGS.thresholdPercent,
    };
  } catch {
    return { ...DEFAULT_EXPLORER_SETTINGS };
  }
}

export function saveExplorerSettings(s: ExplorerSettings): void {
  try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(s)); } catch { /* Speicher gesperrt → nur diese Sitzung */ }
}

export function clampThreshold(percent: number): number {
  return Math.min(MAX_THRESHOLD_PERCENT, Math.max(MIN_THRESHOLD_PERCENT, Math.round(percent * 10) / 10));
}

/**
 * Nach dieser Antwort nochmal fragen? `null` = aufhören, sonst die Wartezeit in ms. Aufgehört wird,
 * wenn alles da ist, ein Token fehlt/abgelehnt wurde, der Explorer nicht erreichbar war — oder eine
 * Runde ohne Drossel KEINEN Fortschritt brachte (sonst liefe die Schleife endlos gegen eine Wand).
 */
export function nextRoundDelayMs(res: ExplorerAnalysisResult, previous: ExplorerAnalysisResult | null): number | null {
  if (res.complete || res.tokenMissing || res.tokenInvalid || res.fetchFailed) return null;
  if (res.rateLimited) return ((res.retryAfterSeconds ?? 60) + 1) * 1000;
  if (previous && !previous.rateLimited && res.positionsAnalyzed <= previous.positionsAnalyzed) return null;
  return 0;
}

/** Zugfolge mit Nummern ab der Startstellung („1. e4 c5 2. Nf3" bzw. „12… Nf6 13. Bg5"). */
export function formatPath(startFen: string | null, sans: string[]): string {
  const { side, fullMove } = startNumbering(startFen || START_FEN);
  const parts: string[] = [];
  let cur = side;
  let num = fullMove;
  sans.forEach((san, i) => {
    if (cur === 'w') parts.push(`${num}. ${san}`);
    else parts.push(i === 0 ? `${num}… ${san}` : san);
    if (cur === 'b') num++;
    cur = cur === 'w' ? 'b' : 'w';
  });
  return parts.join(' ');
}

/**
 * Auswahl an die LOKALE Quelle anpassen: dort gibt es nur Elo ab 1600 und kein (Ultra-)Bullet — eine
 * Stufe darunter liefert korrekt 0 Partien und damit stillschweigend keine Löcher. Bleibt nichts
 * übrig, gilt die Vorgabe innerhalb der lokalen Grenzen.
 */
export function fitToLocal(s: ExplorerSettings, src: ExplorerSources): ExplorerSettings {
  const ratings = s.ratings.filter(r => src.localRatings.includes(r));
  const speeds = s.speeds.filter(x => src.localSpeeds.includes(x));
  return {
    ...s,
    ratings: ratings.length ? ratings : DEFAULT_EXPLORER_SETTINGS.ratings.filter(r => src.localRatings.includes(r)),
    speeds: speeds.length ? speeds : DEFAULT_EXPLORER_SETTINGS.speeds.filter(x => src.localSpeeds.includes(x)),
  };
}

/** Anteil 0…1 als Prozent — mit so vielen Stellen, dass auch kleine Werte lesbar bleiben. */
export function formatPercent(x: number): string {
  const p = x * 100;
  if (p > 0 && p < 0.01) return `<${(0.01).toLocaleString(undefined, { minimumFractionDigits: 2 })} %`;
  const digits = p >= 10 ? 0 : p >= 1 ? 1 : 2;
  return `${p.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })} %`;
}

/** Beschriftung des fehlenden Zugs mit Nummer („5… g6"). */
export function holeMoveLabel(hole: RepertoireHole): string {
  const parts = hole.fen.split(/\s+/);
  const num = parseInt(parts[5] || '1', 10) || 1;
  return parts[1] === 'b' ? `${num}… ${hole.san}` : `${num}. ${hole.san}`;
}

/** Stellung NACH dem fehlenden Zug samt Zugfeldern — fürs Brett. `null`, wenn der Zug nicht geht. */
export function positionAfterHole(hole: RepertoireHole): { fen: string; lastMove: [string, string] } | null {
  try {
    const chess = new Chess(hole.fen);
    const mv = chess.move(hole.san);
    return mv ? { fen: chess.fen(), lastMove: [mv.from, mv.to] } : null;
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class RepertoireExplorerService {
  /** Sicherheitsdeckel der Schleife — ein Lauf über einen riesigen Kurs braucht einige Dutzend Runden. */
  static readonly MaxRounds = 300;

  private sources$: Observable<ExplorerSources> | null = null;

  constructor(private http: HttpClient) {}

  /** Angebotene Quellen (einmal je Sitzung gefragt; Fehler = nur online). */
  sources(): Observable<ExplorerSources> {
    this.sources$ ??= this.http.get<ExplorerSources>('/api/repertoires/explorer/sources').pipe(
      catchError(() => of(NO_LOCAL)),
      shareReplay(1),
    );
    return this.sources$;
  }

  /** Die gemerkte Auswahl, passend zu dem, was der Server anbietet (lokal weg → online). */
  effectiveSettings(): Observable<ExplorerSettings> {
    const s = readExplorerSettings();
    if (s.source !== 'local') return of(s);
    return this.sources().pipe(map(src => src.local ? fitToLocal(s, src) : { ...s, source: 'online' as const }));
  }

  analyze(repertoireId: number, req: ExplorerAnalysisRequest): Observable<ExplorerAnalysisResult> {
    return this.http.post<ExplorerAnalysisResult>(`/api/repertoires/${repertoireId}/explorer-analysis`, req);
  }

  /** Zugstatistik einer Stellung — Elo/Tempo gehen nur bei der Lichess-Datenbank mit. */
  position(fen: string, s: ExplorerSettings): Observable<ExplorerPosition> {
    return this.http.get<ExplorerPosition>('/api/explorer/position', { params: this.params(fen, s) });
  }

  private params(fen: string, s: ExplorerSettings): Record<string, string> {
    const params: Record<string, string> = { fen, source: s.source, database: s.database };
    if (s.database === 'lichess') {
      params['ratings'] = s.ratings.join(',');
      params['speeds'] = s.speeds.join(',');
    }
    return params;
  }

  /** Eine Handvoll Partien, die diese Stellung erreicht haben — gleiche Parameter wie {@link position}. */
  games(fen: string, s: ExplorerSettings): Observable<ExplorerGames> {
    return this.http.get<ExplorerGames>('/api/explorer/games', { params: this.params(fen, s) });
  }

  /** Fragt Runde um Runde, bis {@link nextRoundDelayMs} aufhört; jede Antwort kommt heraus (Fortschritt). */
  run(repertoireId: number, req: ExplorerAnalysisRequest): Observable<ExplorerAnalysisResult> {
    let previous: ExplorerAnalysisResult | null = null;
    let rounds = 0;
    return this.analyze(repertoireId, req).pipe(
      expand(res => {
        const delay = nextRoundDelayMs(res, previous);
        previous = res;
        if (delay === null || ++rounds >= RepertoireExplorerService.MaxRounds) return EMPTY;
        return timer(delay).pipe(switchMap(() => this.analyze(repertoireId, req)));
      }),
    );
  }
}
