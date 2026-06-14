import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Tournament, TournamentPlayer, TournamentTeam, Subscription, TournamentFavorite, TournamentMonitorStatus, CrawlJob } from '../../core/models';

/**
 * Kapselt alle HTTP-Endpunkte rund um die Turnier-Detailansicht
 * (Turnierdaten, Abo, Runden-Monitor, Refresh-Crawl, Favoriten).
 * Hält URL-Strings + Request-Shapes aus der Komponente heraus und ist isoliert testbar.
 */
@Injectable({ providedIn: 'root' })
export class TournamentDetailService {
  constructor(private http: HttpClient) {}

  // --- Turnierdaten ---
  getTournament(id: string): Observable<Tournament> {
    return this.http.get<Tournament>(`/api/tournaments/${id}`);
  }
  getPlayers(id: string): Observable<TournamentPlayer[]> {
    return this.http.get<TournamentPlayer[]>(`/api/tournaments/${id}/players`);
  }
  getTeams(id: string): Observable<TournamentTeam[]> {
    return this.http.get<TournamentTeam[]>(`/api/tournaments/${id}/teams`);
  }
  // Response kann TeamPairingResponse[] oder TournamentPairing[] sein (vom Turniertyp abhängig)
  getPairings(id: string, round: number): Observable<any[]> {
    return this.http.get<any[]>(`/api/tournaments/${id}/pairings?round=${round}`);
  }
  getTeamDetails(id: string, teamSnr: number): Observable<TournamentTeam> {
    return this.http.get<TournamentTeam>(`/api/tournaments/${id}/teams/${teamSnr}`);
  }

  // --- Abo ---
  getSubscriptions(): Observable<Subscription[]> {
    return this.http.get<Subscription[]>('/api/subscriptions');
  }
  subscribe(id: string, tournamentName: string): Observable<Subscription> {
    return this.http.post<Subscription>('/api/subscriptions', { crawlerTournamentId: id, tournamentName });
  }
  unsubscribe(subscriptionId: number): Observable<unknown> {
    return this.http.delete(`/api/subscriptions/${subscriptionId}`);
  }

  // --- Runden-Monitor ---
  getMonitor(id: string): Observable<TournamentMonitorStatus> {
    return this.http.get<TournamentMonitorStatus>(`/api/tournament-monitors/${id}`);
  }
  startMonitor(id: string): Observable<TournamentMonitorStatus> {
    return this.http.post<TournamentMonitorStatus>(`/api/tournament-monitors/${id}`, {});
  }
  stopMonitor(id: string): Observable<unknown> {
    return this.http.delete(`/api/tournament-monitors/${id}`);
  }

  // --- Refresh (Crawl-Job) ---
  startCrawl(chessResultsId: string): Observable<CrawlJob> {
    return this.http.post<CrawlJob>('/api/tournaments/crawl', { chessResultsId, jobType: 'Full' });
  }
  getCrawlJob(jobId: number): Observable<CrawlJob> {
    return this.http.get<CrawlJob>(`/api/tournaments/crawl/${jobId}`);
  }

  // --- Favoriten (serverseitig) ---
  getFavorites(id: string): Observable<TournamentFavorite[]> {
    return this.http.get<TournamentFavorite[]>(`/api/tournament-favorites?tournamentId=${id}`);
  }
  getFavoriteSettings(id: string): Observable<{ showFavoritesOnly: boolean }> {
    return this.http.get<{ showFavoritesOnly: boolean }>(`/api/tournament-favorites/settings/${id}`);
  }
  saveFavoriteSettings(id: string, showFavoritesOnly: boolean): Observable<unknown> {
    return this.http.put(`/api/tournament-favorites/settings/${id}`, { showFavoritesOnly });
  }
  addPlayerFavorite(id: string, playerSnr: number): Observable<TournamentFavorite> {
    return this.http.post<TournamentFavorite>('/api/tournament-favorites', { crawlerTournamentId: id, playerSnr });
  }
  removePlayerFavorite(id: string, playerSnr: number): Observable<unknown> {
    return this.http.delete(`/api/tournament-favorites/by-player/${id}/${playerSnr}`);
  }
  addTeamFavorite(id: string, teamSnr: number): Observable<TournamentFavorite> {
    return this.http.post<TournamentFavorite>('/api/tournament-favorites/team', { crawlerTournamentId: id, teamSnr });
  }
  removeTeamFavorite(id: string, teamSnr: number): Observable<unknown> {
    return this.http.delete(`/api/tournament-favorites/by-team/${id}/${teamSnr}`);
  }
}
