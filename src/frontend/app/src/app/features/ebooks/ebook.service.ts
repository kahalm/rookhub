import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Observable, map, of, shareReplay, tap } from 'rxjs';

/** Eine Datei (ein Format) eines E-Books. */
export interface EbookFile {
  id: number;
  format: string;
  fileName: string;
  sizeBytes: number;
}

export interface EbookProgress {
  page: number;
  pageCount: number | null;
  /** EPUB: CFI der Lesestelle. */
  locator?: string | null;
  updatedAt: string;
}

/** Ein E-Book in Übersicht und Leser (Server: `EbookDto`). */
export interface Ebook {
  id: number;
  title: string;
  author: string;
  year: number | null;
  tags: string[];
  description: string | null;
  targetMinElo: number | null;
  source: 'upload' | 'library';
  pageCount: number | null;
  hasCover: boolean;
  /** Stand der Diagramm-Erkennung: `pending` / `done` / `failed` / `unsupported` (Scan ohne Schach-Font). */
  diagramState: 'pending' | 'done' | 'failed' | 'unsupported';
  diagramCount: number;
  conversionState?: 'notneeded' | 'pending' | 'done' | 'failed';
  ownerUserId: number | null;
  ownerName: string | null;
  isOwner: boolean;
  canManage: boolean;
  /** Nur gefüllt, wenn `canManage`. */
  groupIds: number[] | null;
  files: EbookFile[];
  primaryFormat: string | null;
  progress: EbookProgress | null;
  createdAt: string;
  updatedAt: string;
}

/** Ein erkanntes Diagramm: Lage als Anteil der Seite (0–1, Ursprung links oben), FEN, Nummer, Überschrift. */
export interface EbookDiagram {
  id: number;
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
  fen: string;
  label: string | null;
  caption: string | null;
  sideKnown: boolean;
  reportedByMe: boolean;
  /** Nur für Verwalter gefüllt. */
  reportCount: number;
}

export interface EbookDiagramList {
  state: Ebook['diagramState'];
  items: EbookDiagram[];
}

export interface EbookTagFacet { tag: string; count: number; }

export interface EbookList {
  items: Ebook[];
  total: number;
  page: number;
  pageSize: number;
  tags: EbookTagFacet[];
  uploadEnabled: boolean;
  quotaUsedBytes: number;
  quotaLimitBytes: number | null;
  maxFileBytes: number;
  canManageAll: boolean;
  canImportLibrary: boolean;
}

export type EbookSort = 'title' | 'newest' | 'recent';

export interface EbookQuery {
  search?: string;
  tag?: string | null;
  mine?: boolean;
  /** Nur Bücher mit erkannten Diagrammen. */
  withDiagrams?: boolean;
  sort?: EbookSort;
  page?: number;
  pageSize?: number;
}

/** Metadaten + Freigabe (PUT: nur gesetzte Felder wirken). */
export interface EbookMeta {
  title?: string;
  author?: string;
  year?: number | null;
  tags?: string[];
  description?: string;
  targetMinElo?: number | null;
  groupIds?: number[];
}

export interface EbookGroupOption { id: number; name: string; isEveryone: boolean; }

export interface EbookLibraryImportResult {
  configured: boolean;
  filesSeen: number;
  added: number;
  filesAdded: number;
  skipped: number;
  unreadable: number;
}

/** Zugelassene Endungen — Spiegel von `EbookFormats.Allowed` im Backend (für das Dateifeld des Dialogs). */
export const EBOOK_ACCEPT = '.pdf,.epub,.djvu,.cbz,.cbr,.mobi,.azw3';

/** Formate, die der Browser zeigt (alles andere ist Download). */
export const READABLE_FORMATS: ReadonlySet<string> = new Set(['pdf', 'epub']);

/** „1,2 MB" — Bytes lesbar, ohne Nachkommastelle unter 10 in der Einheit. */
export function formatBytes(bytes: number, lang: string = 'en'): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '';
  const units = ['B', 'kB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1000 && unit < units.length - 1) { value /= 1000; unit++; }
  const digits = unit === 0 ? 0 : value < 10 ? 1 : 0;
  return `${value.toLocaleString(lang, { maximumFractionDigits: digits, minimumFractionDigits: digits })} ${units[unit]}`;
}

/** Wie viel Vorlauf der Ausweis haben soll, bevor er neu geholt wird (er lebt 12 h). */
const TOKEN_MARGIN_MS = 5 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class EbookService {
  private http = inject(HttpClient);

  private token: { value: string; expiresAt: number } | null = null;
  private tokenRequest: Observable<string> | null = null;

  list(q: EbookQuery = {}): Observable<EbookList> {
    let params = new HttpParams();
    if (q.search) params = params.set('search', q.search);
    if (q.tag) params = params.set('tag', q.tag);
    if (q.mine) params = params.set('mine', 'true');
    if (q.withDiagrams) params = params.set('withDiagrams', 'true');
    if (q.sort) params = params.set('sort', q.sort);
    if (q.page) params = params.set('page', String(q.page));
    if (q.pageSize) params = params.set('pageSize', String(q.pageSize));
    return this.http.get<EbookList>('/api/ebooks', { params });
  }

  get(id: number): Observable<Ebook> {
    return this.http.get<Ebook>(`/api/ebooks/${id}`);
  }

  diagrams(id: number): Observable<EbookDiagramList> {
    return this.http.get<EbookDiagramList>(`/api/ebooks/${id}/diagrams`);
  }

  /** „Diagramm falsch" melden bzw. zurücknehmen. */
  reportDiagram(ebookId: number, diagramId: number, reported: boolean): Observable<{ reported: boolean }> {
    const url = `/api/ebooks/${ebookId}/diagrams/${diagramId}/report`;
    return reported ? this.http.post<{ reported: boolean }>(url, {}) : this.http.delete<{ reported: boolean }>(url);
  }

  groupOptions(): Observable<EbookGroupOption[]> {
    return this.http.get<EbookGroupOption[]>('/api/ebooks/groups');
  }

  /** Hochladen mit Fortschritt (Ereignisse, weil ein Buch Minuten brauchen kann). */
  upload(file: File, meta: EbookMeta): Observable<HttpEvent<Ebook>> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (meta.title) form.append('title', meta.title);
    if (meta.author) form.append('author', meta.author);
    if (meta.year != null) form.append('year', String(meta.year));
    if (meta.tags?.length) form.append('tags', meta.tags.join(','));
    if (meta.description) form.append('description', meta.description);
    if (meta.targetMinElo != null) form.append('targetMinElo', String(meta.targetMinElo));
    if (meta.groupIds) form.append('groupIds', meta.groupIds.join(','));
    return this.http.post<Ebook>('/api/ebooks', form, { reportProgress: true, observe: 'events' });
  }

  update(id: number, meta: EbookMeta): Observable<Ebook> {
    return this.http.put<Ebook>(`/api/ebooks/${id}`, meta);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/ebooks/${id}`);
  }

  setGroups(ebookIds: number[], groupIds: number[]): Observable<{ updated: number }> {
    return this.http.put<{ updated: number }>('/api/ebooks/groups', { ebookIds, groupIds });
  }

  setProgress(id: number, page: number, pageCount: number | null, locator?: string | null): Observable<EbookProgress> {
    return this.http.put<EbookProgress>(`/api/ebooks/${id}/progress`, { page, pageCount, locator: locator ?? null });
  }

  importLibrary(): Observable<EbookLibraryImportResult> {
    return this.http.post<EbookLibraryImportResult>('/api/admin/ebooks/library/import', {});
  }

  /**
   * Der Ausweis für Datei und Titelbild (`?t=`): einmal geholt, bis kurz vor Ablauf wiederverwendet; parallele
   * Anfragen teilen sich EINE Server-Anfrage.
   */
  accessToken(): Observable<string> {
    if (this.token && this.token.expiresAt - Date.now() > TOKEN_MARGIN_MS) return of(this.token.value);
    if (!this.tokenRequest) {
      this.tokenRequest = this.http.get<{ token: string; expiresAt: string }>('/api/ebooks/access-token').pipe(
        map(r => {
          this.token = { value: r.token, expiresAt: Date.parse(r.expiresAt) };
          return r.token;
        }),
        tap({ finalize: () => { this.tokenRequest = null; } }),
        shareReplay(1),
      );
    }
    return this.tokenRequest;
  }

  /** Vergessen (z. B. nach einem 401 an der Datei) — der nächste Aufruf holt einen frischen. */
  forgetToken(): void {
    this.token = null;
    this.tokenRequest = null;
  }

  fileUrl(id: number, token: string, opts: { fileId?: number; download?: boolean } = {}): string {
    let url = `/api/ebooks/${id}/file?t=${encodeURIComponent(token)}`;
    if (opts.fileId) url += `&fileId=${opts.fileId}`;
    if (opts.download) url += '&download=true';
    return url;
  }

  coverUrl(id: number, token: string, version: string | number = ''): string {
    return `/api/ebooks/${id}/cover?t=${encodeURIComponent(token)}${version ? `&v=${encodeURIComponent(String(version))}` : ''}`;
  }
}
