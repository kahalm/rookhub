import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Friend, FriendRequest, SentFriendRequest, UserSearchResult } from './models';

/**
 * Kapselt die Freundes-HTTP-Calls (`/api/friends/*`), damit die Komponenten nicht direkt den
 * `HttpClient` ansprechen (Service-Layer, Audit-Fund „14 Komponenten rufen HttpClient direkt").
 * Stats/Revenge sind generisch, weil ihre Response-Formen feature-lokal definiert sind (kein
 * Rückwärts-Import core → feature).
 */
@Injectable({ providedIn: 'root' })
export class FriendsService {
  private readonly apiUrl = '/api/friends';

  constructor(private http: HttpClient) {}

  search(query: string): Observable<UserSearchResult[]> {
    return this.http.get<UserSearchResult[]>(`${this.apiUrl}/search?q=${encodeURIComponent(query)}`);
  }

  getFriends(): Observable<Friend[]> {
    return this.http.get<Friend[]>(this.apiUrl);
  }

  getRequests(): Observable<FriendRequest[]> {
    return this.http.get<FriendRequest[]>(`${this.apiUrl}/requests`);
  }

  /** Von mir gesendete, noch nicht angenommene Anfragen (ausstehend). */
  getSentRequests(): Observable<SentFriendRequest[]> {
    return this.http.get<SentFriendRequest[]>(`${this.apiUrl}/requests/sent`);
  }

  sendRequest(userId: number): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/request/${userId}`, {});
  }

  accept(friendshipId: number): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/accept/${friendshipId}`, {});
  }

  decline(friendshipId: number): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/decline/${friendshipId}`, {});
  }

  remove(friendshipId: number): Observable<unknown> {
    return this.http.delete(`${this.apiUrl}/${friendshipId}`);
  }

  /** Puzzle-Vergleichsstatistik eines Freundes (Form feature-lokal → generisch). */
  getStats<T>(userId: number): Observable<T> {
    return this.http.get<T>(`${this.apiUrl}/${userId}/stats`);
  }

  /** „Revenge"-Liste eines Freundes (Form feature-lokal → generisch). */
  getRevenge<T>(userId: number): Observable<T> {
    return this.http.get<T>(`${this.apiUrl}/${userId}/revenge`);
  }
}
