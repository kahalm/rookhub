import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, Subject, tap } from 'rxjs';

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

/** Eine Seite der vollständigen Benachrichtigungs-History + Gesamtzahl (für „mehr laden"). */
export interface NotificationHistory {
  items: AppNotification[];
  total: number;
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

  private arrived = new Subject<void>();
  /** Feuert, wenn beim Zähler-Refresh (Login + 60-s-Poll) eine NEUE Benachrichtigung erkannt wurde
   *  (Zähler gestiegen). Komponenten hängen sich dran, um davon abhängige Daten ohne Seiten-Refresh
   *  nachzuladen — z. B. die Freundeszahl, wenn jemand meine Freundschaftsanfrage annimmt. */
  arrived$ = this.arrived.asObservable();

  /** Zeitpunkt der letzten optimistischen Verkleinerung (markSeen/markAllSeen). */
  private lastOptimisticAt = 0;
  /** Schutzfenster: kurz nach einer optimistischen Verkleinerung darf ein (evtl. noch
   *  in-flight gestarteter, veralteter) Server-Refresh den Zähler nicht wieder anheben. */
  private static readonly OPTIMISTIC_GRACE_MS = 5000;

  constructor(private http: HttpClient) {}

  /** Ungelesen-Zähler neu laden (Polling + bei Login). */
  refreshCount(): void {
    this.http.get<{ count: number }>(`${this.apiUrl}/count`).subscribe({
      next: r => {
        // Race-Schutz gegen Badge-Flackern: Ein gleichzeitig gestarteter Refresh liefert evtl.
        // noch den ALTEN (höheren) Wert, nachdem markSeen den Zähler optimistisch gesenkt hat.
        // Innerhalb des Schutzfensters einen HÖHEREN Serverwert ignorieren (Verkleinerungen +
        // echte neue Benachrichtigungen nach Ablauf des Fensters greifen normal).
        const withinGrace = Date.now() - this.lastOptimisticAt < InAppNotificationService.OPTIMISTIC_GRACE_MS;
        if (withinGrace && r.count > this.unseen.value) return;
        const increased = r.count > this.unseen.value;
        this.unseen.next(r.count);
        // Gestiegener Zähler = mindestens eine neue Benachrichtigung → abhängige Daten nachladen lassen.
        if (increased) this.arrived.next();
      },
      error: () => { /* nicht kritisch */ }
    });
  }

  /** Letzte Benachrichtigungen (neueste zuerst). `unseenOnly`=true liefert nur ungelesene
   * (die Glocke zeigt nur diese; gelesene bleiben über „Alle anzeigen" sichtbar). */
  list(take = 20, unseenOnly = false): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(`${this.apiUrl}?take=${take}&unseenOnly=${unseenOnly}`);
  }

  /** Eine Seite der vollständigen History (neueste zuerst) + Gesamtzahl. */
  history(page = 1, pageSize = 30): Observable<NotificationHistory> {
    return this.http.get<NotificationHistory>(`${this.apiUrl}/history?page=${page}&pageSize=${pageSize}`);
  }

  /** Alle als gelesen markieren (beim Öffnen der Glocke) — leert das Badge sofort. */
  markAllSeen(): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/seen`, {}).pipe(tap(() => {
      this.lastOptimisticAt = Date.now();
      this.unseen.next(0);
    }));
  }

  /** Eine einzelne Benachrichtigung als gelesen markieren (Klick darauf) — Badge -1. */
  markSeen(id: number): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/${id}/seen`, {}).pipe(tap(() => {
      this.lastOptimisticAt = Date.now();
      this.unseen.next(Math.max(0, this.unseen.value - 1));
    }));
  }

  /** Beim Logout den Zähler lokal zurücksetzen. */
  reset(): void {
    this.unseen.next(0);
  }
}
