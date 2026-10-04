import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, firstValueFrom } from 'rxjs';
import { ScoresheetResolveResult } from '@rh/features/games/scoresheet.service';
import { ChessBaseResult, ClubDraft, ClubDraftDetail, ClubGame, ClubGameDetail, ClubGameRequest, ClubGameUpdate, ClubImportResult, ClubList, ClubMatch, ClubPreview, ImportGameDecision, LeagueScanState, OpenScan, RosterPerson, ScanRef, ScoresheetScan, ScoresheetStatus } from './club.models';

/** Stand eines Stapel-Uploads (0.651.0). */
export interface BatchState { key: string; files: number; bytes: number; finished: boolean }

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

  /** `mine` (0.652.0): nur die selbst hochgeladenen — Lasche „Meine Partien". */
  list(fide: string | null, q: string | null, page: number, mine = false): Promise<ClubList> {
    const params = mine ? this.params(fide, q, page).set('mine', 'true') : this.params(fide, q, page);
    return firstValueFrom(this.http.get<ClubList>(`${this.base}/games`, { params }));
  }

  /** Eine Partie mit PGN und Stand der Analyse (angemeldet, `league.view`). */
  game(id: number): Promise<ClubGameDetail> {
    return firstValueFrom(this.http.get<ClubGameDetail>(`${this.base}/games/${id}`));
  }

  /** Adresse der Bewertungen aus der Hintergrund-Analyse — für RookHubs Rückblick (`GameReviewComponent`). */
  evalsUrl(id: number): string {
    return `${this.base}/games/${id}/evals`;
  }

  pgn(fide: string | null, q: string | null): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/games/pgn`, { params: this.params(fide, q), responseType: 'blob' }));
  }

  /** `draftId` (angemeldet): gehört zu diesem Entwurf — ein Verwalter, der ihn fertigstellt, bekommt die Vorgaben des Einreichers. */
  preview(pgn: string, draftId?: number | null): Promise<ClubPreview> {
    return firstValueFrom(this.http.post<ClubPreview>(`${this.base}/games/preview`, this.withDraft({ pgn }, draftId)));
  }

  /** `draftId` (angemeldet): die Partien tragen dann den Einreicher als Hochladenden, nicht den Verwalter. */
  importPgn(pgn: string, games: ImportGameDecision[], draftId?: number | null): Promise<ClubImportResult> {
    return firstValueFrom(this.http.post<ClubImportResult>(`${this.base}/games/import`, this.withDraft({ pgn, games }, draftId)));
  }

  private withDraft<T extends object>(body: T, draftId?: number | null): T & { draftId?: number } {
    return draftId != null && !this.anonymous ? { ...body, draftId } : body;
  }

  // ── Entwürfe (0.595.0): jede eingereichte Partieliste liegt sofort online ──────────────────

  private withRef<T extends { id: number; key?: string | null }>(d: T): T & { ref: string } {
    return { ...d, ref: this.anonymous ? d.key ?? '' : String(d.id) };
  }

  async createDraft(pgn: string, source: string | null, label: string | null): Promise<ClubDraft> {
    return this.withRef(await firstValueFrom(this.http.post<ClubDraft>(`${this.base}/drafts`, { pgn, source, label })));
  }

  /** Die eigenen offenen Entwürfe — ohne Konto die zu den Schlüsseln, die sich der Browser gemerkt hat. */
  async drafts(keys: string[] = []): Promise<ClubDraft[]> {
    if (this.anonymous) {
      if (!keys.length) return [];
      const r = await firstValueFrom(this.http.post<ClubDraft[]>(`${this.base}/drafts/lookup`, { keys }));
      return r.map(d => this.withRef(d));
    }
    return (await firstValueFrom(this.http.get<ClubDraft[]>(`${this.base}/drafts`))).map(d => this.withRef(d));
  }

  async draft(ref: string): Promise<ClubDraftDetail> {
    return this.withRef(await firstValueFrom(this.http.get<ClubDraftDetail>(`${this.base}/drafts/${encodeURIComponent(ref)}`)));
  }

  saveDraft(ref: string, body: { state?: string; imported?: number[] }): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.base}/drafts/${encodeURIComponent(ref)}`, body));
  }

  deleteDraft(ref: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/drafts/${encodeURIComponent(ref)}`));
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

  /** Namen und Ergebnis einer gespeicherten Partie korrigieren (angemeldet; wer löschen darf, darf korrigieren). */
  updateGame(id: number, body: ClubGameUpdate): Promise<ClubGame> {
    return firstValueFrom(this.http.put<ClubGame>(`${this.base}/games/${id}`, body));
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

  /** Eine ChessBase-Datenbank (die Dateien, einzeln gepackt, oder ein ZIP) → PGN; gespeichert wird dabei nichts. */
  chessBase(files: { name: string; blob: Blob }[]): Promise<ChessBaseResult> {
    const form = new FormData();
    for (const f of files) form.append('files', f.blob, f.name);
    return firstValueFrom(this.http.post<ChessBaseResult>(`${this.base}/games/chessbase`, form));
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

  // ── Stapel-Upload (0.651.0): nur ablegen, nicht einlesen ──

  batchStart(comment: string): Promise<BatchState> {
    return firstValueFrom(this.http.post<BatchState>(`${this.base}/batches`, { comment: comment.trim() || null }));
  }

  batchFile(key: string, file: File): Promise<BatchState> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<BatchState>(`${this.base}/batches/${encodeURIComponent(key)}/files`, form));
  }

  batchFinish(key: string): Promise<BatchState> {
    return firstValueFrom(this.http.post<BatchState>(`${this.base}/batches/${encodeURIComponent(key)}/finish`, {}));
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
/** Das Nötige einer Partie aus RookHubs „Meine Partien" (`GET /api/games/{id}`); `shareToken` = öffentlicher Link `/g/…`. */
export interface SavedGameRef { pgn: string; white: string | null; black: string | null; shareToken: string }

@Injectable({ providedIn: 'root' })
export class ClubApiService {
  private readonly http = inject(HttpClient);

  client(share: string | null = null): ClubClient {
    return new ClubClient(this.http, share);
  }

  /** Alle offenen Entwürfe von PGN-Importen (Verwalter, 0.595.0) — zum Fertigstellen, wenn jemand abgebrochen hat. */
  async allDrafts(): Promise<ClubDraft[]> {
    const list = await firstValueFrom(this.http.get<ClubDraft[]>('/api/league/club/admin/drafts'));
    return list.map(d => ({ ...d, ref: String(d.id) }));
  }

  /** Alle offenen Liga-Einlesungen (Verwalter) — was hochgeladen, aber nie geprüft wurde, hängt sonst im Limbo. */
  openScans(): Promise<OpenScan[]> {
    return firstValueFrom(this.http.get<OpenScan[]>('/api/league/club/admin/scans'));
  }

  /** Eine geprüfte Partie in RookHubs „Meine Partien" (dasselbe Konto) — `POST /api/games/import`. */
  addToMyGames(pgn: string): Promise<{ imported: number; duplicates: number; ids: number[] }> {
    return firstValueFrom(this.http.post<{ imported: number; duplicates: number; ids: number[] }>('/api/games/import', { pgn }));
  }

  /** Eine eigene Partie aus RookHub (⋮ → „In die Vereins-Datenbank" auf der Partieseite) — dieselbe API, dasselbe Konto. */
  savedGame(id: number): Promise<SavedGameRef> {
    return firstValueFrom(this.http.get<SavedGameRef>(`/api/games/${id}`));
  }
}
