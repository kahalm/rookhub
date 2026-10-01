import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { filterParams } from '@lh/core/league-api.service';
import { OpeningTree, ProfileView, RecentGames, TreeFilter } from '@lh/core/league.models';
import { PrepCardJson, PrepHit, PrepOptions } from './prep.models';

/**
 * Die Spielervorbereitung (`/api/prep/*`, Recht `prep.view`): Spieler suchen und seine Karte lesen. Die Filter des
 * Baums übersetzt dieselbe Funktion wie bei LeagueHub (`filterParams`); `unsure` wirkt am Server nur mit `prep.manage`.
 */
@Injectable({ providedIn: 'root' })
export class PrepApiService {
  private readonly http = inject(HttpClient);

  /** Die letzte Suche — die Spielerseite führt dorthin zurück. */
  readonly lastQuery = signal('');

  search(q: string): Promise<PrepHit[]> {
    return firstValueFrom(this.http.get<{ items: PrepHit[] }>('/api/prep/players', { params: { q } }).pipe(map(r => r.items)));
  }

  card(id: number, o: PrepOptions): Promise<PrepCardJson> {
    return firstValueFrom(this.http.get<PrepCardJson>(`/api/prep/player/${id}`, { params: opts(new HttpParams(), o) }));
  }

  profile(id: number, o: PrepOptions, filter: TreeFilter): Promise<ProfileView> {
    return firstValueFrom(this.http.get<ProfileView>(`/api/prep/player/${id}/profile`, { params: opts(filterParams(new HttpParams(), filter), o) }));
  }

  recent(id: number, o: PrepOptions, color?: 'w' | 's'): Promise<RecentGames> {
    let params = opts(new HttpParams(), o);
    if (color) params = params.set('color', color);
    return firstValueFrom(this.http.get<RecentGames>(`/api/prep/player/${id}/recent`, { params }));
  }

  tree(id: number, o: PrepOptions, color: 'w' | 's', line: string[], filter?: TreeFilter): Promise<OpeningTree> {
    const params = opts(filterParams(new HttpParams().set('color', color).set('line', line.join(' ')), filter), o);
    return firstValueFrom(this.http.get<OpeningTree>(`/api/prep/player/${id}/tree`, { params }));
  }

  pgn(id: number, o: PrepOptions): Promise<Blob> {
    return firstValueFrom(this.http.get(`/api/prep/player/${id}/pgn`, { params: opts(new HttpParams(), o), responseType: 'blob' }));
  }
}

function opts(params: HttpParams, o: PrepOptions): HttpParams {
  if (o.all) params = params.set('all', 'true');
  if (o.twin) params = params.set('twin', 'true');
  return params;
}
