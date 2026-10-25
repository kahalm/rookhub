import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BookPuzzleDto } from '../puzzles/puzzle.service';
import {
  CourseShareRecipient, CourseShareResult, PublicSlugChapterTarget, PublicSlugTarget,
} from './course.types';
import { langParams } from '../../shared/lang-params';

/** Kurse teilen: Freigabe an Nutzer, öffentliche Kurse und Kurz-URLs (`/{slug}`, `/{slug}/{kapitel}`). */
@Injectable({ providedIn: 'root' })
export class CourseShareApi {
  private readonly http = inject(HttpClient);

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

  /** Puzzles eines ÖFFENTLICHEN Kurses — ohne Login. Basis für das registrierungsfreie
   *  Durchspielen eines als „public" markierten Kurses (404, wenn nicht öffentlich).
   *  Optional seitenweise (`skip`/`take`): große Kurse laden die erste Seite sofort, den Rest
   *  im Hintergrund — ohne Parameter kommt (rückwärtskompatibel) das ganze Buch. */
  getPublicCourse(bookId: number, skip?: number, take?: number, lang?: string | null): Observable<BookPuzzleDto[]> {
    let params = new HttpParams();
    if (skip != null) params = params.set('skip', String(skip));
    if (take != null) params = params.set('take', String(take));
    return this.http.get<BookPuzzleDto[]>(`/api/courses/${bookId}/public`, { params: langParams(lang, params) });
  }

  /**
   * Öffentlichen Kurz-Alias (z. B. „mate1") auf die BookId auflösen — ohne Login (404 = unbekannt).
   *
   * `isCalculation` entscheidet, WOHIN der Alias springt: ein Kalkulationsbuch besteht aus
   * `IsInfoOnly`-Stellungen, die aus allen Solver-Pools ausgeschlossen sind — der Solver meldete
   * dort sofort „abgeschlossen". Die Verzweigung braucht die Auskunft, sie ist also Teil der
   * Antwort und wird nicht erraten (fehlt sie, gilt „kein Kalkulationsbuch").
   */
  resolvePublicSlug(slug: string): Observable<PublicSlugTarget> {
    return this.http.get<PublicSlugTarget>(`/api/courses/by-slug/${encodeURIComponent(slug)}`);
  }

  /**
   * Kapitel-Kurz-URL `/{slug}/{kapitel}`: der zweite Teil IST der Kapitelname (kein zweites
   * Konzept — der Link soll selbsterklärend bleiben). Der Server liefert zusätzlich den
   * SOLVER-Kapitelindex (`chapterIndex`, nur Quiz-Kapitel — `null`, wenn das Kapitel im Solver
   * nicht startbar ist); Kalkulationsbücher bekommen den Namen stattdessen als Filter.
   */
  resolvePublicSlugChapter(slug: string, chapter: string): Observable<PublicSlugChapterTarget> {
    return this.http.get<PublicSlugChapterTarget>(
      `/api/courses/by-slug/${encodeURIComponent(slug)}/${encodeURIComponent(chapter)}`);
  }
}
