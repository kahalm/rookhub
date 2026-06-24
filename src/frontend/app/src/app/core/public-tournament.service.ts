import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Tournament, TournamentPlayer, TournamentTeam } from './models';

/**
 * Lese-Calls der öffentlichen Turnier-Detailseite (`/api/tournaments/{id}/...`), gekapselt als
 * Service statt direkter `HttpClient`-Nutzung in der Komponente (Service-Layer, Audit-Fund).
 */
@Injectable({ providedIn: 'root' })
export class PublicTournamentService {
  private readonly apiUrl = '/api/tournaments';

  constructor(private http: HttpClient) {}

  getTournament(id: number | string): Observable<Tournament> {
    return this.http.get<Tournament>(`${this.apiUrl}/${id}`);
  }

  getPlayers(id: number | string): Observable<TournamentPlayer[]> {
    return this.http.get<TournamentPlayer[]>(`${this.apiUrl}/${id}/players`);
  }

  getTeams(id: number | string): Observable<TournamentTeam[]> {
    return this.http.get<TournamentTeam[]>(`${this.apiUrl}/${id}/teams`);
  }

  getTeam(id: number | string, snr: number): Observable<TournamentTeam> {
    return this.http.get<TournamentTeam>(`${this.apiUrl}/${id}/teams/${snr}`);
  }

  getPairings<T>(id: number | string, round: number): Observable<T> {
    return this.http.get<T>(`${this.apiUrl}/${id}/pairings?round=${round}`);
  }
}
