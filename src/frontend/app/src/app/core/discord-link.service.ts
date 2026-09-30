import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of, switchMap } from 'rxjs';
import { ConfirmService } from '../shared/confirm-dialog/confirm-dialog.component';
import { SnackbarService } from './snackbar.service';

/**
 * sessionStorage-Key für einen vorgemerkten Discord-Link-Token (anonyme Session). Früher lag er im
 * localStorage und überlebte dort das Schließen des Browsers — auf einem geteilten Gerät löste dann der
 * NÄCHSTE, der sich anmeldete, den Link eines Fremden ein. Jetzt nur noch in diesem Tab.
 */
export const DISCORD_LINK_STASH_KEY = 'rookhub_discord_link';

/** Ausgang von {@link DiscordLinkService.confirmAndLink}: `failed` = vorübergehend (Netz, 5xx), später noch einmal. */
export type DiscordLinkOutcome = 'linked' | 'declined' | 'rejected' | 'failed';

/** Die bestehende Discord-Verknüpfung des eigenen Kontos (Auszug aus GET /api/profile). */
interface CurrentDiscordLink {
  discordId: string | null;
  discordUsername: string | null;
}

/**
 * Liest Discord-ID und -Namen aus dem Token-Payload (`body.sig`, body = base64url(JSON {id,u,exp})) — NUR
 * zur Anzeige in der Rückfrage. Ob der Token echt ist, prüft allein der Server (Signatur + Ablauf).
 * `null`, wenn der Payload nicht lesbar ist.
 */
export function discordIdentityFromToken(token: string): { id: string; name: string | null } | null {
  try {
    const dot = token.lastIndexOf('.');
    if (dot <= 0) return null;
    const b64 = token.slice(0, dot).replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64 + '='.repeat((4 - (b64.length % 4)) % 4));
    const json = JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, c => c.charCodeAt(0))));
    const id = typeof json?.id === 'string' ? json.id.trim() : '';
    if (!id) return null;
    const name = typeof json.u === 'string' && json.u.trim() ? json.u.trim() : null;
    return { id, name };
  } catch {
    return null;
  }
}

/**
 * Verknüpfung des RookHub-Kontos mit Discord via bot-signiertem Token (`?dl=`-Param).
 * - Eingeloggt: erst nachfragen (mit dem Discord-Namen aus dem Token), dann an die API senden — außer das
 *   Konto ist schon mit genau dieser Discord-ID verknüpft (dann still, siehe confirmAndLink).
 * - Anonym: Token in sessionStorage vormerken; nach Login/Registrierung wird ebenfalls erst nachgefragt
 *   (siehe AuthService.storeUser → consumeStashed).
 *
 * Warum die Rückfrage: der Token bindet nur die Discord-ID, nicht den Empfänger. Ohne sie verknüpfte ein
 * fremder Link (Angreifer holt sich per /link einen Token für sein EIGENES Discord-Konto und schickt ihn dem
 * Opfer) das Konto still mit dem Angreifer — der Bot schickte ihm dann täglich die Trainingsdaten des Opfers.
 */
@Injectable({ providedIn: 'root' })
export class DiscordLinkService {
  private readonly confirmDialog = inject(ConfirmService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  constructor(private http: HttpClient) {}

  link(token: string): Observable<unknown> {
    return this.http.post('/api/profile/discord/link', { token });
  }

  unlink(): Observable<unknown> {
    return this.http.delete('/api/profile/discord');
  }

  /**
   * Fragt mit dem Discord-Namen aus dem Token nach und verknüpft erst nach „OK"; das Ergebnis meldet ein
   * Snackbar. Wirft nie. Ein unlesbarer Token kommt gar nicht erst bis zur Rückfrage (der Server lehnte ihn
   * ohnehin ab, und es gäbe keinen Namen zu zeigen).
   *
   * Vorher wird die bestehende Verknüpfung gelesen (GET /api/profile):
   * - schon mit GENAU dieser Discord-ID verknüpft → keine Rückfrage, `linked`. Der Bot hängt `?dl=` an jeden
   *   hideBoard-Rätsellink; ein Warn-Dialog bei jedem Rätselklick gewöhnte ans blinde Bestätigen und entwertete
   *   damit die einzige Schutzstufe. Nur wenn der Name im Token vom gespeicherten abweicht, geht der POST still
   *   hinaus (gleiche ID, hält den Namen aktuell — wie früher).
   * - mit einer ANDEREN Discord-ID verknüpft → die Rückfrage sagt, welche Verknüpfung ersetzt wird.
   * - Profil nicht lesbar (offline, 5xx) → wie unverknüpft nachfragen (sichere Seite).
   */
  confirmAndLink(token: string): Observable<DiscordLinkOutcome> {
    const identity = discordIdentityFromToken(token);
    if (!identity) {
      this.snackbar.info(this.translate.instant('profile.discord.linkFailed'), { duration: 4000 });
      return of('rejected');
    }
    return this.currentLink().pipe(
      switchMap((current): Observable<DiscordLinkOutcome> => {
        if (current?.discordId === identity.id) return this.refreshSameLink(token, identity.name, current);
        const discord = identity.name ?? identity.id;
        const ask$ = current?.discordId
          ? this.confirmDialog.ask('profile.discord.confirmLinkReplace',
              { discord, current: current.discordUsername ?? current.discordId })
          : this.confirmDialog.ask('profile.discord.confirmLink', { discord });
        return ask$.pipe(switchMap(ok => ok ? this.linkWithFeedback(token) : of<DiscordLinkOutcome>('declined')));
      }),
    );
  }

  /** Bestehende Verknüpfung des eigenen Kontos; `null`, wenn das Profil nicht lesbar ist. */
  private currentLink(): Observable<CurrentDiscordLink | null> {
    return this.http.get<Partial<CurrentDiscordLink> | null>('/api/profile').pipe(
      map(p => ({
        discordId: p?.discordId?.trim() || null,
        discordUsername: p?.discordUsername?.trim() || null,
      })),
      catchError(() => of(null)),
    );
  }

  /** Gleiche Discord-ID schon verknüpft: nichts fragen, nichts melden; nur einen geänderten Namen still nachziehen. */
  private refreshSameLink(token: string, tokenName: string | null, current: CurrentDiscordLink): Observable<DiscordLinkOutcome> {
    if (!tokenName || tokenName === current.discordUsername) return of('linked');
    return this.link(token).pipe(
      map((): DiscordLinkOutcome => 'linked'),
      catchError(() => of<DiscordLinkOutcome>('linked')),
    );
  }

  private linkWithFeedback(token: string): Observable<DiscordLinkOutcome> {
    return this.link(token).pipe(
      map((): DiscordLinkOutcome => {
        this.snackbar.info(this.translate.instant('profile.discord.linked'));
        return 'linked';
      }),
      catchError(err => {
        const key = err?.status === 409 ? 'profile.discord.linkConflict' : 'profile.discord.linkFailed';
        this.snackbar.info(this.translate.instant(key), { duration: 4000 });
        return of<DiscordLinkOutcome>(err?.status === 400 || err?.status === 409 ? 'rejected' : 'failed');
      }),
    );
  }

  stash(token: string): void {
    this.clearLegacyStash();
    try { sessionStorage.setItem(DISCORD_LINK_STASH_KEY, token); } catch { /* ignore */ }
  }

  /**
   * Löst einen vorgemerkten Token nach Login/Registrierung ein — nach derselben Rückfrage wie eingeloggt.
   * Erfolg, Ablehnen und endgültige Ablehnungen (400 ungültig/abgelaufen, 409 bereits an anderen Account
   * gebunden) entfernen den Stash; transiente Fehler (z.B. Netz) lassen ihn für später stehen.
   */
  consumeStashed(): void {
    this.clearLegacyStash();
    let token: string | null = null;
    try { token = sessionStorage.getItem(DISCORD_LINK_STASH_KEY); } catch { /* ignore */ }
    if (!token) return;

    this.confirmAndLink(token).subscribe(outcome => { if (outcome !== 'failed') this.clearStash(); });
  }

  private clearStash(): void {
    try { sessionStorage.removeItem(DISCORD_LINK_STASH_KEY); } catch { /* ignore */ }
  }

  /** Vormerkung aus der Zeit vor der Rückfrage (localStorage) — wird nie mehr eingelöst, nur weggeräumt. */
  private clearLegacyStash(): void {
    try { localStorage.removeItem(DISCORD_LINK_STASH_KEY); } catch { /* ignore */ }
  }
}
