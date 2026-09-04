import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface RevengeNotification {
  id: number;
  avengerUserId: number;
  avengerUsername: string;
  avengerDisplayName: string | null;
  puzzleId: number;
  rating: number;
  solved: boolean;
  createdAt: string;
  seen: boolean;
}

/**
 * Revanche-Benachrichtigungen: wenn ein Freund eines meiner gescheiterten Puzzles angeht (Revenge),
 * werde ich informiert — gelöst oder nicht.
 *
 * KEIN eigener Badge-Zähler (siehe ChallengeService): das frühere Subject wurde gepflegt, aber von
 * niemandem gelesen — und hätte beim Nutzerwechsel fremde Zahlen behalten.
 */
@Injectable({ providedIn: 'root' })
export class RevengeService {
  constructor(private http: HttpClient) {}

  /** Ergebnis einer Revanche melden (fire-and-forget vom Puzzle-Solver). */
  recordResult(targetUserId: number, puzzleId: number, solved: boolean): Observable<unknown> {
    return this.http.post('/api/revenge/result', { targetUserId, puzzleId, solved });
  }

  getNotifications(): Observable<RevengeNotification[]> {
    return this.http.get<RevengeNotification[]>('/api/revenge/notifications');
  }

  markSeen(): Observable<unknown> {
    return this.http.post('/api/revenge/notifications/seen', {});
  }
}
