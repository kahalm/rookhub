import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { HistoryFriend, PlayerHistory, TrackPlayerRequest, TrackedPlayer } from './tournament-history.model';

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

  // ----- Verfolgte Spieler -------------------------------------------------

  /** Die eigene Liste verfolgter Spieler — die Reiter neben den Freunden. */
  tracked(): Observable<TrackedPlayer[]> {
    return this.http.get<TrackedPlayer[]>('/api/tournament-history/tracked');
  }

  /**
   * Einen Spieler verfolgen. Denselben zweimal zu schicken ist kein Fehler — der Server gibt den
   * vorhandenen Eintrag zurueck (die Kennung entscheidet, nicht die Schreibweise des Namens).
   */
  track(request: TrackPlayerRequest): Observable<TrackedPlayer> {
    return this.http.post<TrackedPlayer>('/api/tournament-history/tracked', request);
  }

  untrack(id: number): Observable<void> {
    return this.http.delete<void>(`/api/tournament-history/tracked/${id}`);
  }

  /**
   * Der Verlauf EINES verfolgten Spielers. Eigener Weg statt `get([id])`: dort ist die Zahl ein
   * KONTO, hier ein Eintrag der eigenen Liste — dieselbe Zahl haette zwei Bedeutungen.
   */
  trackedHistory(id: number): Observable<PlayerHistory> {
    return this.http.get<PlayerHistory>(`/api/tournament-history/tracked/${id}`);
  }
}
