import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, switchMap } from 'rxjs';
import { Tournament, Subscription, CrawlJob } from '@rh/core/models';

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

  /**
   * EIN schon geholtes Turnier. Die Crawler-Route loest sowohl die interne Nummer als auch die
   * chess-results-Nummer auf — von der Verzeichnis-Detailseite kommt letztere. Nicht geholt = 404,
   * das ist hier der Normalfall und kein Fehler.
   */
  getTournament(id: string | number): Observable<Tournament> {
    return this.http.get<Tournament>(`/api/tournaments/${id}`);
  }

  /**
   * „Merken" heisst auch „holen".
   *
   * <p>Ein Abo allein legt nur einen Vermerk an: Teilnehmer, Paarungen und Tabelle kommen erst,
   * wenn der Abo-Refresh das Turnier zum Spielbeginn von selbst holt — bei einem Turnier in drei
   * Monaten also in drei Monaten. Wer etwas merkt, will es aber ANSEHEN koennen. Deshalb wird der
   * Crawl-Auftrag gleich mit eingereiht.</p>
   *
   * <p>Liess er sich nicht einreihen, bleibt das Abo trotzdem stehen (`job: null`) — gemerkt ist
   * gemerkt, und der naechste Refresh holt es ohnehin.</p>
   */
  bookmarkAndImport(chessResultsId: string, tournamentName: string):
      Observable<{ subscription: Subscription; job: CrawlJob | null }> {
    return this.subscribe(chessResultsId, tournamentName).pipe(
      switchMap(subscription => this.startCrawl(chessResultsId).pipe(
        map(job => ({ subscription, job: job as CrawlJob | null })),
        catchError(() => of({ subscription, job: null })),
      )),
    );
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

  /**
   * Abo loesen, ohne die Abo-Id zu kennen — die Kurzansicht kennt nur das Turnier. Idempotent:
   * „war schon nicht gemerkt" ist kein Fehler, der Knopf ist ein Umschalter.
   */
  unsubscribeByTournament(crawlerTournamentId: string): Observable<unknown> {
    return this.http.delete(`/api/subscriptions/by-tournament/${crawlerTournamentId}`);
  }

  startCrawl(chessResultsId: string): Observable<CrawlJob> {
    return this.http.post<CrawlJob>('/api/tournaments/crawl', { chessResultsId, jobType: 'Full' });
  }

  getCrawlJob(jobId: number): Observable<CrawlJob> {
    return this.http.get<CrawlJob>(`/api/tournaments/crawl/${jobId}`);
  }
}
