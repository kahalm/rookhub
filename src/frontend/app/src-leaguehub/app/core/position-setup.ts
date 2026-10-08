import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Chess, Square } from 'chess.js';

/**
 * Stellung direkt eingeben (Zug-Editor, Modus „Stellung", 2026-10-08) und die häufigsten Zugfolgen dorthin aus dem LOKALEN
 * Explorer vorschlagen lassen (`GET /api/explorer/paths`, Server: `ExplorerPathFinder`).
 *
 * Das Brett ist ein Feld aus 64 Zeichen: Index 0 = a8, 7 = h8, 56 = a1, 63 = h1 (Lesereihenfolge einer FEN), leer = ''.
 */

export type Side = 'w' | 'b';
export type SetupBoard = readonly string[];

export const START_PLACEMENT = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR';
export const PIECES = ['K', 'Q', 'R', 'B', 'N', 'P', 'k', 'q', 'r', 'b', 'n', 'p'] as const;

/** Platzierungsteil einer FEN → 64 Felder, oder `null`, wenn er nicht aufgeht. */
export function boardFromPlacement(placement: string): string[] | null {
  const ranks = placement.trim().split('/');
  if (ranks.length !== 8) return null;
  const board: string[] = [];
  for (const rank of ranks) {
    let n = 0;
    for (const ch of rank) {
      if (/[1-8]/.test(ch)) { for (let i = 0; i < +ch; i++) board.push(''); n += +ch; }
      else if (/[KQRBNPkqrbnp]/.test(ch)) { board.push(ch); n++; }
      else return null;
    }
    if (n !== 8) return null;
  }
  return board;
}

export function emptyBoard(): string[] {
  return Array(64).fill('');
}

/** 64 Felder → Platzierungsteil der FEN. */
export function placementOf(board: SetupBoard): string {
  const rows: string[] = [];
  for (let r = 0; r < 8; r++) {
    let row = '';
    let empty = 0;
    for (let f = 0; f < 8; f++) {
      const p = board[r * 8 + f];
      if (!p) { empty++; continue; }
      if (empty) { row += empty; empty = 0; }
      row += p;
    }
    rows.push(row + (empty ? empty : ''));
  }
  return rows.join('/');
}

/** Rochaderechte aus der Stellung: König und Turm auf ihren Ausgangsfeldern. */
export function castlingOf(board: SetupBoard): string {
  let c = '';
  if (board[60] === 'K' && board[63] === 'R') c += 'K';
  if (board[60] === 'K' && board[56] === 'R') c += 'Q';
  if (board[4] === 'k' && board[7] === 'r') c += 'k';
  if (board[4] === 'k' && board[0] === 'r') c += 'q';
  return c || '-';
}

/** Volle FEN: Rochaderechte abgeleitet, en passant „-", Zugzähler unbekannt (0 1). */
export function composeFen(board: SetupBoard, side: Side): string {
  return `${placementOf(board)} ${side} ${castlingOf(board)} - 0 1`;
}

/** Eingetippte/eingefügte FEN lesen (nur der Platzierungsteil ist Pflicht, Seite sonst Weiß). */
export function parseFenInput(text: string): { board: string[]; side: Side } | null {
  const parts = text.trim().split(/\s+/);
  if (!parts[0]) return null;
  const board = boardFromPlacement(parts[0]);
  if (!board) return null;
  const side: Side = parts[1] === 'b' ? 'b' : 'w';
  return { board, side };
}

function squareOf(index: number): Square {
  return `${'abcdefgh'[index % 8]}${8 - Math.floor(index / 8)}` as Square;
}

/** Warum die Stellung nicht gehen kann, oder `null`. */
export function positionProblem(board: SetupBoard, side: Side): string | null {
  const whiteKings = board.filter(p => p === 'K').length;
  const blackKings = board.filter(p => p === 'k').length;
  if (whiteKings !== 1 || blackKings !== 1) return 'Jede Seite braucht genau einen König.';
  for (let f = 0; f < 8; f++)
    if (/[Pp]/.test(board[f]) || /[Pp]/.test(board[56 + f])) return 'Bauern können nicht auf der 1. oder 8. Reihe stehen.';
  // Die Seite, die NICHT am Zug ist, darf nicht im Schach stehen (sie hätte gerade einen illegalen Zug gemacht).
  const otherKing = board.indexOf(side === 'w' ? 'k' : 'K');
  try {
    const chess = new Chess(composeFen(board, side), { skipValidation: true });
    if (chess.isAttacked(squareOf(otherKing), side)) return `${side === 'w' ? 'Schwarz' : 'Weiß'} steht im Schach, ist aber nicht am Zug.`;
  } catch {
    return 'Diese Stellung lässt sich nicht lesen.';
  }
  return null;
}

/** Ein Vorschlag des Servers. */
export interface ExplorerPath {
  /** Englische SAN. */
  moves: string[];
  uci: string[];
  estGames: number;
  /** Anteil an den Partien der Zielstellung (0..1). */
  share: number;
}

export interface ExplorerPathsResult {
  opening: { eco: string | null; name: string } | null;
  games: number;
  paths: ExplorerPath[];
  searched: number;
  queries: number;
  truncated: boolean;
  failed: boolean;
}

/** Halbzüge, bis zu denen die Suche prüft. */
export const MAX_SEARCH_PLIES = 20;

@Injectable({ providedIn: 'root' })
export class ExplorerPathsService {
  private readonly http = inject(HttpClient);
  private local: Promise<boolean> | null = null;

  /** Gibt es einen lokalen Explorer? Ohne Antwort: ja (der Knopf meldet es dann selbst). */
  hasLocal(): Promise<boolean> {
    this.local ??= firstValueFrom(this.http.get<{ local: boolean }>('/api/repertoires/explorer/sources'))
      .then(r => !!r.local, () => true);
    return this.local;
  }

  paths(fen: string): Promise<ExplorerPathsResult> {
    return firstValueFrom(this.http.get<ExplorerPathsResult>('/api/explorer/paths',
      { params: { fen, maxPlies: MAX_SEARCH_PLIES, source: 'local' } }));
  }
}

/** „≈ 1,2 Mio. Partien" / „≈ 74.000 Partien" / „≈ 1 Partie". */
export function formatGames(n: number): string {
  if (n >= 1_000_000) return `≈ ${(n / 1_000_000).toLocaleString('de-DE', { maximumFractionDigits: 1 })} Mio. Partien`;
  if (n >= 1000) {
    const digits = Math.floor(Math.log10(n)) + 1;
    const step = Math.pow(10, digits - 2);
    return `≈ ${(Math.round(n / step) * step).toLocaleString('de-DE')} Partien`;
  }
  const r = Math.max(1, Math.round(n));
  return `≈ ${r} ${r === 1 ? 'Partie' : 'Partien'}`;
}

/** „89,4 %", unter 0,1 % „< 0,1 %". */
export function formatShare(x: number): string {
  if (x > 0 && x < 0.001) return '< 0,1 %';
  return `${(x * 100).toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} %`;
}

/** Fehler der Suche als Satz. */
export function pathsErrorText(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const reason = (err.error as { reason?: string } | null)?.reason;
    if (reason === 'noLocalExplorer') return 'Auf diesem Server gibt es keinen lokalen Eröffnungs-Explorer.';
    if (reason === 'invalidFen') return 'Diese Stellung kann der Server nicht lesen.';
    if (err.status === 429) return 'Zu viele Suchen in kurzer Zeit — bitte eine Minute warten.';
    if (err.status === 504 || err.status === 0) return 'Die Suche hat zu lange gedauert oder die Verbindung ist weg — bitte noch einmal versuchen.';
  }
  return 'Die Suche hat nicht geklappt.';
}
