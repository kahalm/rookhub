import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

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
 * werde ich informiert — gelöst oder nicht. Hält den Zähler ungelesener Benachrichtigungen reaktiv
 * fürs Navbar-Badge.
 */
@Injectable({ providedIn: 'root' })
export class RevengeService {
  private readonly unseen = new BehaviorSubject<number>(0);
  readonly unseenCount$ = this.unseen.asObservable();

  constructor(private http: HttpClient) {}

  /** Ergebnis einer Revanche melden (fire-and-forget vom Puzzle-Solver). */
  recordResult(targetUserId: number, puzzleId: number, solved: boolean): Observable<unknown> {
    return this.http.post('/api/revenge/result', { targetUserId, puzzleId, solved });
  }

  getNotifications(): Observable<RevengeNotification[]> {
    return this.http.get<RevengeNotification[]>('/api/revenge/notifications');
  }

  markSeen(): Observable<unknown> {
    return this.http.post('/api/revenge/notifications/seen', {}).pipe(tap(() => this.unseen.next(0)));
  }

  refreshCount(): void {
    this.http.get<{ count: number }>('/api/revenge/notifications/count').subscribe({
      next: r => this.unseen.next(r.count),
      error: () => {}
    });
  }
}
