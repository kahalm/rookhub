import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** „Züge vergleichen" (0.602.0) — siehe `MoveComparisonService` in der API. Bewertungen aus WEISS-Sicht wie auf dem
 *  Analysebrett. */
export type MoveComparisonStatus = 'candidates' | 'replies' | 'explaining' | 'done' | 'failed';
export type MoveComparisonLineState = 'pending' | 'done' | 'failed' | 'illegal';

export interface MoveComparisonReply { uci: string; san: string; evalText: string | null; line: string[]; }

/** Eine der besten Antworten auf einen schwächeren Kandidaten, gespielt nach dem BESTEN: `line` beginnt mit dem
 *  eigenen Zug danach. */
export interface MoveComparisonTest {
  replyUci: string; replySan: string; state: MoveComparisonLineState; depth: number; evalText: string | null; line: string[];
}

export interface MoveComparisonCandidate {
  uci: string; san: string; state: MoveComparisonLineState; depth: number; evalText: string | null; isBest: boolean;
  replies: MoveComparisonReply[]; tests: MoveComparisonTest[]; explanation: string | null;
}

export interface MoveComparison {
  id: number; fen: string; title: string | null; depth: number; status: MoveComparisonStatus; error: string | null;
  whiteToMove: boolean; bestUci: string | null; language: string; explanationsAvailable: boolean;
  pending: number; total: number; createdAt: string; finishedAt: string | null;
  candidates: MoveComparisonCandidate[];
}

export interface MoveComparisonSummary {
  id: number; fen: string; title: string | null; status: MoveComparisonStatus; createdAt: string; moves: string[]; bestSan: string | null;
}

export interface MoveComparisonStatusInfo {
  engineAvailable: boolean; ownEngine: boolean; explanations: boolean; maxCandidates: number;
  defaultDepth: number; maxDepth: number; openComparisons: number; maxOpen: number;
}

export interface CreateMoveComparisonRequest { fen: string; moves: string[]; depth?: number; lang?: string; title?: string; }

@Injectable({ providedIn: 'root' })
export class MoveComparisonService {
  private readonly http = inject(HttpClient);

  status(): Observable<MoveComparisonStatusInfo> {
    return this.http.get<MoveComparisonStatusInfo>('/api/move-comparisons/status');
  }

  list(): Observable<MoveComparisonSummary[]> {
    return this.http.get<MoveComparisonSummary[]>('/api/move-comparisons');
  }

  get(id: number): Observable<MoveComparison> {
    return this.http.get<MoveComparison>(`/api/move-comparisons/${id}`);
  }

  /** 400 trägt `{ reason, message }` (invalid-move, too-few-moves, too-many-open, no-engine …). */
  create(req: CreateMoveComparisonRequest): Observable<MoveComparison> {
    return this.http.post<MoveComparison>('/api/move-comparisons', req);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/move-comparisons/${id}`);
  }
}

/** Nummerierte Zugfolge ab einer Stellung: „14.Nf3 Qb6 15.Qc2", mit Schwarz am Zug „14...Qb6 15.Qc2". */
export function numberedLine(fen: string, sans: readonly string[], maxPlies = sans.length): string {
  const parts = fen.split(' ');
  let white = parts[1] !== 'b';
  let n = parseInt(parts[5] ?? '1', 10) || 1;
  const out: string[] = [];
  for (const san of sans.slice(0, maxPlies)) {
    out.push(white ? `${n}.${san}` : out.length === 0 ? `${n}...${san}` : san);
    if (!white) n++;
    white = !white;
  }
  return out.join(' ');
}
