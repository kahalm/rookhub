import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type GoalSource = 'none' | 'group' | 'personal';
export type GoalStatus = 'none' | 'partial' | 'full';

export interface TrainingGoal {
  puzzleMinutes: number;
  bookMinutes: number;
  /** Tagesziel Chessable-Training in Minuten (aktive Zeit von der RepCheck-Extension). */
  chessableMinutes: number;
  /** Wochenziel: Anzahl Rapid-/Classical-Partien pro ISO-Woche. */
  playGames: number;
  weeklyDaysTarget: number;
  source: GoalSource;
  groupName: string | null;
}

export interface TrainingGoalInput {
  puzzleMinutes: number;
  bookMinutes: number;
  chessableMinutes: number;
  playGames: number;
  weeklyDaysTarget: number;
}

export interface TrackerDay {
  date: string;
  puzzleSeconds: number;
  bookSeconds: number;
  /** Aktiv trainierte Chessable-Sekunden an diesem Tag. */
  chessableSeconds: number;
  /** Rapid-/Classical-Partien an diesem Tag (informativ; Tagesstatus nutzt nur Puzzles/Buch/Chessable). */
  playGames: number;
  status: GoalStatus;
}

export interface TrackerResponse {
  goal: TrainingGoal;
  days: TrackerDay[];
}

/** Zeitbasierte Tages-Kategorie (Puzzles/Buch). */
export interface CategoryProgress {
  targetMinutes: number;
  doneSeconds: number;
  met: boolean;
}

/** Wöchentliches Spielen-Ziel: Partien dieser Woche vs. Zielanzahl. */
export interface PlayProgress {
  targetGames: number;
  doneGames: number;
  met: boolean;
}

export interface TodayProgress {
  goal: TrainingGoal;
  puzzles: CategoryProgress;
  book: CategoryProgress;
  chessable: CategoryProgress;
  play: PlayProgress;
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

  /** Gespielte Rapid-/Classical-Partien (Lichess/chess.com) jetzt synchronisieren. */
  syncPlay(): Observable<{ synced: boolean }> {
    return this.http.post<{ synced: boolean }>('/api/training-goals/sync-play', {});
  }
}
