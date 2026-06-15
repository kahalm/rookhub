import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

/** Eine Nachricht im Admin↔User-Thread. */
export interface ChatMessage {
  id: number;
  /** true = vom Admin-Team an den User; false = Antwort des Users. */
  fromAdmin: boolean;
  body: string;
  createdAt: string;
  /** Lesebestätigung der jeweils anderen Seite. */
  readByRecipient: boolean;
}

/** Admin-Übersicht: ein Thread je User. */
export interface AdminThreadSummary {
  userId: number;
  username: string;
  lastMessagePreview: string;
  lastMessageAt: string;
  lastFromAdmin: boolean;
  unreadFromUser: number;
}

/**
 * Admin↔User-Direktnachrichten. Die User-Seite (eigener Thread mit dem Admin-Team, `/api/messages`)
 * und die Admin-Seite (alle Threads verwalten, `/api/admin/messages`) liegen bewusst in einem Service.
 * Hält je einen Ungelesen-Zähler (User-Badge in der Navbar bzw. Admin-Tab-Badge).
 */
@Injectable({ providedIn: 'root' })
export class MessageService {
  private userUnread = new BehaviorSubject<number>(0);
  /** Ungelesene Admin-Nachrichten des eingeloggten Users (Navbar-Mail-Badge). */
  userUnread$ = this.userUnread.asObservable();

  private adminUnread = new BehaviorSubject<number>(0);
  /** Ungelesene User-Antworten über alle Threads (Admin-Tab-Badge). */
  adminUnread$ = this.adminUnread.asObservable();

  constructor(private http: HttpClient) {}

  // ---- User-Seite ----

  /** Eigener Thread mit dem Admin-Team (chronologisch, älteste zuerst). */
  getThread(): Observable<ChatMessage[]> {
    return this.http.get<ChatMessage[]>('/api/messages');
  }

  /** Antwort des Users (nur möglich, sobald der Admin den Thread gestartet hat). */
  reply(body: string): Observable<ChatMessage> {
    return this.http.post<ChatMessage>('/api/messages/reply', { body });
  }

  /** Eigene Admin-Nachrichten als gelesen markieren (leert das Badge sofort). */
  markUserSeen(): Observable<unknown> {
    return this.http.post('/api/messages/seen', {}).pipe(tap(() => this.userUnread.next(0)));
  }

  /** Ungelesen-Zähler des Users neu laden (Login + Polling). */
  refreshUserUnread(): void {
    this.http.get<{ count: number }>('/api/messages/unread-count').subscribe({
      next: r => this.userUnread.next(r.count),
      error: () => { /* nicht kritisch */ },
    });
  }

  // ---- Admin-Seite ----

  /** Alle Konversationen (ein Eintrag je User). */
  getThreads(): Observable<AdminThreadSummary[]> {
    return this.http.get<AdminThreadSummary[]>('/api/admin/messages/threads');
  }

  /** Vollständiger Thread mit einem User. */
  getAdminThread(userId: number): Observable<ChatMessage[]> {
    return this.http.get<ChatMessage[]>(`/api/admin/messages/threads/${userId}`);
  }

  /** Admin schickt/antwortet dem User (legt den Thread bei der ersten Nachricht an). */
  sendToUser(userId: number, body: string): Observable<ChatMessage> {
    return this.http.post<ChatMessage>(`/api/admin/messages/threads/${userId}`, { body });
  }

  /** User-Antworten eines Threads als vom Admin gelesen markieren. */
  markAdminSeen(userId: number): Observable<unknown> {
    return this.http.post(`/api/admin/messages/threads/${userId}/seen`, {});
  }

  /** Admin-Ungelesen-Zähler (über alle Threads) neu laden. */
  refreshAdminUnread(): void {
    this.http.get<{ count: number }>('/api/admin/messages/unread-count').subscribe({
      next: r => this.adminUnread.next(r.count),
      error: () => { /* nicht kritisch */ },
    });
  }

  /** Beim Logout lokale Zähler zurücksetzen. */
  reset(): void {
    this.userUnread.next(0);
    this.adminUnread.next(0);
  }
}
