import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CalcGrade, CalcReviewPatch, toReviewBody } from './calc-review.util';

/** Eine Stellung in der Sprungliste (leicht — ohne FEN/Kommentar, ohne Züge). */
export interface CalcPositionListItem {
  id: number;
  round: string;
  title: string | null;
  /** null = ohne Kapitel. */
  chapter: string | null;
  /** Der Nutzer hat zu dieser Stellung schon einen Analysebaum gespeichert. */
  hasTree: boolean;
  /** Festlegung: erster Zug, auf den sich der Nutzer festgelegt hat (SAN); null = keine. */
  chosenSan: string | null;
  /** Dieselbe Festlegung in UCI. */
  chosenUci: string | null;
  /** Aufsummierte aktive Rechenzeit an dieser Stellung (Sekunden). */
  secondsSpent: number;
  /**
   * Selbstbewertung als STUFE 0..4; null = noch nicht bewertet (≠ Stufe 0 „nicht gelöst").
   * Die Punkte schickt der Server zwar mit, die Anzeige leitet sie aber selbst aus der Stufe ab
   * (`gradePoints`) — sonst gäbe es zwei Wahrheiten, die beim optimistischen Klick auseinanderlaufen.
   */
  grade: CalcGrade | null;
}

/** Fertige Summen eines Kapitels (kommen vom Server, siehe {@link CalcBook}). */
export interface CalcChapterSummary {
  /** null = Sammelgruppe „ohne Kapitel". */
  chapter: string | null;
  /** Summe der erreichten Punkte. */
  points: number;
  /** Erreichbare Punkte des Kapitels (4 je Stellung) — eine Summe ohne Maximum ist nicht lesbar. */
  maxPoints: number;
  /**
   * Summe der Rechenzeit (Sekunden). Heißt bewusst `secondsSum` und nicht `secondsSpent`: es ist
   * eine SUMME über mehrere Stellungen, während `secondsSpent` überall sonst die Zeit EINER
   * Stellung meint. Der Name MUSS mit `CalcChapterSummaryDto.SecondsSum` im Backend übereinstimmen —
   * eine Abweichung fällt nicht auf, sie zeigt nur still 0 an.
   */
  secondsSum: number;
}

export interface CalcBook {
  bookId: number;
  displayName: string;
  isCalculation: boolean;
  positions: CalcPositionListItem[];
  /** Summen je Kapitel. Fehlen sie, rechnet die Ansicht sie aus den Zeilen selbst. */
  chapters?: CalcChapterSummary[];
  /** Erreichte Punkte des ganzen Kurses. */
  points?: number;
  /** Erreichbare Punkte des ganzen Kurses. */
  maxPoints?: number;
  /** Gesamte Rechenzeit des Kurses (Sekunden) — Summe, daher `secondsSum` (siehe
   *  {@link CalcChapterSummary}). */
  secondsSum?: number;
}

/** Eine Stellung inkl. eigenem Baum. Enthält NIE die Buchlösung — nur den Vorlauf `setupMoves`. */
export interface CalcPosition {
  id: number;
  bookId: number;
  round: string;
  title: string | null;
  chapter: string | null;
  fen: string;
  /** UCI-Züge von `fen` bis zur Aufgabenstellung (leer bei reinen Stellungs-Linien). */
  setupMoves: string;
  comment: string | null;
  treeJson: string | null;
  treeUpdatedAt: string | null;
  /** Festlegung/Zeit/Stufe — die Sprungliste führt dieselben Werte; hier nur, falls der
   *  Server sie an der Einzelstellung mitliefert (dann gewinnen sie beim Laden). */
  chosenSan?: string | null;
  chosenUci?: string | null;
  secondsSpent?: number;
  grade?: CalcGrade | null;
}

export interface CalcTreeSaved {
  bookPuzzleId: number;
  updatedAt: string;
}

/** Antwort auf das Setzen von Festlegung/Stufe/Zeit — der Server schickt den neuen Stand zurück
 *  (insbesondere die AUFADDIERTE `secondsSpent`, die der Client nicht selbst kennen kann). */
export interface CalcReviewSaved {
  bookPuzzleId: number;
  chosenSan: string | null;
  chosenUci: string | null;
  secondsSpent: number;
  grade: CalcGrade | null;
}

/** HTTP-Zugang zum Kalkulations-Modus (`/api/calculations`). */
@Injectable({ providedIn: 'root' })
export class CalculationService {
  constructor(private http: HttpClient) {}

  getBook(bookId: number): Observable<CalcBook> {
    return this.http.get<CalcBook>(`/api/calculations/books/${bookId}`);
  }

  getPosition(bookPuzzleId: number): Observable<CalcPosition> {
    return this.http.get<CalcPosition>(`/api/calculations/positions/${bookPuzzleId}`);
  }

  saveTree(bookPuzzleId: number, treeJson: string): Observable<CalcTreeSaved> {
    return this.http.put<CalcTreeSaved>(`/api/calculations/positions/${bookPuzzleId}`, { treeJson });
  }

  deleteTree(bookPuzzleId: number): Observable<void> {
    return this.http.delete<void>(`/api/calculations/positions/${bookPuzzleId}`);
  }

  /**
   * Setzt Festlegung/Stufe und meldet Rechenzeit — bewusst NEBEN dem Baum-Speichern (PATCH statt
   * PUT): der Baum ist für den Server opak und bis zu 256 KB groß, diese drei Werte sind eigene
   * Spalten (auswertbar) und ändern sich unabhängig von ihm. Gesendet werden nur die geänderten
   * Felder; `secondsDelta` wird serverseitig AUFADDIERT.
   */
  saveReview(bookPuzzleId: number, patch: CalcReviewPatch): Observable<CalcReviewSaved> {
    return this.http.patch<CalcReviewSaved>(`/api/calculations/positions/${bookPuzzleId}`,
      toReviewBody(patch));
  }
}
