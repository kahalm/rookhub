import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, Subject } from 'rxjs';
import { BookPuzzleDto } from '../puzzles/puzzle.service';

export type CourseMode = 'sequential' | 'random';

export interface CourseListItem {
  bookId: number;
  fileName: string;
  displayName: string;
  difficulty: string | null;
  rating: number | null;
  tags: string | null;
  description: string | null;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
  lastMode: string | null;
  /** ISO-Zeitstempel der letzten Verwendung (CourseProgress.UpdatedAt); null = noch nie angefangen. */
  lastActivityAt: string | null;
  /** true = eigener (selbst importierter) Chessable-Kurs; false = über eine Gruppe freigegebener öffentlicher Kurs. */
  isOwned: boolean;
}

export interface CourseChapter {
  index: number;
  /** null = Sammelgruppe „ohne Kapitel". */
  name: string | null;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
}

/** Statistik eines Kurs-Bereichs (ganzes Buch ODER aktuelles Kapitel): Fortschritt + Zeit + Erst-Versuch-Trefferquote.
 *  Zeit/Trefferquote zählen nur Versuche seit dem letzten Reset. */
export interface CourseScopeStats {
  solvedCount: number;
  total: number;
  progressPercent: number;
  /** Akkumulierte Zeit über alle Versuche (seit letztem Reset), Sekunden. */
  totalSeconds: number;
  /** Puzzles mit mindestens einem Versuch (seit Reset). */
  attemptedCount: number;
  /** Davon beim ERSTEN Versuch korrekt. */
  firstTryCorrect: number;
  /** 0–100: firstTryCorrect / attemptedCount. */
  accuracyPercent: number;
}

export interface CourseNextPuzzle {
  puzzle: BookPuzzleDto | null;
  solvedCount: number;
  total: number;
  completed: boolean;
  /** Statistik fürs ganze Buch. */
  book?: CourseScopeStats | null;
  /** Statistik fürs Kapitel des aktuellen Puzzles; null = Buch hat nur ein Kapitel / kein aktuelles Puzzle. */
  chapter?: CourseScopeStats | null;
  chapterName?: string | null;
}

export interface CourseProgress {
  bookId: number;
  solvedCount: number;
  total: number;
  progressPercent: number;
  completed: boolean;
  lastMode: string | null;
  book?: CourseScopeStats | null;
  chapter?: CourseScopeStats | null;
  chapterName?: string | null;
}

/** Status der Aufbereitungs-Versionierung (Kurse/Repertoires) — Basis für den „Aktualisieren (N)"-Knopf. */
export interface ReprocessStatus {
  currentVersion: number;
  total: number;
  stale: number;
  reprocessableLocally: number;
  refetchable: number;
  needsReimport: number;
}

/** Ergebnis eines Reprocess-Laufs. */
export interface ReprocessResult {
  reprocessed: number;
  updatedLines: number;
  enqueued: number;
  skipped: number;
}

@Injectable({ providedIn: 'root' })
export class CourseService {
  constructor(private http: HttpClient) {}

  /** Feuert, wenn sich der Kurs-Zugriff geändert haben könnte (z. B. nach einem Buch-Import) —
   *  die Navbar prüft daraufhin neu, ob das „Kurse"-Menü gezeigt wird. */
  private readonly accessChanged = new Subject<void>();
  readonly accessChanged$ = this.accessChanged.asObservable();
  notifyAccessChanged(): void { this.accessChanged.next(); }

  getCourses(): Observable<CourseListItem[]> {
    return this.http.get<CourseListItem[]>('/api/courses');
  }

  /** Lädt ein PGN als persönlichen Kurs hoch (nur für den Nutzer sichtbar). */
  uploadCourse(file: File, name?: string): Observable<CourseListItem> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (name && name.trim()) form.append('name', name.trim());
    return this.http.post<CourseListItem>('/api/courses/upload', form);
  }

  /** Löscht einen eigenen Kurs des Nutzers. */
  deleteCourse(bookId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}`);
  }

  /** Alle Puzzles eines Buchs (für das Offline-Speichern des ganzen Buchs). */
  getBookPuzzles(bookId: number): Observable<BookPuzzleDto[]> {
    return this.http.get<BookPuzzleDto[]>(`/api/courses/${bookId}/puzzles`);
  }

  /** Lädt das Buch als PGN (ein Spiel je Linie). */
  downloadPgn(bookId: number): Observable<Blob> {
    return this.http.get(`/api/courses/${bookId}/pgn`, { responseType: 'blob' });
  }

  /** Hat der eingeloggte User Zugriff auf mindestens einen Kurs? (Menü-Sichtbarkeit) */
  checkAccess(): Observable<{ hasAccess: boolean }> {
    return this.http.get<{ hasAccess: boolean }>('/api/courses/access');
  }

  /** Kapitel eines Buchs in Lesereihenfolge inkl. Fortschritt (für die Kapitelübersicht). */
  getChapters(bookId: number): Observable<CourseChapter[]> {
    return this.http.get<CourseChapter[]>(`/api/courses/${bookId}/chapters`);
  }

  getNext(bookId: number, mode: CourseMode, after?: number, exclude?: number, chapterIndex?: number): Observable<CourseNextPuzzle> {
    let params = new HttpParams().set('mode', mode);
    if (after != null) params = params.set('after', after);
    if (exclude != null) params = params.set('exclude', exclude);
    if (chapterIndex != null) params = params.set('chapterIndex', chapterIndex);
    return this.http.get<CourseNextPuzzle>(`/api/courses/${bookId}/next`, { params });
  }

  recordResult(bookId: number, bookPuzzleId: number, solved: boolean, mode?: CourseMode, timeSeconds = 0, chapterIndex?: number, hintsUsed = 0): Observable<CourseProgress> {
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/results`, { bookPuzzleId, solved, mode, timeSeconds, chapterIndex, hintsUsed });
  }

  reset(bookId: number): Observable<CourseProgress> {
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/reset`, {});
  }

  /** Merkt eine sequenziell durchgeklickte Info-/Erklärlinie — beim nächsten Wiedereinstieg
   *  startet der Kurs dahinter statt sie erneut zu zeigen. */
  markInfoSeen(bookId: number, bookPuzzleId: number): Observable<void> {
    return this.http.post<void>(`/api/courses/${bookId}/info-seen`, { bookPuzzleId });
  }

  /** Wie viele (verwaltbare) Kurse müssen wegen einer neueren Aufbereitungs-Pipeline neu aufbereitet werden? */
  reprocessStatus(): Observable<ReprocessStatus> {
    return this.http.get<ReprocessStatus>('/api/courses/reprocess/status');
  }

  /** Bereitet alle veralteten Kurse neu auf (lokal bzw. Chessable-Re-Fetch im Hintergrund). */
  reprocess(): Observable<ReprocessResult> {
    return this.http.post<ReprocessResult>('/api/courses/reprocess', {});
  }
}
