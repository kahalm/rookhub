import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Eine Stellung in der Sprungliste (leicht — ohne FEN/Kommentar, ohne Züge). */
export interface CalcPositionListItem {
  id: number;
  round: string;
  title: string | null;
  /** null = ohne Kapitel. */
  chapter: string | null;
  /** Der Nutzer hat zu dieser Stellung schon einen Analysebaum gespeichert. */
  hasTree: boolean;
}

export interface CalcBook {
  bookId: number;
  displayName: string;
  isCalculation: boolean;
  positions: CalcPositionListItem[];
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
}

export interface CalcTreeSaved {
  bookPuzzleId: number;
  updatedAt: string;
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
}
