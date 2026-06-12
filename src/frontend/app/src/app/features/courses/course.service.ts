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
}

export interface CourseNextPuzzle {
  puzzle: BookPuzzleDto | null;
  solvedCount: number;
  total: number;
  completed: boolean;
}

export interface CourseProgress {
  bookId: number;
  solvedCount: number;
  total: number;
  progressPercent: number;
  completed: boolean;
  lastMode: string | null;
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

  getNext(bookId: number, mode: CourseMode, after?: number, exclude?: number): Observable<CourseNextPuzzle> {
    let params = new HttpParams().set('mode', mode);
    if (after != null) params = params.set('after', after);
    if (exclude != null) params = params.set('exclude', exclude);
    return this.http.get<CourseNextPuzzle>(`/api/courses/${bookId}/next`, { params });
  }

  recordResult(bookId: number, bookPuzzleId: number, solved: boolean, mode?: CourseMode, timeSeconds = 0): Observable<CourseProgress> {
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/results`, { bookPuzzleId, solved, mode, timeSeconds });
  }

  reset(bookId: number): Observable<CourseProgress> {
    return this.http.post<CourseProgress>(`/api/courses/${bookId}/reset`, {});
  }
}
