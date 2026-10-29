import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Observable, distinctUntilChanged, map, of, shareReplay, skip, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';

/** Eine Datei (ein Format) eines E-Books. */
export interface EbookFile {
  id: number;
  format: string;
  fileName: string;
  sizeBytes: number;
  /** Abgeleitete Lesefassung (z. B. aus DJVU umgewandeltes PDF). */
  derived?: boolean;
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
  /** Startmodus des Bretts neben dem Buch (0.781.0); der Server liefert immer einen, Standard „solve“. */
  defaultBoardMode?: 'view' | 'free' | 'calc' | 'solve' | 'maia';
  source: 'upload' | 'library';
  pageCount: number | null;
  hasCover: boolean;
  /** Stand der Diagramm-Erkennung: `pending` / `done` / `failed` / `unsupported` (Scan ohne Schach-Font). */
  diagramState: 'pending' | 'done' | 'failed' | 'unsupported';
  diagramCount: number;
  /** Hintergrundschritte laufen noch (Titelbild, Umwandlung, Diagramme, Prüfung). */
  processing?: boolean;
  /** Sammlung des Buchs (0.787.0), z. B. alle Hefte einer Zeitschrift. */
  collection?: EbookCollection | null;
  /** Dieser Eintrag steht in der Liste für seine ganze Sammlung (Ordner-Karte). */
  collectionStub?: boolean;
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
  /** Sprache des Buchtexts (0.876.0, ISO 639-1); `null` = nicht bestimmt/unklar. */
  language?: string | null;
  /** Fertige Übersetzungen (Zielsprachen, z. B. `['en']`) — der Leser bietet „Original / English“ an. */
  translations?: string[];
  createdAt: string;
  updatedAt: string;
  /** Nur in der Antwort des Uploads (0.863.0): vorhandene Bücher, die wahrscheinlich dasselbe sind. */
  possibleDuplicates?: EbookDuplicate[] | null;
}

/** Ein vorhandenes Buch, das ein Upload womöglich doppelt (0.863.0; Server: `EbookDuplicateDto`). */
export interface EbookDuplicate {
  id: number;
  title: string;
  author: string;
  year: number | null;
  formats: string[];
  /** `file` gleiche Datei, `isbn` gleiche ISBN, `text` fast gleicher Text, `title` gleicher Titel + Autor. */
  reason: 'file' | 'isbn' | 'text' | 'title';
  /** Text-Ähnlichkeit 0–1. */
  similarity: number;
}

/** Fehlercode „dieses Buch gibt es schon“ (409, 0.863.0) — `error.details.books` nennt es. */
export const EBOOK_DUPLICATE_BOOK = 'ebook_duplicate_book';

/** Die vorhandenen Bücher aus einem 409 (`ebook_duplicate_book` oder `ebook_duplicate_file`), sonst `null`. */
export function duplicateBooksOf(err: unknown): { code: string; books: EbookDuplicate[] } | null {
  const e = (err as { status?: number; error?: { code?: unknown; details?: { books?: unknown } } } | null);
  if (e?.status !== 409) return null;
  const code = e.error?.code;
  if (code !== EBOOK_DUPLICATE_BOOK && code !== 'ebook_duplicate_file') return null;
  const books = e.error?.details?.books;
  return { code, books: Array.isArray(books) ? books as EbookDuplicate[] : [] };
}

/** Ein erkanntes Diagramm: Lage als Anteil der Seite (0–1, Ursprung links oben), FEN, Nummer, Überschrift. */
/** EPUB-Stelle eines Doppelklicks ohne erkanntes Diagramm (0.798.0): Bildpfad im Buch (`null` = Text) und Kapitel (Spine-href). */
/** Doppelklick-Stelle im EPUB: Bildpfad (sonst Text), Kapitel und — im Fließtext-EPUB aus einem PDF — die Druckseite. */
export interface EpubMissedSpot { ref: string | null; chapter: string | null; page?: number | null; }

/**
 * Druckseite einer Stelle im Fließtext-EPUB (aus einem PDF umgewandelt): der letzte Seitenanker davor — Absatz mit
 * `id="p17"` oder Seitenbild `…/p0017-2.svg`. Echte EPUBs haben keine solchen Anker → `null`.
 */
export function epubPageAt(target: Element): number | null {
  const doc = target.ownerDocument;
  if (!doc) return null;
  let page: number | null = null;
  for (const el of Array.from(doc.querySelectorAll('[id^="p"], img, image'))) {
    const pos = el.compareDocumentPosition(target);
    const before = el === target || (pos & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 || (pos & Node.DOCUMENT_POSITION_CONTAINED_BY) !== 0;
    if (!before) break;
    const id = /^p(\d+)$/.exec(el.id ?? '');
    const src = /(?:^|\/)p(\d{4})-\d+\.\w+$/.exec(el.getAttribute('src') ?? el.getAttribute('xlink:href') ?? el.getAttribute('href') ?? '');
    const n = id ? +id[1] : src ? +src[1] : null;
    if (n) page = n;
  }
  return page;
}

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
  /** EPUB (0.775.0): Bildpfad im Buch; `page` ist dann das Kapitel (Spine, 1-basiert). PDF: `null`. */
  imageRef?: string | null;
  /** EPUB-Kapitel eines aus dem PDF erkannten, mit dem EPUB verknüpften Diagramms (0.793.0). */
  epubChapter?: number | null;
  /** Lösung aus dem Buch: Hauptlinie (UCI/SAN, leerzeichengetrennt), Absatz, Seite — `null`, wenn keine gefunden. */
  solutionUci?: string | null;
  solutionSan?: string | null;
  solutionText?: string | null;
  solutionPage?: number | null;
  /** Woher die Lösung stammt (`book` / `engine`) und was Stockfish dazu sagt. */
  solutionSource?: 'book' | 'engine' | null;
  solutionCheck?: 'unchecked' | 'confirmed' | 'doubtful' | 'replaced' | 'engine' | 'nopuzzle';
  /** Stockfish-Bewertung für die Seite am Zug (cp; Matt in n = ±(10000 − n)). */
  engineEval?: number | null;
  reportedByMe: boolean;
  /** Nur für Verwalter gefüllt. */
  reportCount: number;
  /** Partiensammlungen (0.831.0): zu diesem Diagramm ist eine Partie hinterlegt (Modus „Partie“). Fehlt bei alten Antworten. */
  hasGame?: boolean;
}

/** Die Partie zu einem Diagramm (0.831.0): Kopf, Hauptlinie (SAN) und der Halbzug der Diagrammstellung. */
export interface EbookGame {
  id: number;
  white?: string | null;
  black?: string | null;
  event?: string | null;
  date?: string | null;
  result?: string | null;
  moves: string[];
  ply: number;
}

export interface EbookDiagramList {
  state: Ebook['diagramState'];
  items: EbookDiagram[];
}

export interface EbookTagFacet { tag: string; count: number; }

/** Sammlung (0.787.0): mehrere Bücher als ein Ordner in der Liste. */
export interface EbookCollection { id: number; name: string; bookCount: number; }

export interface EbookList {
  items: Ebook[];
  /** Die geöffnete Sammlung (bei `collection` in der Anfrage). */
  collection?: EbookCollection | null;
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

/** Einzelne Namen aus dem Autorenfeld („Lev Alburt, Sam Palatnik“ → zwei Namen) — für die Autor-Links (0.796.0). */
export function ebookAuthorNames(author: string | null | undefined): string[] {
  return (author ?? '').split(/\s*(?:,|;|&|\band\b|\bund\b)\s*/).map(n => n.trim()).filter(n => n.length > 1);
}

export interface EbookQuery {
  search?: string;
  tag?: string | null;
  /** Nur Bücher dieses Autors (Klick auf den Namen). */
  author?: string | null;
  mine?: boolean;
  /** Elo-Spanne (0.822.0): Mindest-Elo des Buchs von … bis …; mit einer Grenze fallen Bücher ohne Elo heraus. */
  minElo?: number | null;
  maxElo?: number | null;
  /** Nur die Bücher dieser Sammlung; ohne erscheinen Sammlungen als je ein Ordner. */
  collection?: number | null;
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
  defaultBoardMode?: string;
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

/** Buch-Übersetzung (0.876.0): Übersicht für Verwalter (Server: `EbookTranslationStatusDto`). */
export interface EbookTranslationItem {
  ebookId: number;
  title: string;
  language: string;
  sourceLanguage: string | null;
  /** `pending` / `running` / `done` / `failed` / `notext`; `queued` = in der Reihe, noch ohne Lauf. */
  state: string;
  priority: boolean;
  segmentsDone: number;
  segmentsTotal: number;
  segmentsFailed: number;
  error: string | null;
  completedAt: string | null;
}

export interface EbookTranslationStatus {
  enabled: boolean;
  modelAvailable: boolean;
  languages: string[];
  quietNow: boolean;
  quietUntil: string | null;
  running: { ebookId: number; title: string; language: string; sourceLanguage: string | null; segmentsDone: number; segmentsTotal: number; startedAt: string; etaMinutes: number | null } | null;
  /** Bücher je Quellsprache; `und` = unklar/gemischt, `notext` = ohne Text, `pending` = noch nicht geprüft. */
  bookLanguages: { language: string; books: number }[];
  targets: { language: string; done: number; pending: number; failed: number; noText: number; remaining: number; remainingFirstRound: number }[];
  failed: EbookTranslationItem[];
  next: EbookTranslationItem[];
}

/** Gemerkte Lesesprache je Gerät (`original` oder Zielsprache wie `en`). */
export const EBOOK_READ_LANG_KEY = 'rh.ebooks.readLang';

/**
 * Lesestelle beim Wechsel Original ↔ Übersetzung: dieselbe Kapitel- und Elementstruktur, aber andere Textlängen — der
 * Zeichenversatz am Ende einer CFI (`…/4/2:120)`) zeigte ins Leere. Ohne ihn landet epub.js am Anfang desselben Absatzes.
 */
export function coarseCfi(cfi: string | null | undefined): string | null {
  if (!cfi) return null;
  return cfi.replace(/:\d+(\[[^\]]*\])?(?=\)$)/, '').replace(/,[^,)]*,[^,)]*(?=\)$)/, '');
}


/**
 * „1,2 MB" — Bytes lesbar, ohne Nachkommastelle unter 10 in der Einheit. Binär gerechnet (÷ 1024, Einheiten wie im
 * Betriebssystem „kB/MB/GB“): die Upload-Grenze des Servers ist 300 MiB und soll überall als „300 MB“ erscheinen,
 * nicht als „315 MB“ neben einem Hinweis „bis 300 MB“ (UI-Sweep c2-eb-upload-limit-315-300).
 */
export function formatBytes(bytes: number, lang: string = 'en'): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '';
  const units = ['B', 'kB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
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

  constructor() {
    // Der Ausweis gehört zu genau einem Aufrufer (Konto oder anonyme Kennung, 0.778.0): bei An-/Abmelden oder
    // Kontowechsel vergessen, sonst sähe ein Abgemeldeter am selben Gerät noch die Bücher des Kontos.
    inject(AuthService).currentUser$.pipe(map(u => u?.userId ?? null), distinctUntilChanged(), skip(1))
      .subscribe(() => this.forgetToken());
  }

  list(q: EbookQuery = {}): Observable<EbookList> {
    let params = new HttpParams();
    if (q.search) params = params.set('search', q.search);
    if (q.tag) params = params.set('tag', q.tag);
    if (q.author) params = params.set('author', q.author);
    if (q.mine) params = params.set('mine', 'true');
    if (q.minElo != null) params = params.set('minElo', String(q.minElo));
    if (q.maxElo != null) params = params.set('maxElo', String(q.maxElo));
    if (q.collection) params = params.set('collection', String(q.collection));
    if (q.sort) params = params.set('sort', q.sort);
    if (q.page) params = params.set('page', String(q.page));
    if (q.pageSize) params = params.set('pageSize', String(q.pageSize));
    return this.http.get<EbookList>('/api/ebooks', { params });
  }

  /** Sammlungen (nur Verwalter). */
  collections(): Observable<EbookCollection[]> {
    return this.http.get<EbookCollection[]>('/api/ebooks/collections');
  }

  /** Bücher einer bestehenden (`collectionId`) oder neuen (`newName`) Sammlung zuordnen; beides leer = herausnehmen.
   *  `additional` (0.861.0): zusätzlich zur bisherigen Sammlung; `fromCollectionId`: beim Herausnehmen nur aus dieser. */
  assignCollection(ebookIds: number[], collectionId: number | null, newName: string | null,
                   opts: { additional?: boolean; fromCollectionId?: number | null } = {}): Observable<EbookCollection | null> {
    return this.http.put<EbookCollection | null>('/api/ebooks/collections/assign',
      { ebookIds, collectionId, newName, additional: !!opts.additional, fromCollectionId: opts.fromCollectionId ?? null });
  }

  renameCollection(id: number, name: string): Observable<EbookCollection> {
    return this.http.put<EbookCollection>(`/api/ebooks/collections/${id}`, { name });
  }

  /** Sammlung auflösen — die Bücher bleiben. */
  deleteCollection(id: number): Observable<void> {
    return this.http.delete<void>(`/api/ebooks/collections/${id}`);
  }

  get(id: number): Observable<Ebook> {
    return this.http.get<Ebook>(`/api/ebooks/${id}`);
  }

  diagrams(id: number): Observable<EbookDiagramList> {
    return this.http.get<EbookDiagramList>(`/api/ebooks/${id}/diagrams`);
  }

  /** Die Partie zu einem Diagramm (0.831.0); 404 = keine hinterlegt. */
  diagramGame(id: number, diagramId: number): Observable<EbookGame> {
    return this.http.get<EbookGame>(`/api/ebooks/${id}/diagrams/${diagramId}/game`);
  }

  /** „Ist hier ein Diagramm, das nicht erkannt wurde?“ — Antwort zur späteren Auswertung (0.788.0). */
  reportMissedDiagram(id: number, page: number, x: number, y: number, isDiagram: boolean, epub?: EpubMissedSpot): Observable<void> {
    const body = epub
      ? { page: 0, x: 0, y: 0, isDiagram, epubRef: epub.ref, epubChapter: epub.chapter }
      : { page, x, y, isDiagram };
    return this.http.post<void>(`/api/ebooks/${id}/missed-diagram`, body);
  }

  /** „Stellung korrigieren“ (0.798.0, nur Verwalter des Buchs): neue FEN → das aktualisierte Diagramm. */
  correctDiagramPosition(ebookId: number, diagramId: number, fen: string): Observable<EbookDiagram> {
    return this.http.put<EbookDiagram>(`/api/ebooks/${ebookId}/diagrams/${diagramId}/position`, { fen });
  }

  /** „Als Kurs anlegen“: persönlicher Kurs aus den Diagrammen des Buchs → Id des neuen Kurses. */
  createCourse(id: number): Observable<{ bookId: number; displayName: string }> {
    return this.http.post<{ bookId: number; displayName: string }>(`/api/ebooks/${id}/course`, {});
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
  upload(file: File, meta: EbookMeta, forceNew = false): Observable<HttpEvent<Ebook>> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (meta.title) form.append('title', meta.title);
    if (meta.author) form.append('author', meta.author);
    if (meta.year != null) form.append('year', String(meta.year));
    if (meta.tags?.length) form.append('tags', meta.tags.join(','));
    if (meta.description) form.append('description', meta.description);
    if (meta.targetMinElo != null) form.append('targetMinElo', String(meta.targetMinElo));
    if (meta.defaultBoardMode) form.append('defaultBoardMode', meta.defaultBoardMode);
    if (meta.groupIds) form.append('groupIds', meta.groupIds.join(','));
    // „Trotzdem als neues Buch hochladen“ (0.863.0): ISBN-/Text-/Titelprüfung aus, Byte-Prüfung bleibt.
    if (forceNew) form.append('forceNew', 'true');
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

  translationStatus(): Observable<EbookTranslationStatus> {
    return this.http.get<EbookTranslationStatus>('/api/admin/ebooks/translations');
  }

  /** Ein Buch vorziehen (bzw. neu übersetzen lassen) — läuft beim nächsten Lauf außerhalb der Sperrzeit. */
  enqueueTranslation(id: number, lang: string): Observable<EbookTranslationItem> {
    return this.http.post<EbookTranslationItem>(`/api/admin/ebooks/${id}/translations/${encodeURIComponent(lang)}`, {});
  }

  retryFailedTranslations(): Observable<{ queued: number }> {
    return this.http.post<{ queued: number }>('/api/admin/ebooks/translations/retry-failed', {});
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

  /** Anonyme Uploads + Lesestände dieser Kennung ins Konto übernehmen (beim Anmelden, 0.778.0). Danach gehört der
   *  zwischengespeicherte Ausweis zum alten (anonymen) Aufrufer — weg damit. */
  claimSession(sessionId: string): Observable<{ claimed: number }> {
    this.forgetToken();
    return this.http.post<{ claimed: number }>('/api/ebooks/claim-session', { sessionId }).pipe(tap({ finalize: () => this.forgetToken() }));
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

  /** Basis des EPUB-Lesers „als Ordner“: einzelne Einträge des ZIPs, Ausweis im PFAD (epub.js löst relative Adressen
   *  von Kapiteln, Bildern und CSS dagegen auf und kann keine Query-Parameter anhängen). Endet mit „/“. */
  epubBaseUrl(id: number, token: string): string {
    return `/api/ebooks/${id}/epub/${encodeURIComponent(token)}/`;
  }

  /** Übersetztes EPUB als Datei (Rückfall „ganzes Archiv“ bzw. Download). */
  translatedFileUrl(id: number, lang: string, token: string, download = false): string {
    return `/api/ebooks/${id}/translations/${encodeURIComponent(lang)}/file?t=${encodeURIComponent(token)}${download ? '&download=true' : ''}`;
  }

  /** Wie {@link epubBaseUrl}, aber das übersetzte EPUB (gleiche Pfade im Archiv). */
  translatedEpubBaseUrl(id: number, lang: string, token: string): string {
    return `/api/ebooks/${id}/translations/${encodeURIComponent(lang)}/epub/${encodeURIComponent(token)}/`;
  }

  coverUrl(id: number, token: string, version: string | number = ''): string {
    return `/api/ebooks/${id}/cover?t=${encodeURIComponent(token)}${version ? `&v=${encodeURIComponent(String(version))}` : ''}`;
  }
}
