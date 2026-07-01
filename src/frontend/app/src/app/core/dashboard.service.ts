import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Subscription, Repertoire, Friend, PuzzleStatsDto } from './models';

/** Minimal-Sicht auf einen Kurs fürs Dashboard (Zähler + angepinnte Kurse mit Schnellstart). */
export interface DashboardCourse {
  bookId: number;
  displayName: string;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
  isPinned: boolean;
}

/**
 * Kapselt die Lese-Calls des Dashboards (Repertoires/Abos/Freunde/Puzzle-Stats), damit
 * `dashboard.component` nicht direkt den `HttpClient` anspricht (Service-Layer, Audit-Fund).
 * Die Fehlerbehandlung (catchError → leere Defaults) bleibt in der Komponente, damit das
 * forkJoin-Verhalten unverändert ist.
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  constructor(private http: HttpClient) {}

  getRepertoires(): Observable<Repertoire[]> {
    return this.http.get<Repertoire[]>('/api/repertoires');
  }

  /** Sichtbare Kurse (Bücher) des Users — für den Zähler + die angepinnten Kurse (Schnellstart). */
  getCourses(): Observable<DashboardCourse[]> {
    return this.http.get<DashboardCourse[]>('/api/courses');
  }

  getSubscriptions(): Observable<Subscription[]> {
    return this.http.get<Subscription[]>('/api/subscriptions');
  }

  getFriends(): Observable<Friend[]> {
    return this.http.get<Friend[]>('/api/friends');
  }

  getPuzzleStats(): Observable<PuzzleStatsDto> {
    return this.http.get<PuzzleStatsDto>('/api/puzzles/stats');
  }
}
