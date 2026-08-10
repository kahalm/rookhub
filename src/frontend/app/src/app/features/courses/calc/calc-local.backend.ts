import { Observable, map, of, throwError } from 'rxjs';
import { CalcReviewPatch } from './calc-review.util';
import {
  CalcBackend, CalcBook, CalcPosition, CalcPositionListItem, CalcPublicBook, CalcPublicPosition,
  CalcReviewSaved, CalcTreeSaved, CalculationService,
} from './calculation.service';
import {
  deleteCalcLocalTree, readCalcLocal, readCalcLocalEntry, writeCalcLocalReview, writeCalcLocalTree,
} from './calc-local.util';

/**
 * Kalkulations-Modus OHNE Konto: die Stellungen kommen einmalig lesend vom öffentlichen
 * Endpoint, alles Selbstgemachte (Baum, Festlegung, Zeit, Bewertung) bleibt im localStorage
 * dieses Geräts.
 *
 * Bewusst als {@link CalcBackend} gebaut und nicht als Sonderweg in der Komponente: die
 * Komponente kennt nur „lade Buch / lade Stellung / speichere". Der Unterschied „Server oder
 * dieses Gerät" liegt an EINER Stelle — hier.
 *
 * Alle Schreibwege antworten synchron (`of(...)`), weil der Speicher synchron ist; die
 * Komponente behandelt sie wie jede andere Antwort (inkl. ihrer Outbox-Logik, die dann nie
 * etwas zu wiederholen hat).
 */
export class LocalCalculationBackend implements CalcBackend {
  /** Stellungen des öffentlichen Buchs (nach dem einen Abruf) — Quelle für `getPosition`. */
  private positions: CalcPublicPosition[] = [];
  private book: CalcPublicBook | null = null;

  constructor(private api: CalculationService, private bookId: number) {}

  getBook(bookId: number): Observable<CalcBook> {
    return this.api.getPublicBook(bookId).pipe(map(book => {
      this.book = book;
      this.positions = book.positions ?? [];
      this.bookId = book.bookId || bookId;
      return this.toCalcBook(book);
    }));
  }

  /**
   * Sprungliste aus dem öffentlichen Buch + dem lokalen Stand. Kapitel- und Kurssummen bleiben
   * ABSICHTLICH leer: die rechnet die Ansicht aus den Zeilen selbst (siehe `refreshSums`) —
   * es gibt keinen Server, der sie führen könnte, und zwei Wahrheiten wollen wir nicht.
   */
  private toCalcBook(book: CalcPublicBook): CalcBook {
    const local = readCalcLocal(this.bookId);
    const positions: CalcPositionListItem[] = (book.positions ?? []).map(p => {
      const entry = local[String(p.id)];
      return {
        id: p.id,
        round: p.round,
        title: p.title,
        chapter: p.chapter,
        hasTree: !!entry?.tree,
        chosenSan: entry?.chosenSan ?? null,
        chosenUci: entry?.chosenUci ?? null,
        secondsSpent: entry?.secondsSpent ?? 0,
        grade: entry?.grade ?? null,
      };
    });
    return {
      bookId: book.bookId,
      displayName: book.displayName,
      isCalculation: book.isCalculation,
      positions,
    };
  }

  getPosition(bookPuzzleId: number): Observable<CalcPosition> {
    const found = this.positions.find(p => p.id === bookPuzzleId);
    if (!found) return throwError(() => new Error('position not in public book'));
    const entry = readCalcLocalEntry(this.bookId, bookPuzzleId);
    return of({
      id: found.id,
      bookId: this.book?.bookId ?? this.bookId,
      round: found.round,
      title: found.title,
      chapter: found.chapter,
      fen: found.fen,
      setupMoves: found.setupMoves ?? '',
      comment: found.comment,
      treeJson: entry?.tree ?? null,
      treeUpdatedAt: entry?.updatedAt ?? null,
      chosenSan: entry?.chosenSan ?? null,
      chosenUci: entry?.chosenUci ?? null,
      secondsSpent: entry?.secondsSpent ?? 0,
      grade: entry?.grade ?? null,
    });
  }

  saveTree(bookPuzzleId: number, treeJson: string): Observable<CalcTreeSaved> {
    const updatedAt = writeCalcLocalTree(this.bookId, bookPuzzleId, treeJson);
    // Kein Platz (Quota/gesperrt/zu groß) → wie ein fehlgeschlagener Server-Save: die Komponente
    // zeigt ihren Hinweis, statt fälschlich „gespeichert" zu behaupten.
    if (!updatedAt) return throwError(() => new Error('local storage unavailable'));
    return of({ bookPuzzleId, updatedAt });
  }

  deleteTree(bookPuzzleId: number): Observable<void> {
    // Wie saveTree/saveReview: ein gescheiterter Speicher meldet einen FEHLER, statt Erfolg
    // vorzutäuschen (der Baum wäre nach dem Neuladen wieder da).
    if (!deleteCalcLocalTree(this.bookId, bookPuzzleId)) {
      return throwError(() => new Error('local storage unavailable'));
    }
    return of(undefined);
  }

  saveReview(bookPuzzleId: number, patch: CalcReviewPatch): Observable<CalcReviewSaved> {
    const next = writeCalcLocalReview(this.bookId, bookPuzzleId, patch);
    // Wie `saveTree`: kein Platz (Quota/gesperrt) → FEHLER. Ein `of(...)` mit dem bloß gerechneten
    // Stand hätte der Ansicht „Festlegung, Zeit und Bewertung gespeichert" gemeldet, obwohl nichts
    // geschrieben wurde — nach dem Neuladen wäre alles weg gewesen.
    if (!next) return throwError(() => new Error('local storage unavailable'));
    return of({ bookPuzzleId, ...next });
  }
}
