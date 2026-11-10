import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BookPuzzleDto } from '../puzzles/puzzle.service';

/** `solving` = Zyklus läuft, `break` = Pause (nächster Zyklus ab `nextCycleAt`), `ready` = nächster Zyklus kann
 *  beginnen, `completed` = Methode abgeschlossen, `archived` = im Archiv, nur lesbar (0.958.0; geht allem vor). */
export type WoodpeckerStatus = 'solving' | 'break' | 'ready' | 'completed' | 'archived';

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
  /** Versuche mit mindestens einem Tipp (0.956.0; ältere Server ohne Feld). */
  hinted?: number;
  /** Davon gelöst — zählen in `solved` mit („sauber gelöst“ = solved − solvedWithHint). */
  solvedWithHint?: number;
}

export interface WoodpeckerPlan {
  id: number;
  name: string;
  createdAt: string;
  completedAt: string | null;
  /** Archiviert am (0.958.0); fehlt/`null` = aktiv. Je Nutzer höchstens EIN nicht archiviertes Training. */
  archivedAt?: string | null;
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

/** Laufender Zyklus gegen einen früheren auf DENSELBEN Aufgaben (Schnittmenge über die Aufgabe, 0.957.0). */
export interface WoodpeckerOverlap {
  cycle: number;
  count: number;
  secondsNow: number;
  secondsThen: number;
  solvedNow: number;
  solvedThen: number;
  hintedNow: number;
  hintedThen: number;
  /** Prozentpunkte (0–100). */
  ratePctNow: number | null;
  ratePctThen: number | null;
  /** Anteil der damaligen Zeit (0,5 = halbe Zeit). */
  timeRatio: number | null;
  /** damals / jetzt (2,0 = doppelt so schnell). */
  speedFactor: number | null;
}

export interface WoodpeckerStatsCycle {
  number: number;
  targetDays: number;
  startedAt: string;
  finishedAt: string | null;
  attempted: number;
  solved: number;
  solvedClean: number;
  hinted: number;
  timeSeconds: number;
  ratePct: number | null;
  avgSeconds: number | null;
  daysUsed: number;
  /** Versuche ab dem Tipp-Stichtag — nur deren Tipp-Zahl ist belastbar. */
  hintsTrackedCount: number;
  sameAsNow: WoodpeckerOverlap | null;
}

export interface WoodpeckerTimeBuckets { fast: number; medium: number; slow: number; }

export interface WoodpeckerChanges {
  cycle: number;
  improved: number;
  worsened: number;
  bothSolved: number;
  bothMissed: number;
  faster: number;
  slower: number;
  sameTime: number;
  muchFaster: number;
  muchSlower: number;
  savedSeconds: number;
  lostSeconds: number;
  bucketsNow: WoodpeckerTimeBuckets;
  bucketsThen: WoodpeckerTimeBuckets;
}

export interface WoodpeckerForecast {
  basisCycle: number;
  sample: number;
  timeRatio: number;
  basisTotalSeconds: number;
  spentSeconds: number;
  openCount: number;
  remainingSeconds: number;
  projectedTotalSeconds: number;
  goalRatio: number;
  goalReached: boolean;
}

export interface WoodpeckerHintBreakdown {
  tracked: number;
  untracked: number;
  solvedNoHint: number;
  solvedHint1: number;
  solvedHint2: number;
  solvedHint3: number;
  missedNoHint: number;
  missedWithHint: number;
}

export interface WoodpeckerHintChanges { cycle: number; count: number; more: number; fewer: number; same: number; sumNow: number; sumThen: number; }

export interface WoodpeckerStatsDay { date: string; attempted: number; solved: number; timeSeconds: number; }

export interface WoodpeckerTrouble {
  bookPuzzleId: number;
  bookId: number;
  label: string;
  secondsNow: number | null;
  secondsPrev: number | null;
  solvedNow: boolean | null;
  cycles: number;
}

/** `GET /api/woodpecker/{id}/stats` — ausführliche Statistik (Formeln: docs/api/woodpecker.md). */
export interface WoodpeckerStats {
  planId: number;
  currentCycle: number;
  setSize: number;
  cycles: WoodpeckerStatsCycle[];
  changes: WoodpeckerChanges | null;
  forecast: WoodpeckerForecast | null;
  days: WoodpeckerStatsDay[];
  hintsTrackedSince: string;
  hints: WoodpeckerHintBreakdown;
  hintChanges: WoodpeckerHintChanges | null;
  timeBuckets: WoodpeckerTimeBuckets;
  slowest: WoodpeckerTrouble[];
  alwaysMissedCount: number;
  alwaysMissed: WoodpeckerTrouble[];
}

/** Woodpecker-Training über Kurse (`/api/woodpecker`, Regeln fest im Backend: `WoodpeckerRules`). */
@Injectable({ providedIn: 'root' })
export class WoodpeckerService {
  private http = inject(HttpClient);
  private readonly base = '/api/woodpecker';

  list(): Observable<WoodpeckerPlan[]> { return this.http.get<WoodpeckerPlan[]>(this.base); }

  get(planId: number): Observable<WoodpeckerPlan> { return this.http.get<WoodpeckerPlan>(`${this.base}/${planId}`); }

  /** `startedAt` (ISO, UTC) leer = jetzt. `archiveCurrent`: das bisherige aktive Training dabei archivieren — ohne
   *  das antwortet der Server mit 409, solange eins existiert. */
  create(bookIds: number[], startedAt?: string, archiveCurrent = false): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(this.base, { bookIds, startedAt: startedAt ?? null, archiveCurrent });
  }

  /** Ins Archiv legen (jederzeit; bleibt lesbar). */
  archive(planId: number): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(`${this.base}/${planId}/archive`, {});
  }

  /** Aus dem Archiv holen — nur ohne anderes aktives Training (sonst 409). */
  unarchive(planId: number): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(`${this.base}/${planId}/unarchive`, {});
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

  /** `lang`: Kommentare in dieser Sprache, wo der Kurs eine Übersetzung hat (wie `GET /api/courses/{id}/next`). */
  next(planId: number, lang?: string | null): Observable<WoodpeckerNext> {
    const params: Record<string, string> = lang ? { lang } : {};
    return this.http.get<WoodpeckerNext>(`${this.base}/${planId}/next`, { params });
  }

  /** Ausführliche Statistik; Tageszeilen in der Zeitzone dieses Geräts. */
  stats(planId: number, utcOffsetMinutes = -new Date().getTimezoneOffset()): Observable<WoodpeckerStats> {
    return this.http.get<WoodpeckerStats>(`${this.base}/${planId}/stats`, { params: { utcOffsetMinutes: String(utcOffsetMinutes) } });
  }

  /** `hintsUsed` = höchste aufgedeckte Tipp-Stufe (0–3), der Server klemmt. */
  recordAttempt(planId: number, bookPuzzleId: number, solved: boolean, timeSeconds: number, hintsUsed = 0): Observable<WoodpeckerPlan> {
    return this.http.post<WoodpeckerPlan>(`${this.base}/${planId}/attempts`, { bookPuzzleId, solved, timeSeconds, hintsUsed });
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

/**
 * Das laufende Training fürs Dashboard: unter den nicht abgeschlossenen und nicht archivierten (seit 0.958.0 gibt es
 * höchstens eins; Altbestand kann mehrere haben) das zuletzt bewegte — der jüngste Zeitpunkt aus
 * Anlage, Beginn und Ende des aktuellen Zyklus (ein Versuchszeitpunkt steht nicht im DTO). Gleichstand: Reihenfolge der
 * API (neueste zuerst). Keins → `null`.
 */
export function activeWoodpeckerPlan(plans: readonly WoodpeckerPlan[]): WoodpeckerPlan | null {
  let best: WoodpeckerPlan | null = null;
  let bestAt = -Infinity;
  for (const p of plans) {
    if (p.status === 'completed' || isArchivedWoodpeckerPlan(p)) continue;
    const c = p.cycles[p.cycles.length - 1];
    const at = Math.max(...[p.createdAt, c?.startedAt, c?.finishedAt]
      .filter((x): x is string => !!x).map(x => new Date(x).getTime()));
    if (at > bestAt) { best = p; bestAt = at; }
  }
  return best;
}

/** Im Archiv? (`archivedAt` gesetzt oder Status `archived`.) */
export function isArchivedWoodpeckerPlan(p: WoodpeckerPlan): boolean {
  return p.status === 'archived' || !!p.archivedAt;
}

/** Ganze verbleibende Tage bis `deadline` (aufgerundet, nie negativ). */
export function daysLeft(deadline: string, now = Date.now()): number {
  return Math.max(0, Math.ceil((new Date(deadline).getTime() - now) / 86_400_000));
}
