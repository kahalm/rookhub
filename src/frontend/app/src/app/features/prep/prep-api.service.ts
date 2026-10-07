import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { filterParams } from '@lh/core/league-api.service';
import { TrainingLines, TrainingLinesQuery, trainingLinesParams } from '@rh/shared/player-card/training-lines';
import { Account, AccountChecks, OpeningTree, ProfileView, RecentGames, TreeFilter } from '@lh/core/league.models';
import { PrepCardJson, PrepHit, PrepOptions, PrepSuggestionList } from './prep.models';

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

  /** Trainingslinien gegen diesen Spieler (2026-10-07) — Grenze/Zwilling wie die Karte. */
  trainingLines(id: number, o: PrepOptions, q: TrainingLinesQuery): Promise<TrainingLines> {
    return firstValueFrom(this.http.get<TrainingLines>(`/api/prep/player/${id}/training-lines`, { params: opts(trainingLinesParams(q), o) }));
  }

  tree(id: number, o: PrepOptions, color: 'w' | 's', line: string[], filter?: TreeFilter): Promise<OpeningTree> {
    const params = opts(filterParams(new HttpParams().set('color', color).set('line', line.join(' ')), filter), o);
    return firstValueFrom(this.http.get<OpeningTree>(`/api/prep/player/${id}/tree`, { params }));
  }

  pgn(id: number, o: PrepOptions): Promise<Blob> {
    return firstValueFrom(this.http.get(`/api/prep/player/${id}/pgn`, { params: opts(new HttpParams(), o), responseType: 'blob' }));
  }

  // ── Online-Konten suchen (Phase 4, prep.manage + Schalter Prep:AccountSearch) ──

  suggestions(id: number): Promise<PrepSuggestionList> {
    return firstValueFrom(this.http.get<PrepSuggestionList>(`/api/prep/player/${id}/suggestions`));
  }

  /** Jetzt suchen — dauert einige Sekunden (Lichess und chess.com werden einzeln gefragt). */
  scanSuggestions(id: number): Promise<PrepSuggestionList> {
    return firstValueFrom(this.http.post<PrepSuggestionList>(`/api/prep/player/${id}/suggestions/scan`, {}));
  }

  acceptSuggestion(id: number, sure: boolean): Promise<Account> {
    return firstValueFrom(this.http.post<Account>(`/api/prep/suggestions/${id}/accept`, { sure }));
  }

  rejectSuggestion(id: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/prep/suggestions/${id}/reject`, {}));
  }

  suggestionChecks(id: number): Promise<AccountChecks> {
    return firstValueFrom(this.http.get<AccountChecks>(`/api/prep/suggestions/${id}/checks`));
  }

  /** Ein eingetragenes Konto umstufen (0.639.0) — nur das eines Spielers, den LeagueHub nicht kennt (sonst 409 `leagueHub`). */
  updateAccount(id: number, body: { sure?: boolean; comment?: string }): Promise<Account> {
    return firstValueFrom(this.http.put<Account>(`/api/prep/accounts/${id}`, body));
  }

  /** Entfernen — samt der geholten Online-Partien; die Suche schlägt es nicht wieder vor. */
  deleteAccount(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/prep/accounts/${id}`));
  }
}

function opts(params: HttpParams, o: PrepOptions): HttpParams {
  if (o.all) params = params.set('all', 'true');
  if (o.twin) params = params.set('twin', 'true');
  return params;
}
