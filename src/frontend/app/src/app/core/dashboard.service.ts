import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Subscription, Repertoire, Friend, PuzzleStatsDto } from './models';

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

  /** Sichtbare Kurse (Bücher) des Users — nur für den Zähler auf der Dashboard-Kachel. */
  getCourses(): Observable<unknown[]> {
    return this.http.get<unknown[]>('/api/courses');
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
