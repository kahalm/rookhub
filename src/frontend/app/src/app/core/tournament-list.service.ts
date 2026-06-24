import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Tournament, Subscription, CrawlJob } from './models';

/**
 * Kapselt die HTTP-Calls der Turnierliste (Liste/Abos/Crawl-Job), damit `tournament-list.component`
 * nicht direkt den `HttpClient` anspricht (Service-Layer, Audit-Fund „14 Komponenten rufen HttpClient direkt").
 */
@Injectable({ providedIn: 'root' })
export class TournamentListService {
  constructor(private http: HttpClient) {}

  getTournaments(pageSize = 200): Observable<{ items: Tournament[]; totalCount: number }> {
    return this.http.get<{ items: Tournament[]; totalCount: number }>(`/api/tournaments?pageSize=${pageSize}`);
  }

  getSubscriptions(): Observable<Subscription[]> {
    return this.http.get<Subscription[]>('/api/subscriptions');
  }

  subscribe(crawlerTournamentId: string, tournamentName: string): Observable<Subscription> {
    return this.http.post<Subscription>('/api/subscriptions', { crawlerTournamentId, tournamentName });
  }

  unsubscribe(subscriptionId: number): Observable<unknown> {
    return this.http.delete(`/api/subscriptions/${subscriptionId}`);
  }

  startCrawl(chessResultsId: string): Observable<CrawlJob> {
    return this.http.post<CrawlJob>('/api/tournaments/crawl', { chessResultsId, jobType: 'Full' });
  }

  getCrawlJob(jobId: number): Observable<CrawlJob> {
    return this.http.get<CrawlJob>(`/api/tournaments/crawl/${jobId}`);
  }
}
