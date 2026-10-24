import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AddCourseLinesResult, CourseEbookLinkResult, CourseLine, CourseLink, CourseListItem, ReprocessResult, ReprocessStatus,
} from './course.types';

/** Kurse anlegen und pflegen (Besitzer/Admin): Anlegen/Löschen, Themen, Kalkulations-Schalter, Verknüpfungen, Linien und Kapitel, Neu-Aufbereitung. */
@Injectable({ providedIn: 'root' })
export class CourseAuthoringApi {
  private readonly http = inject(HttpClient);

  /** Legt einen persönlichen Kurs an (nur für den Nutzer sichtbar) — mit PGN als Inhalt oder LEER.
   *  Ohne Datei entsteht ein Kurs ohne Stellungen, der auf der Detailseite gefüllt wird. */
  createCourse(name: string, file: File | null = null): Observable<CourseListItem> {
    const form = new FormData();
    if (name.trim()) form.append('name', name.trim());
    if (file) form.append('file', file, file.name);
    return this.http.post<CourseListItem>('/api/courses', form);
  }

  /** Löscht einen eigenen Kurs des Nutzers. */
  deleteCourse(bookId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}`);
  }

  /** Setzt die Themen-Tags des Kurs-Buchs (Admin/Besitzer). Antwortet mit den effektiven Keys
   *  (leer → Default ["tactics"]). */
  setCourseThemes(bookId: number, themes: string[]): Observable<{ themes: string[] }> {
    return this.http.put<{ themes: string[] }>(`/api/courses/${bookId}/themes`, { themes });
  }

  /**
   * Schaltet den Kalkulations-Modus des Kurses ein/aus (Besitzer/Admin). Der Schalter sitzt auf der
   * Kurs-Detailseite — nicht in der Admin-Bücherverwaltung.
   */
  setCalculation(bookId: number, isCalculation: boolean): Observable<{ isCalculation: boolean }> {
    return this.http.put<{ isCalculation: boolean }>(`/api/courses/${bookId}/calculation`, { isCalculation });
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

  /** „Mit E-Book verknüpfen“: Linien über die Stellung den Buch-Diagrammen zuordnen (ohne Buch: Server sucht). */
  linkEbook(bookId: number, ebookId?: number | null): Observable<CourseEbookLinkResult> {
    return this.http.post<CourseEbookLinkResult>(`/api/courses/${bookId}/ebook-refs`, { ebookId: ebookId ?? null });
  }

  /** Wandelt einen Kurs in ein neues Repertoire um; liefert das neue Repertoire. Ein EIGENER Kurs wird
   *  dabei verschoben (serverseitig danach gelöscht), geteilte Gruppen-/Admin-Kurse bleiben bestehen. */
  convertToRepertoire(bookId: number): Observable<{ id: number; name: string }> {
    return this.http.post<{ id: number; name: string }>(`/api/courses/${bookId}/convert-to-repertoire`, {});
  }

  /** Linien EINES Kapitels (`null` = „ohne Kapitel"); ohne Lösungszüge. */
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

  /** Wie viele (verwaltbare) Kurse müssen wegen einer neueren Aufbereitungs-Pipeline neu aufbereitet werden? */
  reprocessStatus(): Observable<ReprocessStatus> {
    return this.http.get<ReprocessStatus>('/api/courses/reprocess/status');
  }

  /** Bereitet alle veralteten Kurse neu auf (lokal bzw. Chessable-Re-Fetch im Hintergrund). */
  reprocess(): Observable<ReprocessResult> {
    return this.http.post<ReprocessResult>('/api/courses/reprocess', {});
  }
}
