import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

/** Quelle des Puzzles einer Challenge — bestimmt Tabelle (Backend) + Deep-Link beim Empfänger. */
export type PuzzleChallengeSource = 'standard' | 'book';

export interface IncomingChallenge {
  id: number;
  fromUserId: number;
  fromUsername: string;
  fromDisplayName: string | null;
  puzzleId: number;
  source: 'Standard' | 'Book';
  rating: number;
  themes: string | null;
  title: string | null;
  createdAt: string;
}

export interface OutgoingChallenge {
  id: number;
  toUserId: number;
  toUsername: string;
  toDisplayName: string | null;
  puzzleId: number;
  source: 'Standard' | 'Book';
  rating: number;
  title: string | null;
  status: 'Pending' | 'Solved' | 'Failed';
  createdAt: string;
  resolvedAt: string | null;
  timeSpentSeconds: number | null;
}

/** Ergebnis eines Batch-Versands. */
export interface ChallengeBatchResult {
  sent: number;
  skipped: { toUserId: number; reason: string }[];
}

/**
 * Puzzle-Challenges zwischen Freunden („schick dieses Puzzle an XY"). Hält den Zähler offener
 * eingehender Challenges reaktiv für das Navbar-Badge.
 */
@Injectable({ providedIn: 'root' })
export class ChallengeService {
  private readonly incomingCount = new BehaviorSubject<number>(0);
  /** Anzahl offener eingehender Challenges (Navbar-Badge). */
  readonly incomingCount$ = this.incomingCount.asObservable();

  constructor(private http: HttpClient) {}

  /** Schickt ein Puzzle als Challenge an einen oder mehrere Freunde auf einmal. */
  sendMany(toUserIds: number[], puzzleId: number, source: PuzzleChallengeSource = 'standard'): Observable<ChallengeBatchResult> {
    return this.http.post<ChallengeBatchResult>('/api/challenges', { toUserIds, puzzleId, source });
  }

  getIncoming(): Observable<IncomingChallenge[]> {
    return this.http.get<IncomingChallenge[]>('/api/challenges/incoming')
      .pipe(tap(list => this.incomingCount.next(list.length)));
  }

  getOutgoing(): Observable<OutgoingChallenge[]> {
    return this.http.get<OutgoingChallenge[]>('/api/challenges/outgoing');
  }

  /** Pro Freund (Map userId → Anzahl) die von mir geschickten, noch OFFENEN Challenges —
   *  für die „Freund (n)"-Anzeige im „An Freund schicken"-Menü. Nur Freunde mit n > 0 enthalten. */
  getPendingCounts(): Observable<Record<number, number>> {
    return this.http.get<Record<number, number>>('/api/challenges/outgoing/pending-counts');
  }

  resolve(id: number, solved: boolean, timeSpentSeconds: number): Observable<unknown> {
    return this.http.post(`/api/challenges/${id}/resolve`, { solved, timeSpentSeconds });
  }

  /** Lädt den Badge-Zähler neu (leise; Fehler werden ignoriert). */
  refreshCount(): void {
    this.http.get<{ count: number }>('/api/challenges/incoming/count').subscribe({
      next: r => this.incomingCount.next(r.count),
      error: () => {}
    });
  }
}
