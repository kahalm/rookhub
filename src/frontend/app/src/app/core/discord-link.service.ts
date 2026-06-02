import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** localStorage-Key für einen vorgemerkten Discord-Link-Token (anonyme Session). */
export const DISCORD_LINK_STASH_KEY = 'rookhub_discord_link';

/**
 * Verknüpfung des RookHub-Kontos mit Discord via bot-signiertem Token (`?dl=`-Param).
 * - Eingeloggt: Token sofort an die API senden.
 * - Anonym: Token in localStorage vormerken; wird nach Login/Registrierung
 *   automatisch eingelöst (siehe AuthService.storeUser → consumeStashed).
 */
@Injectable({ providedIn: 'root' })
export class DiscordLinkService {
  constructor(private http: HttpClient) {}

  link(token: string): Observable<unknown> {
    return this.http.post('/api/profile/discord/link', { token });
  }

  unlink(): Observable<unknown> {
    return this.http.delete('/api/profile/discord');
  }

  stash(token: string): void {
    try { localStorage.setItem(DISCORD_LINK_STASH_KEY, token); } catch { /* ignore */ }
  }

  /**
   * Löst einen vorgemerkten Token nach Login/Registrierung ein. Erfolg + endgültige
   * Ablehnungen (400 ungültig/abgelaufen, 409 bereits an anderen Account gebunden)
   * entfernen den Stash; transiente Fehler (z.B. Netz) lassen ihn für später stehen.
   */
  consumeStashed(): void {
    let token: string | null = null;
    try { token = localStorage.getItem(DISCORD_LINK_STASH_KEY); } catch { /* ignore */ }
    if (!token) return;

    this.link(token).subscribe({
      next: () => this.clearStash(),
      error: (err) => { if (err?.status === 400 || err?.status === 409) this.clearStash(); }
    });
  }

  private clearStash(): void {
    try { localStorage.removeItem(DISCORD_LINK_STASH_KEY); } catch { /* ignore */ }
  }
}
