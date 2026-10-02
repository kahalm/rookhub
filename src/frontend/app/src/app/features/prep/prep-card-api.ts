import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from '@rh/core/auth.service';
import { OpeningTree, PlayerCard, ProfileView, RecentGames, TreeFilter } from '@lh/core/league.models';
import { PlayerCardApi } from '@rh/shared/player-card/player-card-api';
import { PrepApiService } from './prep-api.service';
import { PrepCardJson, PrepOptions, PrepScope } from './prep.models';

/** Die Karte der Spielervorbereitung in der Form, die die Spielerkarte erwartet: `key` = Id im Bestand, `fide` leer ohne
 * FIDE-ID (dann kein FIDE-Link), Konten ohne Id (ihre Pflege und Prüfung gehören zu LeagueHub). */
export function toPlayerCard(c: PrepCardJson): PlayerCard {
  return {
    ...c, fide: c.fide ?? '', key: String(c.id),
    accounts: (c.accounts ?? []).map(({ id: _id, ...a }) => a),
  };
}

export function scopeOf(c: PrepCardJson): PrepScope {
  return { id: c.id, games: c.games, loaded: c.loaded, limited: c.limited, limit: c.limit, max: c.max, since: c.since, twin: c.twin,
    twinIncluded: c.twinIncluded, accountSearch: c.accountSearch === true };
}

/**
 * Die Schnittstelle der Spielerkarte (`PLAYER_CARD_API`) für die Spielervorbereitung — je Spielerseite eine (die Seite
 * stellt sie bereit). Hält, was die Seite umschaltet (alle laden, Zwilling), und was die Karte zuletzt geladen hat.
 */
@Injectable()
export class PrepCardApi implements PlayerCardApi {
  private readonly api = inject(PrepApiService);
  private readonly auth = inject(AuthService);

  readonly options = signal<PrepOptions>({ all: false, twin: false });
  readonly scope = signal<PrepScope | null>(null);
  readonly accountsEditable = false;

  /** Unsichere Konten nur für Verwalter — der Server prüft es ebenso. */
  unsureAllowed(): boolean {
    return this.auth.has('prep.manage');
  }

  async card(key: string): Promise<PlayerCard> {
    const c = await this.api.card(Number(key), this.options());
    this.scope.set(scopeOf(c));
    return toPlayerCard(c);
  }

  profile(key: string, _token: string | null, filter: TreeFilter): Promise<ProfileView> {
    return this.api.profile(Number(key), this.options(), filter);
  }

  recent(key: string, _token: string | null, color?: 'w' | 's'): Promise<RecentGames> {
    return this.api.recent(Number(key), this.options(), color);
  }

  tree(key: string, color: 'w' | 's', line: string[], _token: string | null, filter?: TreeFilter): Promise<OpeningTree> {
    return this.api.tree(Number(key), this.options(), color, line, filter);
  }

  pgn(key: string): Promise<Blob> {
    return this.api.pgn(Number(key), this.options());
  }
}
