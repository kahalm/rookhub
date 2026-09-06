import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService, AuthResponse } from './auth.service';
import { partnerSiteUrl } from './partner-site';

/**
 * Der Sprung zwischen RookHub und der Turnierseite.
 *
 * <p>Beide liegen auf verschiedenen Origins und teilen den `localStorage` NICHT — wer hier
 * angemeldet ist, ist es drueben nicht, obwohl dasselbe Konto dahintersteht. Der Sprung holt
 * deshalb einen Einmal-Code (60 s, einmal einloesbar) und haengt ihn an die Ziel-URL; die
 * Gegenseite tauscht ihn beim Start gegen ihre eigene Anmeldung.</p>
 *
 * <p>Ohne Anmeldung wird schlicht ohne Code gesprungen — dann landet man drueben auf der
 * oeffentlichen Seite bzw. der Anmeldemaske.</p>
 *
 * <p>Der Code deckt aber nur den KLICK im Menue ab. Wer die Turnierseite direkt aufruft, nachdem
 * er sich vorhin in RookHub angemeldet hat, bringt keinen mit — dafuer gibt es die GETEILTE
 * Anmeldung: der Server legt beim Anmelden ein Cookie auf der gemeinsamen Elterndomaene ab, und
 * <c>consumeIncoming</c> tauscht es beim Start gegen eine eigene Anmeldung (siehe
 * <c>SharedSessionService</c> auf der Serverseite).</p>
 */
@Injectable({ providedIn: 'root' })
export class HandoffService {
  private http = inject(HttpClient);
  private auth = inject(AuthService);

  /** Parametername in der Ziel-URL. */
  static readonly Param = 'h';

  /** Adresse der Schwesterseite, oder `null` (dann keinen Sprung anbieten). */
  get partnerUrl(): string | null { return partnerSiteUrl(); }

  /** Springt zur Schwesterseite — angemeldet, wenn es geht. `path` ohne fuehrenden Schraegstrich. */
  async jump(path = ''): Promise<void> {
    const base = this.partnerUrl;
    if (!base) return;
    const target = `${base}/${path}`.replace(/([^:]\/)\/+/g, '$1');

    if (!this.auth.isLoggedIn) { location.href = target; return; }
    try {
      const res = await firstValueFrom(this.http.post<{ code: string }>('/api/auth/handoff', {}));
      const sep = target.includes('?') ? '&' : '?';
      location.href = `${target}${sep}${HandoffService.Param}=${encodeURIComponent(res.code)}`;
    } catch {
      // Kein Code zu bekommen ist kein Grund, den Sprung zu verweigern — drueben steht dann
      // die Anmeldemaske, und das ist immer noch besser als ein toter Knopf.
      location.href = target;
    }
  }

  /**
   * Loest einen mitgebrachten Code ein (beim App-Start aufzurufen) und raeumt ihn aus der URL —
   * er ist verbraucht, und im Verlauf hat er nichts verloren. `true`, wenn dadurch eine Anmeldung
   * entstanden ist.
   */
  async consumeIncoming(): Promise<boolean> {
    const url = new URL(location.href);
    const code = url.searchParams.get(HandoffService.Param);

    if (code) {
      url.searchParams.delete(HandoffService.Param);
      history.replaceState({}, '', url.pathname + (url.search || '') + url.hash);
    }

    if (this.auth.isLoggedIn) return false;      // schon angemeldet: Code einfach verfallen lassen

    if (code) {
      try {
        const res = await firstValueFrom(
          this.http.post<AuthResponse>('/api/auth/handoff/exchange', { code }));
        this.auth.adoptSession(res);
        return true;
      } catch {
        return false;                            // abgelaufen/verbraucht → Anmeldemaske
      }
    }

    return this.adoptSharedSession();
  }

  /**
   * Ohne Code: besteht auf der Schwesterseite schon eine Anmeldung? Nachweis ist das Cookie auf
   * der gemeinsamen Elterndomaene — es ist <c>HttpOnly</c>, hier also nicht lesbar; nur der Server
   * kann sagen, ob es taugt. 401 ist der Normalfall (nicht angemeldet, oder es gibt gar keine
   * gemeinsame Domaene) und bleibt deshalb still.
   */
  async adoptSharedSession(): Promise<boolean> {
    if (this.auth.isLoggedIn) return false;
    try {
      const res = await firstValueFrom(this.http.post<AuthResponse>('/api/auth/session', {}));
      this.auth.adoptSession(res);
      return true;
    } catch {
      return false;
    }
  }
}
