import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BookPuzzleDto } from '../puzzles/puzzle.service';

/** `solving` = Zyklus läuft, `break` = Pause (nächster Zyklus ab `nextCycleAt`), `ready` = nächster Zyklus kann
 *  beginnen, `completed` = Methode abgeschlossen. */
export type WoodpeckerStatus = 'solving' | 'break' | 'ready' | 'completed';

export interface WoodpeckerBook { bookId: number; displayName: string; puzzleCount: number; }

export interface WoodpeckerCycle {
  number: number;
  targetDays: number;
  startedAt: string;
  deadline: string;
  finishedAt: string | null;
  attempted: number;
  solved: number;
  timeSeconds: number;
}

export interface WoodpeckerPlan {
  id: number;
  name: string;
  createdAt: string;
  completedAt: string | null;
  books: WoodpeckerBook[];
  setSize: number;
  availableCount: number;
  currentCycle: number;
  maxCycles: number;
  status: WoodpeckerStatus;
  nextCycleAt: string | null;
  cycles: WoodpeckerCycle[];
}

export interface WoodpeckerNext { puzzle: BookPuzzleDto | null; plan: WoodpeckerPlan; }

/** Woodpecker-Training über Kurse (`/api/woodpecker`, Regeln fest im Backend: `WoodpeckerRules`). */
@Injectable({ providedIn: 'root' })
export class WoodpeckerService {
  private http = inject(HttpClient);
  private readonly base = '/api/woodpecker';

  list(): Observable<WoodpeckerPlan[]> { return this.http.get<WoodpeckerPlan[]>(this.base); }

  get(planId: number): Observable<WoodpeckerPlan> { return this.http.get<WoodpeckerPlan>(`${this.base}/${planId}`); }

  /** `startedAt` (ISO, UTC) leer = jetzt. */
  create(bookIds: number[], startedAt?: string): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(this.base, { bookIds, startedAt: startedAt ?? null });
  }

  delete(planId: number): Observable<void> { return this.http.delete<void>(`${this.base}/${planId}`); }

  updateStart(planId: number, startedAt: string): Observable<WoodpeckerPlan> {
    return this.http.put<WoodpeckerPlan>(`${this.base}/${planId}/start`, { startedAt });
  }

  finishSet(planId: number): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(`${this.base}/${planId}/finish-set`, {});
  }

  startNextCycle(planId: number): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(`${this.base}/${planId}/next-cycle`, {});
  }

  next(planId: number): Observable<WoodpeckerNext> {
    return this.http.get<WoodpeckerNext>(`${this.base}/${planId}/next`);
  }

  recordAttempt(planId: number, bookPuzzleId: number, solved: boolean, timeSeconds: number): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(`${this.base}/${planId}/attempts`, { bookPuzzleId, solved, timeSeconds });
  }
}

/** Lokales Datum `YYYY-MM-DD` → Beginn dieses Tages (lokale Mitternacht) als ISO-Zeitpunkt in UTC. */
export function startOfLocalDayIso(ymd: string): string {
  const [y, m, d] = ymd.split('-').map(Number);
  return new Date(y, m - 1, d, 0, 0, 0, 0).toISOString();
}

/** Heute als lokales `YYYY-MM-DD`. */
export function todayYmd(now = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`;
}

/** Lokales `YYYY-MM-DD` eines ISO-Zeitpunkts. */
export function ymdOf(iso: string): string { return todayYmd(new Date(iso)); }

/** Ganze verbleibende Tage bis `deadline` (aufgerundet, nie negativ). */
export function daysLeft(deadline: string, now = Date.now()): number {
  return Math.max(0, Math.ceil((new Date(deadline).getTime() - now) / 86_400_000));
}
