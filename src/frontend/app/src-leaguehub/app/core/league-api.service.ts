import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { League, LeagueIndex, OpeningTree, PlayerCard, RecentGames, SharedFixture, UpdateStatus } from './league.models';

/** LeagueHub-Endpunkte (`/api/league/*`). Teilen-Links (`/api/league/s/{token}`) gehen ohne Anmeldung. */
@Injectable({ providedIn: 'root' })
export class LeagueApiService {
  private readonly http = inject(HttpClient);
  private readonly leagues = new Map<number, League>();

  index(): Promise<LeagueIndex> {
    return firstValueFrom(this.http.get<LeagueIndex>('/api/league/index'));
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

  tree(fide: string, color: 'w' | 's', line: string[], token: string | null): Promise<OpeningTree> {
    const params = new HttpParams().set('color', color).set('line', line.join(' '));
    return firstValueFrom(this.http.get<OpeningTree>(`${this.base(token)}/player/${encodeURIComponent(fide)}/tree`, { params }));
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
