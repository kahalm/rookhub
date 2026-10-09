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

/**
 * Figur auf ein Feld setzen (ersetzt, was dort steht). Ein zweiter König derselben Farbe ersetzt den ersten — es gibt nur
 * einen. Gibt ein neues Feld zurück, das alte bleibt unverändert.
 */
export function placePiece(board: SetupBoard, to: number, piece: string): string[] {
  const next = [...board];
  if (piece === 'K' || piece === 'k') { const old = next.indexOf(piece); if (old >= 0) next[old] = ''; }
  next[to] = piece;
  return next;
}

/** Feld leeren. */
export function removePiece(board: SetupBoard, from: number): string[] {
  const next = [...board];
  next[from] = '';
  return next;
}

/**
 * Figur von einem Feld auf ein anderes ziehen (Aufstell-Brett, Drag-and-drop): die Zielfigur wird ersetzt. Leeres
 * Ausgangsfeld oder Ziel = Ausgangsfeld → unverändert (Abbruch).
 */
export function movePiece(board: SetupBoard, from: number, to: number): string[] {
  const piece = board[from];
  if (!piece || from === to) return [...board];
  const next = [...board];
  next[from] = '';
  next[to] = piece;
  return next;
}

/** Figur in Schwarz („Q" → „q"); „x" (löschen) bleibt. */
export function blackOf(piece: string): string {
  return piece === 'x' ? piece : piece.toLowerCase();
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

const START_COUNT: Record<string, number> = { Q: 1, R: 2, B: 2, N: 2 };
const PLURAL: Record<string, [string, string]> = { Q: ['Dame', 'Damen'], R: ['Turm', 'Türme'], B: ['Läufer', 'Läufer'], N: ['Springer', 'Springer'] };

/**
 * Figurenzahl je Seite (Befund Prod 08.10.2026: Springer auf f3 GESETZT statt gezogen → drei weiße Springer, und die Suche
 * fand nur „keine Zugfolge"): höchstens 8 Bauern; mehr Damen/Türme/Läufer/Springer als in der Grundstellung nur so viele,
 * wie Bauern fehlen (Umwandlung).
 */
export function materialProblem(board: SetupBoard): string | null {
  for (const white of [true, false]) {
    const who = white ? 'Weiß' : 'Schwarz';
    const count = (t: string) => board.filter(p => p === (white ? t : t.toLowerCase())).length;
    const pawns = count('P');
    if (pawns > 8) return `${who} hat ${pawns} Bauern — höchstens 8 gehen.`;
    let extra = 0;
    let first: string | null = null;
    for (const t of ['Q', 'R', 'B', 'N']) {
      const over = count(t) - START_COUNT[t];
      if (over > 0) { extra += over; first ??= t; }
    }
    if (first && extra > 8 - pawns) {
      const n = count(first), start = START_COUNT[first];
      const [one, many] = PLURAL[first];
      return `${who} hat ${n} ${n === 1 ? one : many} — in der Grundstellung ${start === 1 ? 'ist es 1' : `sind es ${start}`}`
        + `${8 - pawns > 0 ? ` (mehr nur durch Umwandlung, und es ${8 - pawns === 1 ? 'fehlt nur 1 Bauer' : `fehlen nur ${8 - pawns} Bauern`})` : ''}; Figur wegnehmen oder ziehen statt setzen.`;
    }
  }
  return null;
}

/** Warum die Stellung nicht gehen kann, oder `null`. */
export function positionProblem(board: SetupBoard, side: Side): string | null {
  const whiteKings = board.filter(p => p === 'K').length;
  const blackKings = board.filter(p => p === 'k').length;
  if (whiteKings !== 1 || blackKings !== 1) return 'Jede Seite braucht genau einen König.';
  for (let f = 0; f < 8; f++)
    if (/[Pp]/.test(board[f]) || /[Pp]/.test(board[56 + f])) return 'Bauern können nicht auf der 1. oder 8. Reihe stehen.';
  const material = materialProblem(board);
  if (material) return material;
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

/** Grundfelder je Figurentyp (Index wie das Brett: 0 = a8, 63 = h1). Bauern stehen auf ihrer Grundreihe. */
const HOME: Record<string, readonly number[]> = {
  K: [60], Q: [59], R: [56, 63], B: [58, 61], N: [57, 62],
  k: [4], q: [3], r: [0, 7], b: [2, 5], n: [1, 6],
};

/** Mindestzahl Züge, mit denen ein Bauer auf seinem Feld steht (Doppelschritt von der Grundreihe = ein Zug). */
function pawnMoves(index: number, white: boolean): number {
  const rank = 8 - Math.floor(index / 8);
  const steps = white ? rank - 2 : 7 - rank;
  if (steps <= 0) return 0;
  return steps <= 2 ? 1 : steps - 1;
}

/**
 * Wie viele Züge jede Seite MINDESTENS gemacht hat (2026-10-08, Prod-Befund: Italienisch mit „Weiß am Zug" aufgebaut —
 * der Explorer kennt die Stellung so aus 135 Partien, mit Schwarz am Zug aus 3 Mio.). Dieselbe Idee wie `SideNeed` im Server,
 * vereinfacht: jede Figur, die nicht auf einem Grundfeld ihres Typs steht, ein Zug; je Bauer die Schritte von der Grundreihe
 * (Doppelschritt = einer); König + Turm rochiert (Kg1+Tf1, Kc1+Td1) zusammen ein Zug. Geschlagene Figuren zählen nicht —
 * es ist eine Schätzung, keine Beweispartie.
 */
export function movesMade(board: SetupBoard): { white: number; black: number } {
  let white = 0;
  let black = 0;
  board.forEach((p, i) => {
    if (!p) return;
    const isWhite = p === p.toUpperCase();
    const n = p === 'P' || p === 'p' ? pawnMoves(i, isWhite) : HOME[p].includes(i) ? 0 : 1;
    if (isWhite) white += n; else black += n;
  });
  if ((board[62] === 'K' && board[61] === 'R') || (board[58] === 'K' && board[59] === 'R')) white--;
  if ((board[6] === 'k' && board[5] === 'r') || (board[2] === 'k' && board[3] === 'r')) black--;
  return { white, black };
}

/** Welche Seite nach der Figurenstellung am Zug sein muss: mehr weiße Züge → Schwarz, gleich viele → Weiß; weniger weiße
 * Züge als schwarze ist eigentlich unmöglich (`impossible`, Seite dann `null`). */
export function expectedSide(board: SetupBoard): { side: Side | null; white: number; black: number; impossible: boolean } {
  const { white, black } = movesMade(board);
  if (white < black) return { side: null, white, black, impossible: true };
  return { side: white > black ? 'b' : 'w', white, black, impossible: false };
}

function moveWord(n: number): string {
  return n === 1 ? '1 Zug' : `${n} Züge`;
}

/** „Seite am Zug automatisch: Schwarz (Weiß hat 4 Züge gemacht, Schwarz 3)". */
export function autoSideText(e: { side: Side | null; white: number; black: number }): string {
  return `Seite am Zug automatisch: ${e.side === 'b' ? 'Schwarz' : 'Weiß'} (Weiß hat ${moveWord(e.white)} gemacht, Schwarz ${e.black})`;
}

/** Warnung, wenn die von Hand gewählte Seite nicht zur Figurenstellung passt (sonst `null`). */
export function sideWarning(board: SetupBoard, side: Side): string | null {
  const e = expectedSide(board);
  if (e.impossible) {
    return `Schwarz hat mehr Züge gemacht als Weiß (Weiß ${e.white}, Schwarz ${e.black}) — so kann die Stellung kaum entstanden sein. `
      + 'Die Schätzung ist nicht exakt; gesucht wird trotzdem.';
  }
  if (e.side === side) return null;
  return side === 'w'
    ? 'Weiß am Zug passt nicht zur Figurenstellung — Schwarz hat weniger Züge gemacht.'
    : 'Schwarz am Zug passt nicht zur Figurenstellung — beide Seiten haben gleich viele Züge gemacht.';
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
  /** Antworten aus dem Speicher des Servers (0.727.3) — nur Information. */
  cached?: number;
  truncated: boolean;
  failed: boolean;
  /** Partien derselben Stellung mit der anderen Seite am Zug — nur bei wenigen/keinen Partien gefragt, sonst `null`. */
  otherSideGames?: number | null;
}

/** Hinweis „mit der anderen Seite am Zug kennt der Explorer die Stellung"? Bei 0 Partien sobald es dort welche gibt, sonst
 * erst ab zehnmal so vielen. */
export function otherSideHint(r: ExplorerPathsResult): boolean {
  const other = r.otherSideGames ?? 0;
  return !r.failed && other > 0 && (r.games === 0 || other >= 10 * r.games);
}

/** Halbzüge, bis zu denen die Suche prüft. */
export const MAX_SEARCH_PLIES = 20;

/** Runden einer Suche (0.727.3): kommt eine Antwort mit `truncated`, fragt die Seite von selbst noch einmal — der Server hat
 * die Antworten der vorigen Runde im Speicher und kommt mit demselben Budget weiter (Prod 09.10.: D00 nach 15 Halbzügen
 * kalt 0 Wege, in Runde 2/3 dann 5). Drei Runden passen ins Rate-Limit `explorer-paths` (10/min je Konto). */
export const MAX_ROUNDS = 3;

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
