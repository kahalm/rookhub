import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, shareReplay } from 'rxjs';

/** Ergebnis aus Sicht der Seite am Zug (bzw. bei `moves` der ziehenden Seite) — siehe `TablebaseService` in der API. */
export type TablebaseCategory = 'win' | 'cursed-win' | 'maybe-win' | 'draw' | 'maybe-loss' | 'blessed-loss' | 'loss' | 'unknown';

export interface TablebaseMove {
  uci: string; san: string; category: TablebaseCategory; dtz: number | null; dtm: number | null;
  zeroing: boolean; checkmate: boolean; stalemate: boolean;
}

export interface TablebaseResult {
  status: 'ok' | 'tooManyPieces' | 'invalid' | 'unavailable' | 'rateLimited';
  category: TablebaseCategory | null; dtz: number | null; dtm: number | null;
  checkmate: boolean; stalemate: boolean; insufficientMaterial: boolean;
  moves: TablebaseMove[];
}

/** So viele Steine kennt die Tablebase von Lichess (Syzygy). */
export const TABLEBASE_MAX_PIECES = 7;

/** Steine auf dem Brett (Könige mitgezählt); -1 = keine lesbare FEN. */
export function tablebasePieceCount(fen: string | null | undefined): number {
  const board = (fen ?? '').trim().split(' ')[0];
  if (board.split('/').length !== 8) return -1;
  return [...board].filter(c => /[a-zA-Z]/.test(c)).length;
}

const UNAVAILABLE: TablebaseResult = {
  status: 'unavailable', category: null, dtz: null, dtm: null, checkmate: false, stalemate: false, insufficientMaterial: false, moves: [],
};

/** Endspiel-Datenbank (0.729.0) über `GET /api/tablebase` — je Stellung einmal gefragt (Erfolge bleiben im Speicher der Seite). */
@Injectable({ providedIn: 'root' })
export class TablebaseService {
  private readonly http = inject(HttpClient);
  private readonly cache = new Map<string, Observable<TablebaseResult>>();

  lookup(fen: string): Observable<TablebaseResult> {
    const key = fen.split(' ').slice(0, 5).join(' ');
    const hit = this.cache.get(key);
    if (hit) return hit;
    const req = this.http.get<TablebaseResult>('/api/tablebase', { params: { fen } }).pipe(
      catchError(() => { this.cache.delete(key); return of(UNAVAILABLE); }),
      shareReplay(1),
    );
    this.cache.set(key, req);
    return req;
  }

  /** Eine fehlgeschlagene Antwort nicht festhalten — beim nächsten Besuch der Stellung neu fragen. */
  forget(fen: string): void { this.cache.delete(fen.split(' ').slice(0, 5).join(' ')); }
}
