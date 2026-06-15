import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

/** Eine generische In-App-Benachrichtigung (Navbar-Glocke). */
export interface AppNotification {
  id: number;
  type: string;
  /** i18n-Parameter (z. B. username/courseName/solved) — der Text wird im Client gerendert. */
  data: Record<string, string> | null;
  link: string | null;
  createdAt: string;
  seen: boolean;
}

/**
 * Holt/zählt die generischen In-App-Benachrichtigungen des eingeloggten Users
 * (`/api/notifications`) und hält den Ungelesen-Zähler fürs Glocken-Badge.
 * Bewusst getrennt vom browser-`NotificationService` (Web-Notification-API, Phase 3).
 */
@Injectable({ providedIn: 'root' })
export class InAppNotificationService {
  private readonly apiUrl = '/api/notifications';
  private unseen = new BehaviorSubject<number>(0);
  /** Ungelesen-Zähler fürs Glocken-Badge. */
  unseenCount$ = this.unseen.asObservable();

  constructor(private http: HttpClient) {}

  /** Ungelesen-Zähler neu laden (Polling + bei Login). */
  refreshCount(): void {
    this.http.get<{ count: number }>(`${this.apiUrl}/count`).subscribe({
      next: r => this.unseen.next(r.count),
      error: () => { /* nicht kritisch */ }
    });
  }

  /** Letzte Benachrichtigungen (neueste zuerst). */
  list(take = 20): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(`${this.apiUrl}?take=${take}`);
  }

  /** Alle als gelesen markieren (beim Öffnen der Glocke) — leert das Badge sofort. */
  markAllSeen(): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/seen`, {}).pipe(tap(() => this.unseen.next(0)));
  }

  /** Beim Logout den Zähler lokal zurücksetzen. */
  reset(): void {
    this.unseen.next(0);
  }
}
