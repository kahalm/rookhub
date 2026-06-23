import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type GoalSource = 'none' | 'group' | 'personal';
export type GoalStatus = 'none' | 'partial' | 'full';

export interface TrainingGoal {
  /** Tagesziel Trainingszeit in Minuten — gemeinsamer Topf aller Quellen. */
  dailyMinutes: number;
  /** Wochenziel: Anzahl Rapid-/Classical-Partien pro ISO-Woche. */
  playGames: number;
  weeklyDaysTarget: number;
  source: GoalSource;
  groupName: string | null;
}

export interface TrainingGoalInput {
  dailyMinutes: number;
  playGames: number;
  weeklyDaysTarget: number;
}

/** Aufschlüsselung von Trainingssekunden nach Quelle. */
export interface SourceBreakdown {
  randomPuzzleSeconds: number;
  courseBookSeconds: number;
  chessableSeconds: number;
}

/** Aufschlüsselung von Trainingssekunden nach Thema (Rest → otherSeconds). */
export interface ThemeBreakdown {
  openingSeconds: number;
  middlegameSeconds: number;
  endgameSeconds: number;
  tacticsSeconds: number;
  otherSeconds: number;
}

/** Schlüssel der Quellen-Aufschlüsselung (für Template-Iteration mit i18n). */
export const SOURCE_KEYS: { key: keyof SourceBreakdown; label: string }[] = [
  { key: 'randomPuzzleSeconds', label: 'randomPuzzle' },
  { key: 'courseBookSeconds', label: 'courseBook' },
  { key: 'chessableSeconds', label: 'chessable' },
];

/** Schlüssel der Themen-Aufschlüsselung (für Template-Iteration mit i18n). */
export const THEME_KEYS: { key: keyof ThemeBreakdown; label: string }[] = [
  { key: 'openingSeconds', label: 'opening' },
  { key: 'middlegameSeconds', label: 'middlegame' },
  { key: 'endgameSeconds', label: 'endgame' },
  { key: 'tacticsSeconds', label: 'tactics' },
  { key: 'otherSeconds', label: 'other' },
];

export interface TrackerDay {
  date: string;
  /** Gesamte (gemeinsam getopfte) Trainingssekunden des Tages. */
  totalSeconds: number;
  bySource: SourceBreakdown;
  byTheme: ThemeBreakdown;
  /** Rapid-/Classical-Partien an diesem Tag (informativ; Tagesstatus nutzt nur die Trainingszeit). */
  playGames: number;
  status: GoalStatus;
  /** Enthält dieser Tag mindestens eine manuell (selbst) eingetragene Offline-Aktivität? */
  hasManual: boolean;
}

/** Art einer manuell eingetragenen Offline-Aktivität (mappt je auf eine bestehende Kategorie). */
export type ManualActivityKind = 'OtbGame' | 'OfflinePuzzle' | 'OfflineStudy' | 'Coaching';

export interface ManualActivity {
  id: number;
  /** yyyy-MM-dd. */
  date: string;
  kind: ManualActivityKind;
  /** Bei OtbGame = Anzahl Partien; sonst = Minuten. */
  amount: number;
  note: string | null;
}

export interface ManualActivityInput {
  date: string;
  kind: ManualActivityKind;
  amount: number;
  note?: string | null;
}

/** Manuell zuweisbares Thema eines Chessable-Kurses (entspricht der Themen-Aufschlüsselung ohne „other"). */
export type ChessableTheme = 'Opening' | 'Middlegame' | 'Endgame' | 'Tactics';

/** Ein in der Chessable-History gruppierter Kurs inkl. ermitteltem Thema. */
export interface ChessableCourseSummary {
  courseId: string;
  courseName: string | null;
  totalSeconds: number;
  totalMoves: number;
  activityCount: number;
  lastActivityAt: string;
  /** Manuell zugeordnetes Thema (lowercase) oder null. */
  assignedTheme: string | null;
  /** Automatisch aus Repertoire abgeleitetes Thema (lowercase) oder null. */
  autoTheme: string | null;
  /** true, wenn ein Thema feststeht (manuell ODER automatisch). */
  isAssigned: boolean;
}

export interface TrackerResponse {
  goal: TrainingGoal;
  days: TrackerDay[];
  /** Summe über das Fenster, nach Quelle. */
  breakdownBySource: SourceBreakdown;
  /** Summe über das Fenster, nach Thema. */
  breakdownByTheme: ThemeBreakdown;
}

/** Fortschritt der zeitbasierten Tages-Trainingszeit (gemeinsamer Topf). */
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
  /** Tageszeit-Ziel: heute trainierte Zeit (alle Quellen) vs. Zielminuten. */
  daily: CategoryProgress;
  bySource: SourceBreakdown;
  byTheme: ThemeBreakdown;
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

  /** Eigene manuell eingetragene Offline-Aktivitäten (neueste zuerst). */
  listManual(): Observable<ManualActivity[]> {
    return this.http.get<ManualActivity[]>('/api/training-goals/manual');
  }

  /** Manuelle Offline-Aktivität anlegen. */
  addManual(input: ManualActivityInput): Observable<ManualActivity> {
    return this.http.post<ManualActivity>('/api/training-goals/manual', input);
  }

  /** Eigene manuelle Aktivität ändern. */
  updateManual(id: number, input: ManualActivityInput): Observable<ManualActivity> {
    return this.http.put<ManualActivity>(`/api/training-goals/manual/${id}`, input);
  }

  /** Eigene manuelle Aktivität löschen. */
  deleteManual(id: number): Observable<void> {
    return this.http.delete<void>(`/api/training-goals/manual/${id}`);
  }

  /** Chessable-Kurs-History (nach Kurs gruppiert); `unassignedOnly` filtert auf Kurse ohne Thema. */
  listChessableCourses(unassignedOnly = false): Observable<ChessableCourseSummary[]> {
    return this.http.get<ChessableCourseSummary[]>('/api/training-goals/chessable-courses',
      { params: new HttpParams().set('unassignedOnly', unassignedOnly) });
  }

  /** Einem Chessable-Kurs manuell ein Thema zuordnen. */
  setChessableCourseTheme(courseId: string, theme: ChessableTheme): Observable<void> {
    return this.http.put<void>(`/api/training-goals/chessable-courses/${encodeURIComponent(courseId)}`, { theme });
  }

  /** Manuelle Themen-Zuordnung eines Chessable-Kurses entfernen. */
  clearChessableCourseTheme(courseId: string): Observable<void> {
    return this.http.delete<void>(`/api/training-goals/chessable-courses/${encodeURIComponent(courseId)}`);
  }
}
