import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type LeaderboardPeriod = 'daily' | 'weekly' | 'monthly' | 'alltime';

export interface LeaderboardEntry {
  name: string;
  discordId?: string | null;
  discordUsername?: string | null;
  count: number;
}

export interface Leaderboards {
  period: string;
  puzzles: LeaderboardEntry[];
  endlessRuns: LeaderboardEntry[];
  courseLines: LeaderboardEntry[];
}

@Injectable({ providedIn: 'root' })
export class LeaderboardService {
  constructor(private http: HttpClient) {}

  get(period: LeaderboardPeriod, top = 100): Observable<Leaderboards> {
    const params = new HttpParams().set('period', period).set('top', top);
    return this.http.get<Leaderboards>('/api/leaderboards', { params });
  }
}
