import { Observable } from 'rxjs';
import { CourseLineEbookRef } from '../courses/course.service';

/**
 * Kurslinie → E-Book-Diagramm (Wunsch 2026-10-15) für den Knopf „Im Buch“ im Löser: je Linie der erste Verweis.
 * Aus `book-puzzle.component` herausgelöst (Regel „große Dateien beim Anfassen verkleinern“).
 */
export class CourseEbookRefs {
  private byLine = new Map<number, CourseLineEbookRef>();

  /** Verweis der Linie oder null. */
  get(lineId: number | null | undefined): CourseLineEbookRef | null {
    return lineId == null ? null : this.byLine.get(lineId) ?? null;
  }

  /** Leert die Liste und lädt sie neu; ohne Quelle (anonym/kein Kurs) bleibt sie leer. Fehler still: dann fehlt nur der Knopf. */
  load(source: Observable<CourseLineEbookRef[]> | null): void {
    this.byLine = new Map();
    source?.subscribe({ next: refs => this.set(refs), error: () => {} });
  }

  set(refs: CourseLineEbookRef[]): void {
    const m = new Map<number, CourseLineEbookRef>();
    for (const r of refs) if (!m.has(r.lineId)) m.set(r.lineId, r);
    this.byLine = m;
  }
}
