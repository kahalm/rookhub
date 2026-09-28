import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, firstValueFrom } from 'rxjs';
import { ScoresheetResolveResult } from '@rh/features/games/scoresheet.service';
import {
  ClubGameRequest, ClubImportResult, ClubList, ClubMatch, LeagueScanState, RosterPerson, ScoresheetScan, ScoresheetStatus,
} from './club.models';

const BASE = '/api/league/club';

/** Vereins-Datenbank: Partien lesen (`league.view`), hochladen und Formulare einlesen (`league.contribute`). */
@Injectable({ providedIn: 'root' })
export class ClubApiService {
  private readonly http = inject(HttpClient);

  private params(fide: string | null, q: string | null, page?: number): HttpParams {
    let p = new HttpParams();
    if (fide) p = p.set('fide', fide);
    if (q) p = p.set('q', q);
    if (page) p = p.set('page', page);
    return p;
  }

  list(fide: string | null, q: string | null, page: number): Promise<ClubList> {
    return firstValueFrom(this.http.get<ClubList>(`${BASE}/games`, { params: this.params(fide, q, page) }));
  }

  pgn(fide: string | null, q: string | null): Promise<Blob> {
    return firstValueFrom(this.http.get(`${BASE}/games/pgn`, { params: this.params(fide, q), responseType: 'blob' }));
  }

  importPgn(pgn: string, anonymize: boolean): Promise<ClubImportResult> {
    return firstValueFrom(this.http.post<ClubImportResult>(`${BASE}/games/import`, { pgn, anonymize }));
  }

  addGame(body: ClubGameRequest): Promise<{ id: number; anonymized: boolean }> {
    return firstValueFrom(this.http.post<{ id: number; anonymized: boolean }>(`${BASE}/games`, body));
  }

  deleteGame(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${BASE}/games/${id}`));
  }

  players(q: string): Promise<RosterPerson[]> {
    return firstValueFrom(this.http.get<RosterPerson[]>(`${BASE}/players`, { params: new HttpParams().set('q', q) }));
  }

  match(white: string, black: string): Promise<ClubMatch> {
    return firstValueFrom(this.http.post<ClubMatch>(`${BASE}/match`, { white, black }));
  }

  scoresheetStatus(): Promise<ScoresheetStatus> {
    return firstValueFrom(this.http.get<ScoresheetStatus>(`${BASE}/scoresheet/status`));
  }

  scans(): Promise<ScoresheetScan[]> {
    return firstValueFrom(this.http.get<ScoresheetScan[]>(`${BASE}/scans`));
  }

  upload(file: File, language: string, side: 'white' | 'black' | 'auto'): Promise<ScoresheetScan> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('language', language);
    form.append('side', side);
    return firstValueFrom(this.http.post<ScoresheetScan>(`${BASE}/scans`, form));
  }

  scan(id: number): Promise<LeagueScanState> {
    return firstValueFrom(this.http.get<LeagueScanState>(`${BASE}/scans/${id}`));
  }

  photo(id: number): Promise<Blob> {
    return firstValueFrom(this.http.get(`${BASE}/scans/${id}/photo`, { responseType: 'blob' }));
  }

  /** Als Observable: die geteilte Korrektur-Sitzung hängt es an die Lebensdauer der Seite. */
  resolve(id: number, prefix: string[], writtenFrom: number): Observable<ScoresheetResolveResult> {
    return this.http.post<ScoresheetResolveResult>(`${BASE}/scans/${id}/resolve`, { prefix, writtenFrom });
  }

  discard(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${BASE}/scans/${id}`));
  }
}
