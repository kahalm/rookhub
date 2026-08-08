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
  /** true = vom Nutzer fürs Dashboard angepinnt (persönlich). */
  isPinned: boolean;
  /** true = dieser Kurs wurde von einem anderen Nutzer mit mir geteilt (ich bin nicht der Besitzer). */
  isShared?: boolean;
  /** Benutzername des Teilenden (nur wenn isShared) — für das „von X"-Badge. */
  sharedByUsername?: string | null;
  /** Verknüpfter Partner-Kurs (Buch↔Workbook) für den Schnellwechsel; null = keine Verknüpfung. */
  linkedBookId?: number | null;
  /** Anzeigename des verknüpften Kurses (nur wenn linkedBookId gesetzt). */
  linkedDisplayName?: string | null;
  /** Themen-Tags des Buchs (Keys opening/middlegame/endgame/tactics/other); leer/unset = ["tactics"].
   *  Steuern die Themen-Aufschlüsselung der Kurszeit im Trainingsfortschritt. */
  themes?: string[];
  /** true = Kalkulationsbuch (Stellungen ohne Lösung): Karte bietet statt sequenziell/zufällig den
   *  Kalkulations-Modus an; puzzleCount/solvedCount = alle bzw. bearbeitete Stellungen. */
  isCalculation?: boolean;
  /** Erreichte Punkte der Selbstbewertung im Kalkulations-Modus; null/fehlend = kein
   *  Kalkulationsbuch. 0 heißt „bewertet, aber keine Punkte" — deshalb nicht auf 0 zurückfallen. */
  calcPoints?: number | null;
  /** Erreichbare Punkte (4 je Stellung). Fehlt der Wert, rechnet die Karte ihn aus `puzzleCount`. */
  calcMaxPoints?: number | null;
}

/** Pro-Linien-Bearbeitungsstatus eines Kurs-Buchs (für die „Linien durchsehen"-Ansicht):
 *  gelöste (✓) und versucht-aber-nicht-gelöste (✗) Linien-Ids des Users. */
export interface CourseLineStatus {
  solvedIds: number[];
  failedIds: number[];
}

/** Der verknüpfte Partner-Kurs (leere Felder = keine Verknüpfung). */
export interface CourseLink {
  linkedBookId: number | null;
  linkedDisplayName: string | null;
}

/** Ergebnis eines Teilen-Vorgangs (Batch). */
export interface CourseShareResult {
  shared: number;
  skipped: { userId: number; reason: string }[];
}

/** Ein Nutzer, mit dem ein Kurs aktuell geteilt ist. */
export interface CourseShareRecipient {
  userId: number;
  username: string;
  displayName: string | null;
  sharedAt: string;
}

export interface CourseChapter {
  index: number;
  /** null = Sammelgruppe „ohne Kapitel". */
  name: string | null;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
  /** Anzahl Info-/Erklärlinien im Kapitel (nicht in puzzleCount enthalten); in der Übersicht in Klammern. */
  infoCount: number;
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

/**
 * Kapitel in der VERWALTUNGSSICHT der Detailseite — anders als {@link CourseChapter} sind hier ALLE
 * Kapitel enthalten, auch solche, die nur aus Stellungs-/Info-Linien bestehen (die Kalkulationsbücher).
 * Adressiert wird über `name`, nicht über einen Index (der verschiebt sich beim Anlegen).
 */
export interface CourseManageChapter {
  /** null = Sammelgruppe „ohne Kapitel". */
  name: string | null;
  /** Alle Linien des Kapitels. */
  lineCount: number;
  /** Davon abgefragte Quiz-Linien (mit Lösung). */
  quizCount: number;
  solvedCount: number;
  progressPercent: number;
  /** Index in der Solver-Kapitelliste; null = im Solver nicht startbar (keine Quiz-Linien). */
  solverIndex: number | null;
  /** Erste Linie des Kapitels — Einstieg für den Kalkulations-Modus. */
  firstLineId: number | null;
}

/** Vollbild der Kurs-Detailseite. */
export interface CourseDetail {
  bookId: number;
  fileName: string;
  displayName: string;
  description: string | null;
  difficulty: string | null;
  rating: number | null;
  minElo: number | null;
  maxElo: number | null;
  tags: string | null;
  themes: string[];
  kind: 'Puzzle' | 'Study';
  isCalculation: boolean;
  isPublic: boolean;
  publicSlug: string | null;
  isOwned: boolean;
  isShared: boolean;
  sharedByUsername: string | null;
  isPinned: boolean;
  /** Darf der Aufrufer Kapitel/Linien bearbeiten (Besitzer oder Admin)? */
  canManage: boolean;
  puzzleCount: number;
  solvedCount: number;
  progressPercent: number;
  totalLines: number;
  infoLineCount: number;
  /** Erreichte Punkte der Selbstbewertung im Kalkulations-Modus (nur bei Kalkulationsbüchern
   *  belegt; `null` = kein Kalkulationsbuch, 0 = bewertet ohne Punkte). */
  calcPoints?: number | null;
  /** Erreichbare Punkte (4 je Stellung); fehlt der Wert, wird er aus `puzzleCount` gerechnet. */
  calcMaxPoints?: number | null;
  lastMode: string | null;
  lastActivityAt: string | null;
  linkedBookId: number | null;
  linkedDisplayName: string | null;
  chapters: CourseManageChapter[];
  createdAt: string;
  updatedAt: string;
}

/** Eine Linie in der Verwaltungssicht (ohne Zugfolge — die Detailseite verrät keine Lösung). */
export interface CourseLine {
  id: number;
  lineId: string;
  round: string;
  title: string | null;
  chapter: string | null;
  fen: string;
  comment: string | null;
  isInfoOnly: boolean;
  /** Halbzüge der gespeicherten Linie (0 = reine Stellung). */
  moveCount: number;
}

/** Eine beim Einfügen verworfene Zeile (Grund invalid_fen/duplicate/too_many). */
export interface CourseLineIssue {
  lineNumber: number;
  text: string;
  reason: string;
}

export interface AddCourseLinesResult {
  added: number;
  chapter: string | null;
  issues: CourseLineIssue[];
  totalLines: number;
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

/**
 * Ergebnis eines Reprocess-Laufs — SPIEGEL des Server-DTOs, aber KEIN Antwort-Vertrag:
 * `POST /api/courses/reprocess` startet den Lauf im Hintergrund und antwortet mit
 * `202 { started: true }`. Die Zahlen hier landen also nur im Server-Log; der Typ
 * beschreibt die Struktur für den Fall, dass der Lauf später synchron auswertbar wird.
 * Wer auf `failed` reagieren will, braucht vorher einen Endpoint, der das ausliefert.
 */
export interface ReprocessResult {
  reprocessed: number;
  updatedLines: number;
  enqueued: number;
  /** Nichts zu tun (keine Quelle, Re-Fetch-Backoff, Dedup). */
  skipped: number;
  /** Mit Fehler abgebrochen — der Datensatz bleibt veraltet (siehe Server-Log). */
  failed: number;
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

  /** Teilt einen eigenen Kurs mit ausgewählten (befreundeten) Nutzern. */
  shareCourse(bookId: number, recipientUserIds: number[]): Observable<CourseShareResult> {
    return this.http.post<CourseShareResult>(`/api/courses/${bookId}/share`, { recipientUserIds });
  }

  /** Mit welchen Nutzern ist dieser eigene Kurs aktuell geteilt? */
  getShareRecipients(bookId: number): Observable<CourseShareRecipient[]> {
    return this.http.get<CourseShareRecipient[]>(`/api/courses/${bookId}/shares`);
  }

  /** Nimmt die Freigabe des eigenen Kurses für einen Empfänger zurück. */
  unshareCourse(bookId: number, recipientId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}/share/${recipientId}`);
  }

  /** Verknüpft diesen Kurs mit einem anderen (Buch↔Workbook) für den Schnellwechsel. */
  linkCourse(bookId: number, linkedBookId: number): Observable<void> {
    return this.http.post<void>(`/api/courses/${bookId}/link`, { linkedBookId });
  }

  /** Der aktuell verknüpfte Partner-Kurs (leere Felder = keine Verknüpfung). */
  getLink(bookId: number): Observable<CourseLink> {
    return this.http.get<CourseLink>(`/api/courses/${bookId}/link`);
  }

  /** Hebt die Verknüpfung dieses Kurses wieder auf. */
  unlinkCourse(bookId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}/link`);
  }

  /** Wandelt einen Kurs in ein neues Repertoire um (Original bleibt); liefert das neue Repertoire. */
  convertToRepertoire(bookId: number): Observable<{ id: number; name: string }> {
    return this.http.post<{ id: number; name: string }>(`/api/courses/${bookId}/convert-to-repertoire`, {});
  }

  /** Pinnt einen Kurs fürs Dashboard an (persönlich, idempotent). */
  pinCourse(bookId: number): Observable<void> {
    return this.http.post<void>(`/api/courses/${bookId}/pin`, {});
  }

  /** Löst einen angepinnten Kurs wieder vom Dashboard. */
  unpinCourse(bookId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}/pin`);
  }

  /** Setzt die Themen-Tags des Kurs-Buchs (Admin/Besitzer). Antwortet mit den effektiven Keys
   *  (leer → Default ["tactics"]). */
  setCourseThemes(bookId: number, themes: string[]): Observable<{ themes: string[] }> {
    return this.http.put<{ themes: string[] }>(`/api/courses/${bookId}/themes`, { themes });
  }

  /** Alle Puzzles eines Buchs (für das Offline-Speichern des ganzen Buchs). */
  getBookPuzzles(bookId: number): Observable<BookPuzzleDto[]> {
    return this.http.get<BookPuzzleDto[]>(`/api/courses/${bookId}/puzzles`);
  }

  /** Puzzles eines ÖFFENTLICHEN Kurses — ohne Login. Basis für das registrierungsfreie
   *  Durchspielen eines als „public" markierten Kurses (404, wenn nicht öffentlich).
   *  Optional seitenweise (`skip`/`take`): große Kurse laden die erste Seite sofort, den Rest
   *  im Hintergrund — ohne Parameter kommt (rückwärtskompatibel) das ganze Buch. */
  getPublicCourse(bookId: number, skip?: number, take?: number): Observable<BookPuzzleDto[]> {
    let params = new HttpParams();
    if (skip != null) params = params.set('skip', String(skip));
    if (take != null) params = params.set('take', String(take));
    return this.http.get<BookPuzzleDto[]>(`/api/courses/${bookId}/public`, { params });
  }

  /** Öffentlichen Kurz-Alias (z. B. „mate1") auf die BookId auflösen — ohne Login (404 = unbekannt). */
  resolvePublicSlug(slug: string): Observable<{ bookId: number }> {
    return this.http.get<{ bookId: number }>(`/api/courses/by-slug/${encodeURIComponent(slug)}`);
  }

  /** Pro-Linien-Bearbeitungsstatus (gelöst ✓ / versucht-aber-nicht-gelöst ✗) eines Buchs. */
  getLineStatus(bookId: number): Observable<CourseLineStatus> {
    return this.http.get<CourseLineStatus>(`/api/courses/${bookId}/line-status`);
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

  recordResult(bookId: number, bookPuzzleId: number, solved: boolean, mode?: CourseMode, timeSeconds = 0, chapterIndex?: number, hintsUsed = 0, solveMode?: string): Observable<CourseProgress> {
    // `mode` = Durchlaufart (sequential/random), `solveMode` = Spielweise ('training'/'easy');
    // fehlt letztere, fällt der Server auf 'training' zurück.
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/results`, { bookPuzzleId, solved, mode, timeSeconds, chapterIndex, hintsUsed, solveMode });
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

  // ---- Detailseite + Inhaltspflege ----------------------------------------

  /** Vollbild der Kurs-Detailseite (Metadaten, eigener Fortschritt, Kapitel-Verwaltungssicht). */
  getDetail(bookId: number): Observable<CourseDetail> {
    return this.http.get<CourseDetail>(`/api/courses/${bookId}`);
  }

  /**
   * Schaltet den Kalkulations-Modus des Kurses ein/aus (Besitzer/Admin). Der Schalter sitzt auf der
   * Kurs-Detailseite — nicht in der Admin-Bücherverwaltung.
   */
  setCalculation(bookId: number, isCalculation: boolean): Observable<{ isCalculation: boolean }> {
    return this.http.put<{ isCalculation: boolean }>(`/api/courses/${bookId}/calculation`, { isCalculation });
  }

  /** Linien EINES Kapitels (`null` = „ohne Kapitel"); ohne Lösungszüge. */
  /** Als Flashcard markierte Linien-Ids des Users in diesem Kurs. */
  getFlashcardMarks(bookId: number): Observable<{ lineIds: number[] }> {
    return this.http.get<{ lineIds: number[] }>(`/api/courses/${bookId}/flashcards`);
  }

  /** Setzt/entfernt die persistente Flashcard-Markierung einer Kurs-Linie. */
  setFlashcardMark(bookId: number, lineId: number, marked: boolean): Observable<{ marked: boolean }> {
    return marked
      ? this.http.post<{ marked: boolean }>(`/api/courses/${bookId}/flashcards/${lineId}`, {})
      : this.http.delete<{ marked: boolean }>(`/api/courses/${bookId}/flashcards/${lineId}`);
  }

  getChapterLines(bookId: number, chapter: string | null): Observable<CourseLine[]> {
    const params = new HttpParams().set('chapter', chapter ?? '');
    return this.http.get<CourseLine[]>(`/api/courses/${bookId}/lines`, { params });
  }

  /** Fügt Stellungen aus einem Memo-Text als neue Linien ein (Kapitel entsteht bei Bedarf). */
  addLines(bookId: number, chapter: string | null, text: string): Observable<AddCourseLinesResult> {
    return this.http.post<AddCourseLinesResult>(`/api/courses/${bookId}/lines`, { chapter, text });
  }

  deleteLine(bookId: number, lineId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}/lines/${lineId}`);
  }

  renameChapter(bookId: number, chapter: string | null, newName: string | null): Observable<{ updated: number }> {
    return this.http.put<{ updated: number }>(`/api/courses/${bookId}/chapters/rename`, { chapter, newName });
  }

  deleteChapter(bookId: number, chapter: string | null): Observable<{ deleted: number }> {
    return this.http.post<{ deleted: number }>(`/api/courses/${bookId}/chapters/delete`, { chapter });
  }

  /** Setzt den EIGENEN Fortschritt eines Kapitels zurück (Analysebäume bleiben erhalten). */
  resetChapter(bookId: number, chapter: string | null): Observable<{ cleared: number }> {
    return this.http.post<{ cleared: number }>(`/api/courses/${bookId}/chapters/reset`, { chapter });
  }
}
