import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type LeaderboardPeriod = 'daily' | 'weekly' | 'monthly' | 'alltime';

export interface LeaderboardEntry {
  name: string;
  discordId?: string | null;
  discordUsername?: string | null;
  count: number;
  /** Echte 1-basierte Platzierung (die Liste zeigt nur Top-N + eigenes Fenster, kann also Lücken haben). */
  rank: number;
  /** True für den eigenen Eintrag (Hervorhebung). */
  isMe: boolean;
}

export interface Leaderboards {
  period: string;
  puzzles: LeaderboardEntry[];
  endlessRuns: LeaderboardEntry[];
  courseLines: LeaderboardEntry[];
  dailyPuzzles: LeaderboardEntry[];
}

@Injectable({ providedIn: 'root' })
export class LeaderboardService {
  constructor(private http: HttpClient) {}

  /** Holt je Kategorie die besten `top` plus das Fenster ±`around` um den eigenen Platz. */
  get(period: LeaderboardPeriod, top = 5, around = 2): Observable<Leaderboards> {
    const params = new HttpParams().set('period', period).set('top', top).set('around', around);
    return this.http.get<Leaderboards>('/api/leaderboards', { params });
  }
}
