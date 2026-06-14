import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

export interface IncomingChallenge {
  id: number;
  fromUserId: number;
  fromUsername: string;
  fromDisplayName: string | null;
  puzzleId: number;
  rating: number;
  themes: string | null;
  createdAt: string;
}

export interface OutgoingChallenge {
  id: number;
  toUserId: number;
  toUsername: string;
  toDisplayName: string | null;
  puzzleId: number;
  rating: number;
  status: 'Pending' | 'Solved' | 'Failed';
  createdAt: string;
  resolvedAt: string | null;
  timeSpentSeconds: number | null;
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

  send(toUserId: number, puzzleId: number): Observable<{ id: number }> {
    return this.http.post<{ id: number }>('/api/challenges', { toUserId, puzzleId });
  }

  getIncoming(): Observable<IncomingChallenge[]> {
    return this.http.get<IncomingChallenge[]>('/api/challenges/incoming')
      .pipe(tap(list => this.incomingCount.next(list.length)));
  }

  getOutgoing(): Observable<OutgoingChallenge[]> {
    return this.http.get<OutgoingChallenge[]>('/api/challenges/outgoing');
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
