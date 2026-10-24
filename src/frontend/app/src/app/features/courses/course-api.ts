import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, Subject } from 'rxjs';
import { BookPuzzleDto } from '../puzzles/puzzle.service';
import {
  CourseChapter, CourseDetail, CourseLineEbookRef, CourseLineStatus, CourseListItem, CourseMode, CourseNextPuzzle, CourseProgress,
} from './course.types';
import { courseLangParams } from './course-lang-params';

/** Kurse lesen und lösen: Liste, Detail, Kapitel, nächste Linie, Ergebnisse, Fortschritt, Merkzeichen, Downloads. */
@Injectable({ providedIn: 'root' })
export class CourseApi {
  private readonly http = inject(HttpClient);

  /** Feuert, wenn sich der Kurs-Zugriff geändert haben könnte (z. B. nach einem Buch-Import) —
   *  die Navbar prüft daraufhin neu, ob das „Kurse"-Menü gezeigt wird. */
  private readonly accessChanged = new Subject<void>();
  readonly accessChanged$ = this.accessChanged.asObservable();
  notifyAccessChanged(): void { this.accessChanged.next(); }

  getCourses(): Observable<CourseListItem[]> {
    return this.http.get<CourseListItem[]>('/api/courses');
  }

  /** Hat der eingeloggte User Zugriff auf mindestens einen Kurs? (Menü-Sichtbarkeit) */
  checkAccess(): Observable<{ hasAccess: boolean }> {
    return this.http.get<{ hasAccess: boolean }>('/api/courses/access');
  }

  /** Vollbild der Kurs-Detailseite (Metadaten, eigener Fortschritt, Kapitel-Verwaltungssicht). Mit
   *  `lang` tragen die Kapitel ihren übersetzten Namen als `label`. */
  getDetail(bookId: number, lang?: string | null): Observable<CourseDetail> {
    return this.http.get<CourseDetail>(`/api/courses/${bookId}`, { params: courseLangParams(lang) });
  }

  /** Kapitel eines Buchs in Lesereihenfolge inkl. Fortschritt (für die Kapitelübersicht). Mit `lang`
   *  tragen sie ihren übersetzten Namen als `label`. */
  getChapters(bookId: number, lang?: string | null): Observable<CourseChapter[]> {
    return this.http.get<CourseChapter[]>(`/api/courses/${bookId}/chapters`,
      { params: courseLangParams(lang) });
  }

  /** Alle Puzzles eines Buchs (Offline-Speichern, Durchsehen, Karteikarten). `lang` = Sprache der
   *  Kommentare (Kurs-Übersetzung); fehlt sie, kommt das Original. */
  getBookPuzzles(bookId: number, lang?: string | null): Observable<BookPuzzleDto[]> {
    return this.http.get<BookPuzzleDto[]>(`/api/courses/${bookId}/puzzles`,
      { params: courseLangParams(lang) });
  }

  /** Pro-Linien-Bearbeitungsstatus (gelöst ✓ / versucht-aber-nicht-gelöst ✗) eines Buchs. */
  getLineStatus(bookId: number): Observable<CourseLineStatus> {
    return this.http.get<CourseLineStatus>(`/api/courses/${bookId}/line-status`);
  }

  getNext(bookId: number, mode: CourseMode, after?: number, exclude?: number, chapterIndex?: number,
          lang?: string | null): Observable<CourseNextPuzzle> {
    let params = new HttpParams().set('mode', mode);
    if (after != null) params = params.set('after', after);
    if (exclude != null) params = params.set('exclude', exclude);
    if (chapterIndex != null) params = params.set('chapterIndex', chapterIndex);
    return this.http.get<CourseNextPuzzle>(`/api/courses/${bookId}/next`, { params: courseLangParams(lang, params) });
  }

  recordResult(bookId: number, bookPuzzleId: number, solved: boolean, mode?: CourseMode, timeSeconds = 0, chapterIndex?: number, hintsUsed = 0, solveMode?: string): Observable<CourseProgress> {
    // `mode` = Durchlaufart (sequential/random), `solveMode` = Spielweise ('training'/'easy');
    // fehlt letztere, fällt der Server auf 'training' zurück.
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/results`, { bookPuzzleId, solved, mode, timeSeconds, chapterIndex, hintsUsed, solveMode });
  }

  /** „Offene nochmal": gescheiterte, ungelöste Linien wieder in den Topf (gelöste bleiben gelöst). */
  retryFailed(bookId: number): Observable<void> {
    return this.http.post<void>(`/api/courses/${bookId}/retry-failed`, {});
  }

  /** Persönlich: Info-Linien dieses Kurses im sequenziellen Durchlauf überspringen. */
  setSkipInfoLines(bookId: number, skip: boolean): Observable<{ skipInfoLines: boolean }> {
    return this.http.put<{ skipInfoLines: boolean }>(`/api/courses/${bookId}/skip-info-lines`, { skip });
  }

  reset(bookId: number): Observable<CourseProgress> {
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/reset`, {});
  }

  /** Setzt den EIGENEN Fortschritt eines Kapitels zurück (Analysebäume bleiben erhalten). */
  resetChapter(bookId: number, chapter: string | null): Observable<{ cleared: number }> {
    return this.http.post<{ cleared: number }>(`/api/courses/${bookId}/chapters/reset`, { chapter });
  }


  /** Merkt eine sequenziell durchgeklickte Info-/Erklärlinie — beim nächsten Wiedereinstieg
   *  startet der Kurs dahinter statt sie erneut zu zeigen. */
  markInfoSeen(bookId: number, bookPuzzleId: number): Observable<void> {
    return this.http.post<void>(`/api/courses/${bookId}/info-seen`, { bookPuzzleId });
  }

  /** Pinnt einen Kurs fürs Dashboard an (persönlich, idempotent). */
  pinCourse(bookId: number): Observable<void> {
    return this.http.post<void>(`/api/courses/${bookId}/pin`, {});
  }

  /** Löst einen angepinnten Kurs wieder vom Dashboard. */
  unpinCourse(bookId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}/pin`);
  }

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

  /** Wo die Linien des Kurses im E-Book stehen (nur sichtbare Bücher). */
  getEbookRefs(bookId: number): Observable<CourseLineEbookRef[]> {
    return this.http.get<CourseLineEbookRef[]>(`/api/courses/${bookId}/ebook-refs`);
  }

  /** Lädt das Buch als PGN (ein Spiel je Linie). */
  downloadPgn(bookId: number): Observable<Blob> {
    return this.http.get(`/api/courses/${bookId}/pgn`, { responseType: 'blob' });
  }

  /** Lädt EIN Kapitel als PGN (`chapter` null = „ohne Kapitel"); Varianten und Kommentare bleiben erhalten. */
  downloadChapterPgn(bookId: number, chapter: string | null): Observable<Blob> {
    const params = new HttpParams().set('chapter', chapter ?? '');
    return this.http.get(`/api/courses/${bookId}/chapter-pgn`, { params, responseType: 'blob' });
  }

  /** Lädt EINE Linie als PGN. */
  downloadLinePgn(bookId: number, lineId: number): Observable<Blob> {
    return this.http.get(`/api/courses/${bookId}/lines/${lineId}/pgn`, { responseType: 'blob' });
  }
}
