import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, firstValueFrom } from 'rxjs';
import { ScoresheetResolveResult } from '@rh/features/games/scoresheet.service';
import {
  ClubGameRequest, ClubImportResult, ClubList, ClubMatch, ClubPreview, ImportGameDecision, LeagueScanState, RosterPerson,
  ScanRef, ScoresheetScan, ScoresheetStatus,
} from './club.models';

/**
 * Die Vereins-Datenbank über EINE Oberfläche, zwei Wege: angemeldet (`/api/league/club`, Vereinsgruppe) oder OHNE Konto
 * über einen Teilen-Link (`/api/league/s/{token}/club`). Einlesungen heißen angemeldet nach ihrer Nummer, ohne Konto nach
 * dem geheimen Schlüssel, den der Server beim Hochladen ausgibt — beides steckt in `ref`.
 */
export class ClubClient {
  private readonly base: string;

  constructor(private readonly http: HttpClient, readonly share: string | null) {
    this.base = share ? `/api/league/s/${encodeURIComponent(share)}/club` : '/api/league/club';
  }

  get anonymous(): boolean {
    return !!this.share;
  }

  private params(fide: string | null, q: string | null, page?: number): HttpParams {
    let p = new HttpParams();
    if (fide) p = p.set('fide', fide);
    if (q) p = p.set('q', q);
    if (page) p = p.set('page', page);
    return p;
  }

  list(fide: string | null, q: string | null, page: number): Promise<ClubList> {
    return firstValueFrom(this.http.get<ClubList>(`${this.base}/games`, { params: this.params(fide, q, page) }));
  }

  pgn(fide: string | null, q: string | null): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/games/pgn`, { params: this.params(fide, q), responseType: 'blob' }));
  }

  preview(pgn: string): Promise<ClubPreview> {
    return firstValueFrom(this.http.post<ClubPreview>(`${this.base}/games/preview`, { pgn }));
  }

  importPgn(pgn: string, games: ImportGameDecision[]): Promise<ClubImportResult> {
    return firstValueFrom(this.http.post<ClubImportResult>(`${this.base}/games/import`, { pgn, games }));
  }

  /** Eine Partie aus einem Partieformular; `scanRef` wird danach geschlossen. */
  addGame(body: ClubGameRequest, scanRef: string | null): Promise<{ id: number; anonymized: boolean }> {
    if (this.anonymous) {
      const params = scanRef ? new HttpParams().set('scanKey', scanRef) : undefined;
      return firstValueFrom(this.http.post<{ id: number; anonymized: boolean }>(`${this.base}/games`, { ...body, scanId: null }, { params }));
    }
    return firstValueFrom(this.http.post<{ id: number; anonymized: boolean }>(`${this.base}/games`,
      { ...body, scanId: scanRef ? Number(scanRef) : null }));
  }

  deleteGame(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.base}/games/${id}`));
  }

  /** Spieler zum Korrigieren: Ligaspieler, mit `all` dazu das Spielerverzeichnis der ganzen Megabase. */
  players(q: string, all = false): Promise<RosterPerson[]> {
    let params = new HttpParams().set('q', q);
    if (all) params = params.set('all', 'true');
    return firstValueFrom(this.http.get<RosterPerson[]>(`${this.base}/players`, { params }));
  }

  /** PGN einer öffentlichen Lichess-Studie (Adresse der Studie oder eines Kapitels). */
  async lichess(url: string): Promise<string> {
    return (await firstValueFrom(this.http.post<{ pgn: string }>(`${this.base}/games/lichess`, { url }))).pgn;
  }

  match(white: string, black: string): Promise<ClubMatch> {
    return firstValueFrom(this.http.post<ClubMatch>(`${this.base}/match`, { white, black }));
  }

  scoresheetStatus(): Promise<ScoresheetStatus> {
    return firstValueFrom(this.http.get<ScoresheetStatus>(`${this.base}/scoresheet/status`));
  }

  /** Die eigenen offenen Einlesungen — ohne Konto die zu den Schlüsseln, die sich der Browser gemerkt hat. */
  async scans(keys: string[] = []): Promise<ScanRef[]> {
    if (this.anonymous) {
      if (!keys.length) return [];
      const r = await firstValueFrom(this.http.post<{ key: string; scan: ScoresheetScan }[]>(`${this.base}/scans/lookup`, { keys }));
      return r.map(x => ({ ref: x.key, scan: x.scan }));
    }
    const list = await firstValueFrom(this.http.get<ScoresheetScan[]>(`${this.base}/scans`));
    return list.map(scan => ({ ref: String(scan.id), scan }));
  }

  async upload(file: File, language: string, side: 'white' | 'black' | 'auto'): Promise<ScanRef> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('language', language);
    form.append('side', side);
    if (this.anonymous) {
      const r = await firstValueFrom(this.http.post<{ key: string; scan: ScoresheetScan }>(`${this.base}/scans`, form));
      return { ref: r.key, scan: r.scan };
    }
    const scan = await firstValueFrom(this.http.post<ScoresheetScan>(`${this.base}/scans`, form));
    return { ref: String(scan.id), scan };
  }

  scan(ref: string): Promise<LeagueScanState> {
    return firstValueFrom(this.http.get<LeagueScanState>(`${this.base}/scans/${encodeURIComponent(ref)}`));
  }

  photo(ref: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/scans/${encodeURIComponent(ref)}/photo`, { responseType: 'blob' }));
  }

  /** Als Observable: die geteilte Korrektur-Sitzung hängt es an die Lebensdauer der Seite. */
  resolve(ref: string, prefix: string[], writtenFrom: number): Observable<ScoresheetResolveResult> {
    return this.http.post<ScoresheetResolveResult>(`${this.base}/scans/${encodeURIComponent(ref)}/resolve`, { prefix, writtenFrom });
  }

  discard(ref: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.base}/scans/${encodeURIComponent(ref)}`));
  }
}

/** Liefert den passenden Client: `share` = Token eines Teilen-Links (ohne Anmeldung), sonst angemeldet. */
@Injectable({ providedIn: 'root' })
export class ClubApiService {
  private readonly http = inject(HttpClient);

  client(share: string | null = null): ClubClient {
    return new ClubClient(this.http, share);
  }
}
