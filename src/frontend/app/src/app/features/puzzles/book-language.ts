import { BookPuzzleDto, PuzzleService } from './puzzle.service';
import { CourseLanguageService, CourseRef } from '../courses/course-language.service';

/** Was die Sprachwahl der Kurs-Kommentare vom Buch-Löser braucht. */
export interface BookLanguageHost {
  readonly puzzle: BookPuzzleDto | null;
  readonly inCourse: boolean;
  readonly inWeekly: boolean;
  readonly isDaily: boolean;
  readonly standalone: boolean;
  readonly course: { readonly bookId: number | null; readonly isAnon: boolean; refreshAnonCache(bookId: number): void; autoCache(): void };
  readonly courseLang: CourseLanguageService;
  readonly puzzleService: PuzzleService;
  /** Im Durchklicken die gerade angezeigte Stelle neu ziehen (Kommentar in der neuen Sprache). */
  redrawReview(): void;
}

/**
 * Sprache der Kommentare im Buch-Löser (Kurs-Übersetzung, Stufe C; Zerlegung Z18): Sprachwahl für Kurs und
 * Einzel-Linie im Buch, Texte der laufenden Linie tauschen ohne den Versuch neu zu starten, Offline-Kopie nachziehen.
 */
export class BookLanguageController {
  constructor(private readonly h: BookLanguageHost) {}

  /** Gibt es hier eine Sprachwahl? Kurs und Einzel-Linie im Buch — nicht Tagespuzzle/Wochenpost
   *  (die bleiben bewusst im Original). */
  get enabled(): boolean {
    const h = this.h;
    return (h.inCourse || h.standalone) && !h.isDaily && !h.inWeekly;
  }

  /** Der Kurs dieser Ansicht für die Sprachwahl: im Kurs über die bookId, beim Einzel-Puzzle über den
   *  Dateinamen der Linie (`BookPuzzleDto` trägt keine Kurs-Id). */
  get ref(): CourseRef | null {
    if (!this.enabled) return null;
    return { bookId: this.h.inCourse ? this.h.course.bookId : null, fileName: this.h.puzzle?.bookFileName ?? null };
  }

  /** `?lang=` für die nächste Anfrage — `null` außerhalb von Kurs/Buch (Tagespuzzle, Wochenpost). */
  get requestLang(): string | null {
    return this.enabled ? this.h.courseLang.requestLang(this.ref) : null;
  }

  /** Sprache gewählt: merken, die Texte der aktuellen Linie tauschen (ohne den Versuch neu zu
   *  starten) und die Offline-Kopie in der neuen Sprache nachziehen. */
  pick(lang: string): void {
    const ref = this.ref;
    if (!ref) return;
    this.h.courseLang.setChoice(ref, lang);
    this.refreshTexts();
    const course = this.h.course;
    if (this.h.inCourse && course.bookId != null) {
      if (course.isAnon) course.refreshAnonCache(course.bookId);
      else course.autoCache();
    }
  }

  /** „maschinell übersetzt" angeklickt → aufs Original (die Quelle ist die erste Sprache der Linie). */
  showOriginal(): void {
    const source = this.h.courseLang.source(this.ref) ?? this.h.puzzle?.commentLanguages?.[0] ?? null;
    if (source) this.pick(source);
  }

  /** Nur die TEXTE der aktuellen Linie in der gewählten Sprache holen — der laufende Versuch bleibt. */
  private refreshTexts(): void {
    const p = this.h.puzzle;
    const lang = this.requestLang;
    if (!p || !lang || (typeof navigator !== 'undefined' && !navigator.onLine)) return;
    this.h.puzzleService.getBookPuzzleById(p.id, lang).subscribe({
      next: fresh => { const cur = this.h.puzzle; if (cur?.id === fresh?.id) this.applyTexts(cur, fresh); },
      error: () => { /* Texte bleiben, wie sie sind — die nächste Linie kommt in der neuen Sprache */ },
    });
  }

  /** Texte einer frisch geholten Fassung übernehmen und die gerade angezeigte Stelle neu ziehen. */
  private applyTexts(target: BookPuzzleDto, fresh: BookPuzzleDto): void {
    target.comment = fresh.comment;
    target.moveComments = fresh.moveComments;
    target.titleLabel = fresh.titleLabel ?? null;
    target.chapterLabel = fresh.chapterLabel ?? null;
    target.commentLanguage = fresh.commentLanguage ?? null;
    target.commentLanguages = fresh.commentLanguages ?? null;
    target.commentMachine = !!fresh.commentMachine;
    this.h.courseLang.noteLines(this.ref, [fresh]);
    // Im Durchklicken steht der Kommentar schon als `moveComment` fest — an derselben Stelle neu holen.
    this.h.redrawReview();
  }

  /** Nach dem Laden einer Linie: Sprachen merken; kam sie in einer anderen als der gewählten
   *  (Einzel-Puzzle: Wahl erst mit dem Dateinamen bekannt), die Texte nachholen. */
  afterLineLoaded(p: BookPuzzleDto, sentLang: string | null): void {
    if (!this.enabled) return;
    if (this.h.inCourse) this.h.courseLang.rememberFile(this.h.course.bookId, p.bookFileName);
    this.h.courseLang.noteLines(this.ref, [p]);
    const want = this.requestLang;
    if (want && sentLang && want !== sentLang && (p.commentLanguages?.length ?? 0) > 1) this.refreshTexts();
  }
}
