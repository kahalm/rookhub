import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { HistoryFriend, PlayerHistory } from './tournament-history.model';

/** HTTP-Zugriff auf den Turnierverlauf. */
@Injectable({ providedIn: 'root' })
export class TournamentHistoryService {
  private readonly http = inject(HttpClient);

  /**
   * Der Verlauf mehrerer Konten in EINEM Aufruf. Mehrere, weil die Ansicht auf „alle Freunde"
   * umschaltbar ist und das sonst N Anfragen waere — von denen jede eine Trefferliste holt.
   */
  get(userIds: number[]): Observable<PlayerHistory[]> {
    const params = userIds.length > 0
      ? new HttpParams().set('userIds', userIds.join(','))
      : new HttpParams();
    return this.http.get<PlayerHistory[]>('/api/tournament-history', { params });
  }

  friends(): Observable<HistoryFriend[]> {
    return this.http.get<HistoryFriend[]>('/api/tournament-history/friends');
  }
}
