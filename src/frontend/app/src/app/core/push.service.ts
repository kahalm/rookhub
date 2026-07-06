import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { SwPush } from '@angular/service-worker';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';

export interface PushConfig {
  /** VAPID-Public-Key; null = serverseitig nicht konfiguriert (Push nicht verfügbar). */
  publicKey: string | null;
  /** Aktuell aktivierte Bereiche (Kategorie-Keys). Leer = Push aus. */
  enabledCategories: string[];
}

/**
 * Web-Push-Client auf Basis des Angular Service Workers (ngsw). Holt die serverseitige Konfiguration
 * (VAPID-Key + aktivierte Bereiche), meldet Browser-Subscriptions an/ab und speichert die Bereichs-
 * Präferenzen. Anzeige der Push-Benachrichtigung + Klick-Navigation übernimmt ngsw (Payload trägt
 * `notification.data.onActionClick`); zusätzlich navigieren wir bei offener App via `notificationClicks`.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private http = inject(HttpClient);
  private swPush = inject(SwPush);
  private router = inject(Router);

  /** Unterstützt der Browser Push UND ist der Service Worker aktiv (nur im Prod-Build)? */
  get supported(): boolean {
    return this.swPush.isEnabled && typeof Notification !== 'undefined' && 'PushManager' in window;
  }

  get permissionDenied(): boolean {
    return typeof Notification !== 'undefined' && Notification.permission === 'denied';
  }

  constructor() {
    // Klick auf eine Push-Benachrichtigung bei geöffneter App → zur hinterlegten URL navigieren.
    if (this.swPush.isEnabled) {
      this.swPush.notificationClicks.subscribe(({ notification }) => {
        const url = (notification as any)?.data?.url;
        if (typeof url === 'string' && url.startsWith('/')) this.router.navigateByUrl(url);
      });
    }
  }

  getConfig(): Observable<PushConfig> {
    return this.http.get<PushConfig>('/api/notifications/push/config');
  }

  /** Fordert (falls nötig) eine Browser-Subscription an und registriert sie serverseitig.
   *  Wirft, wenn der User die Berechtigung ablehnt bzw. Push nicht verfügbar ist. */
  async ensureSubscribed(publicKey: string): Promise<void> {
    const existing = await this.currentSubscription();
    const sub = existing ?? await this.swPush.requestSubscription({ serverPublicKey: publicKey });
    const json = sub.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } };
    await this.http.post<void>('/api/notifications/push/subscribe', {
      endpoint: json.endpoint, p256dh: json.keys?.p256dh, auth: json.keys?.auth,
    }).toPromise();
  }

  /** Meldet die aktuelle Browser-Subscription server- und clientseitig ab (idempotent). */
  async removeSubscription(): Promise<void> {
    const sub = await this.currentSubscription();
    if (sub) {
      await this.http.post<void>('/api/notifications/push/unsubscribe', { endpoint: sub.endpoint }).toPromise();
      try { await this.swPush.unsubscribe(); } catch { /* schon abgemeldet → egal */ }
    }
  }

  /** Speichert die aktivierten Bereiche; Antwort = effektive Keys (Server verwirft z. B. „admin" für Nicht-Admins). */
  setPreferences(categories: string[]): Observable<{ categories: string[] }> {
    return this.http.put<{ categories: string[] }>('/api/notifications/push/preferences', { categories });
  }

  private currentSubscription(): Promise<PushSubscription | null> {
    return new Promise(resolve => {
      const s = this.swPush.subscription.subscribe(sub => { s.unsubscribe(); resolve(sub); });
    });
  }
}
