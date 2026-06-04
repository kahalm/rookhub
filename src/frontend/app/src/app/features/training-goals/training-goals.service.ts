import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type GoalSource = 'none' | 'group' | 'personal';
export type GoalStatus = 'none' | 'partial' | 'full';

export interface TrainingGoal {
  puzzleMinutes: number;
  bookMinutes: number;
  playMinutes: number;
  weeklyDaysTarget: number;
  source: GoalSource;
  groupName: string | null;
}

export interface TrainingGoalInput {
  puzzleMinutes: number;
  bookMinutes: number;
  playMinutes: number;
  weeklyDaysTarget: number;
}

export interface TrackerDay {
  date: string;
  puzzleSeconds: number;
  bookSeconds: number;
  playSeconds: number;
  status: GoalStatus;
}

export interface TrackerResponse {
  goal: TrainingGoal;
  days: TrackerDay[];
}

export interface CategoryProgress {
  targetMinutes: number;
  doneSeconds: number;
  met: boolean;
}

export interface TodayProgress {
  goal: TrainingGoal;
  puzzles: CategoryProgress;
  book: CategoryProgress;
  play: CategoryProgress;
  status: GoalStatus;
  weekDaysMet: number;
  weeklyDaysTarget: number;
}

@Injectable({ providedIn: 'root' })
export class TrainingGoalService {
  constructor(private http: HttpClient) {}

  /** Effektives Ziel des Users (persönlich > Gruppen-Vorlage > keins). */
  getGoal(): Observable<TrainingGoal> {
    return this.http.get<TrainingGoal>('/api/training-goals');
  }

  /** Persönlichen Override setzen/aktualisieren; gibt das neue effektive Ziel zurück. */
  saveGoal(input: TrainingGoalInput): Observable<TrainingGoal> {
    return this.http.put<TrainingGoal>('/api/training-goals', input);
  }

  /** Persönlichen Override entfernen → Rückfall auf die Gruppen-Vorlage (falls vorhanden). */
  deleteOverride(): Observable<TrainingGoal> {
    return this.http.delete<TrainingGoal>('/api/training-goals');
  }

  /** Heutiger Fortschritt je Kategorie + Wochenstand. */
  getToday(): Observable<TodayProgress> {
    return this.http.get<TodayProgress>('/api/training-goals/today');
  }

  /** Tagesreihe der letzten `weeks` Wochen für die Tracker-Heatmap. */
  getTracker(weeks = 27): Observable<TrackerResponse> {
    return this.http.get<TrackerResponse>('/api/training-goals/tracker', { params: new HttpParams().set('weeks', weeks) });
  }

  /** Externe Spielzeit (Lichess/chess.com) jetzt synchronisieren (Phase C). */
  syncPlay(): Observable<{ synced: boolean }> {
    return this.http.post<{ synced: boolean }>('/api/training-goals/sync-play', {});
  }
}
