import { Injectable, inject } from '@angular/core';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ClubApiService } from './club-api.service';

/**
 * Eine Partie aus LeagueHub in RookHubs „Meine Partien" legen, dorthin springen oder ihren öffentlichen Link holen
 * (Wunsch 2026-09-28, Spielerkarte → „Letzte Partien" → nachspielen). Dasselbe Konto, dieselbe API: `POST /api/games/import`
 * legt eine schon vorhandene Partie nicht doppelt an und liefert auch dann ihre Id — zweimal klicken schadet nicht.
 */
@Injectable({ providedIn: 'root' })
export class MyGamesService {
  private readonly club = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly handoff = inject(HandoffService);

  /** RookHub zu diesem LeagueHub — auf localhost oder einer IP gibt es keins. */
  get rookHubUrl(): string | null { return this.handoff.rookHubUrl; }

  /** Nur angemeldet (die Partie landet im eigenen Konto) und nur, wenn es ein RookHub dazu gibt. */
  get available(): boolean { return this.auth.isLoggedIn && !!this.rookHubUrl; }

  /** Die Partie ablegen → ihre Id in RookHub, `null` wenn der Text keine übernehmbare Partie ist. */
  async save(pgn: string): Promise<number | null> {
    const r = await this.club.addToMyGames(pgn);
    return r.ids[0] ?? null;
  }

  /** Der öffentliche Link der abgelegten Partie (RookHubs Teilen-Link, ohne Anmeldung lesbar). */
  async shareUrl(id: number): Promise<string> {
    const g = await this.club.savedGame(id);
    return `${this.rookHubUrl}/g/${g.shareToken}`;
  }

  /** Nach RookHub auf die Partie springen — angemeldet über einen Einmal-Code. */
  open(id: number): Promise<void> {
    return this.handoff.jumpToRookHub(`games/${id}`);
  }
}
