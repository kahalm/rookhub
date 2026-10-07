import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Account, AccountChecks, AccountInput, AdminClub, Broadcast, ClubInput, FixturePairing, ForecastStats, GameSources, League, LeagueIndex, OpeningTree, PlayerCard, ProfileView, RecentGames, RhGroup, SharedFixture, SuggestionList, TreeFilter, UpdateStatus } from './league.models';
import { TrainingLines, TrainingLinesQuery, trainingLinesParams } from '@rh/shared/player-card/training-lines';

/** LeagueHub-Endpunkte (`/api/league/*`). Teilen-Links (`/api/league/s/{token}`) gehen ohne Anmeldung. */
@Injectable({ providedIn: 'root' })
export class LeagueApiService {
  private readonly http = inject(HttpClient);
  private readonly leagues = new Map<number, League>();

  index(): Promise<LeagueIndex> {
    return firstValueFrom(this.http.get<LeagueIndex>('/api/league/index'));
  }

  /** Partien im Bestand je Quelle (0.626.0; über einen Teilen-Link seit 0.627.0) — der Server zählt höchstens alle 30 min neu.
   *  `tnr` = Liga, `fides` = Meldeliste des Gegners (0.628.0, nur angemeldet; über den Link bestimmt der Server beides selbst). */
  /** Treffer der Prognose je Runde/Liga/gesamt (0.650.0) — für alle Ansichten derselbe Stand, deshalb je Link einmal geholt. */
  forecastStats(token: string | null = null): Promise<ForecastStats> {
    const key = token ?? '';
    let p = this.statsCache.get(key);
    if (!p) {
      p = firstValueFrom(this.http.get<ForecastStats>(`${this.base(token)}/forecast-stats`));
      p.catch(() => this.statsCache.delete(key));
      this.statsCache.set(key, p);
    }
    return p;
  }
  private readonly statsCache = new Map<string, Promise<ForecastStats>>();

  sources(token: string | null = null, fides: string[] = [], tnr: number | null = null): Promise<GameSources> {
    const q = token ? [] : [
      ...(tnr ? [`tnr=${tnr}`] : []),
      ...(fides.length ? [`fides=${fides.map(encodeURIComponent).join(',')}`] : []),
    ];
    return firstValueFrom(this.http.get<GameSources>(`${this.base(token)}/sources${q.length ? '?' + q.join('&') : ''}`));
  }

  async league(tnr: number, fresh = false): Promise<League> {
    if (fresh || !this.leagues.has(tnr)) this.leagues.set(tnr, await firstValueFrom(this.http.get<League>(`/api/league/${tnr}`)));
    return this.leagues.get(tnr)!;
  }

  clearCache(): void {
    this.leagues.clear();
  }

  private base(token: string | null): string {
    return token ? `/api/league/s/${encodeURIComponent(token)}` : '/api/league';
  }

  /** Brettpaarungen einer gespielten Begegnung samt Partien (0.673.0); über den Link bestimmt der Server die Begegnung. */
  fixtureGames(tnr: number | null, round: number, team: string, token: string | null): Promise<FixturePairing[]> {
    const url = token ? `${this.base(token)}/games`
      : `/api/league/${tnr}/round/${round}/games?team=${encodeURIComponent(team)}`;
    return firstValueFrom(this.http.get<FixturePairing[]>(url));
  }

  card(fide: string, token: string | null): Promise<PlayerCard> {
    return firstValueFrom(this.http.get<PlayerCard>(`${this.base(token)}/player/${encodeURIComponent(fide)}`));
  }

  pgn(fide: string, token: string | null): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base(token)}/player/${encodeURIComponent(fide)}/pgn`, { responseType: 'blob' }));
  }

  /** Die letzten Partien samt PGN; `color` = nur mit dieser Farbe (die letzten 8 davon, nicht 8 gemischte gefiltert). */
  recent(fide: string, token: string | null, color?: 'w' | 's'): Promise<RecentGames> {
    const q = color ? `?color=${color}` : '';
    return firstValueFrom(this.http.get<RecentGames>(`${this.base(token)}/player/${encodeURIComponent(fide)}/recent${q}`));
  }

  tree(fide: string, color: 'w' | 's', line: string[], token: string | null, filter?: TreeFilter): Promise<OpeningTree> {
    const params = filterParams(new HttpParams().set('color', color).set('line', line.join(' ')), filter);
    return firstValueFrom(this.http.get<OpeningTree>(`${this.base(token)}/player/${encodeURIComponent(fide)}/tree`, { params }));
  }

  /** Eröffnungsprofil der Karte über gefilterte Partien (0.617.0) — dieselben Filter wie der Baum. */
  profile(fide: string, token: string | null, filter: TreeFilter): Promise<ProfileView> {
    const params = filterParams(new HttpParams(), filter);
    return firstValueFrom(this.http.get<ProfileView>(`${this.base(token)}/player/${encodeURIComponent(fide)}/profile`, { params }));
  }

  /** Trainingslinien gegen diesen Spieler (2026-10-07): eigene Repertoires, gereiht nach seinen Partien — nur angemeldet. */
  trainingLines(fide: string, q: TrainingLinesQuery): Promise<TrainingLines> {
    return firstValueFrom(this.http.get<TrainingLines>(`/api/league/player/${encodeURIComponent(fide)}/training-lines`,
      { params: trainingLinesParams(q) }));
  }

  // ── Online-Konten eines Spielers (0.605.0, league.manage) ──

  /** Mit `token` über einen Teilen-Link OHNE Anmeldung (0.630.0): immer „gesichert", vermerkt als „anonym". */
  addAccount(fide: string, input: AccountInput, token: string | null = null): Promise<Account> {
    const body = token ? { site: input.site, user: input.user, comment: input.comment } : input;
    return firstValueFrom(this.http.post<Account>(`${this.base(token)}/player/${encodeURIComponent(fide)}/accounts`, body));
  }

  updateAccount(id: number, input: AccountInput): Promise<Account> {
    return firstValueFrom(this.http.put<Account>(`/api/league/accounts/${id}`, input));
  }

  deleteAccount(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/league/accounts/${id}`));
  }

  /** Die Partien des Kontos gleich (neu) abrufen lassen. */
  syncAccount(id: number): Promise<Account> {
    return firstValueFrom(this.http.post<Account>(`/api/league/accounts/${id}/sync`, {}));
  }

  // ── Konto-Vorschläge (0.607.0, league.manage) ──

  /** Alle offenen Vorschläge (stärkste zuerst) samt Stand der Suche. */
  /** Konto-Prüfung (i) eines eingetragenen Kontos bzw. eines Vorschlags (0.619.0) — holt das Profil frisch, dauert ein paar Sekunden. */
  accountChecks(kind: 'account' | 'suggestion', id: number): Promise<AccountChecks> {
    return firstValueFrom(this.http.get<AccountChecks>(`/api/league/${kind === 'account' ? 'accounts' : 'suggestions'}/${id}/checks`));
  }

  suggestions(): Promise<SuggestionList> {
    return firstValueFrom(this.http.get<SuggestionList>('/api/league/suggestions'));
  }

  playerSuggestions(fide: string): Promise<SuggestionList> {
    return firstValueFrom(this.http.get<SuggestionList>(`/api/league/player/${encodeURIComponent(fide)}/suggestions`));
  }

  /** Für diesen Spieler jetzt auf Lichess und chess.com suchen (dauert einige Sekunden). */
  scanSuggestions(fide: string): Promise<SuggestionList> {
    return firstValueFrom(this.http.post<SuggestionList>(`/api/league/player/${encodeURIComponent(fide)}/suggestions/scan`, {}));
  }

  acceptSuggestion(id: number, sure: boolean): Promise<Account> {
    return firstValueFrom(this.http.post<Account>(`/api/league/suggestions/${id}/accept`, { sure }));
  }

  rejectSuggestion(id: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/league/suggestions/${id}/reject`, {}));
  }

  // ── Lichess-Übertragungen (0.608.0, league.manage) ──

  broadcasts(): Promise<Broadcast[]> {
    return firstValueFrom(this.http.get<Broadcast[]>('/api/league/admin/broadcasts'));
  }

  /** Per Link (Turnier oder Runde) hinzufügen und gleich einspielen. */
  // ---- Vereine verwalten (0.700.0, nur Admins mit league.manage) ----

  adminClubs(): Promise<AdminClub[]> {
    return firstValueFrom(this.http.get<AdminClub[]>('/api/league/admin/clubs'));
  }

  createClub(input: ClubInput): Promise<AdminClub> {
    return firstValueFrom(this.http.post<AdminClub>('/api/league/admin/clubs', input));
  }

  updateClub(id: number, input: ClubInput): Promise<AdminClub> {
    return firstValueFrom(this.http.put<AdminClub>(`/api/league/admin/clubs/${id}`, input));
  }

  addClubGroup(clubId: number, groupId: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/league/admin/clubs/${clubId}/groups/${groupId}`, null));
  }

  removeClubGroup(clubId: number, groupId: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/league/admin/clubs/${clubId}/groups/${groupId}`));
  }

  /** RookHubs Gruppen (Admin-Endpunkt, `groups.manage` — Admins haben jedes Recht). */
  groups(): Promise<RhGroup[]> {
    return firstValueFrom(this.http.get<RhGroup[]>('/api/admin/groups'));
  }

  addBroadcast(url: string): Promise<{ tourId: string; name: string; games: number; finished: boolean; error: string | null }> {
    return firstValueFrom(this.http.post<{ tourId: string; name: string; games: number; finished: boolean; error: string | null }>(
      '/api/league/admin/broadcasts', { url }));
  }

  shared(token: string): Promise<SharedFixture> {
    return firstValueFrom(this.http.get<SharedFixture>(this.base(token)));
  }

  createShare(tnr: number, round: number, team: string): Promise<{ token: string; expires: string }> {
    return firstValueFrom(this.http.post<{ token: string; expires: string }>('/api/league/share', { tnr, round, team }));
  }

  deleteShare(token: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/league/share/${encodeURIComponent(token)}`));
  }

  startUpdate(): Promise<UpdateStatus> {
    return firstValueFrom(this.http.post<UpdateStatus>('/api/league/update', {}));
  }

  updateStatus(): Promise<UpdateStatus> {
    return firstValueFrom(this.http.get<UpdateStatus>('/api/league/update/status'));
  }
}

/** Die Filter-Parameter von Baum und Profil (Server: `LeagueProfileStore.TreeFilter.Parse`). */
export function filterParams(params: HttpParams, filter?: TreeFilter): HttpParams {
  if (filter && filter.source !== 'board') {
    params = params.set('source', filter.source);
    if (filter.speeds.length) params = params.set('speeds', filter.speeds.join(','));
    if (filter.withUnsure) params = params.set('unsure', 'true');
  }
  if (filter?.years) params = params.set('years', filter.years);
  return params;
}
