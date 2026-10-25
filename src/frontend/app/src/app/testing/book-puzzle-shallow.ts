import { NO_ERRORS_SCHEMA, Type } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { TranslatePipe } from '@ngx-translate/core';
import { BookPuzzleComponent } from '../features/puzzles/book-puzzle.component';
import { BookContextCardComponent } from '../features/puzzles/book-context-card.component';
import { BookCommentBlockComponent } from '../features/puzzles/book-comment-block.component';
import { BookBoardHintComponent } from '../features/puzzles/book-board-hint.component';
import { CourseStatsPanelComponent } from '../features/puzzles/course-stats-panel.component';

const shallow = (cmp: Type<unknown>, kids: Type<unknown>[] = []) =>
  TestBed.overrideComponent(cmp, { set: { imports: [CommonModule, TranslatePipe, ...kids], schemas: [NO_ERRORS_SCHEMA] } });

/**
 * Buch-Löser im TestBed mit ausgeblendeten fremden Kindkomponenten (Brett, Status-Card, Aktionszeile … bleiben
 * unbekannte Elemente, Eingaben landen als DOM-Eigenschaft) — die eigenen Darstellungsteile der Zerlegung Z18
 * (Kontext-Karte, Statistik, Kommentar, Hinweiszeile) rendern mit, ebenso flach. So prüfen die DOM-Specs dasselbe
 * Markup wie vor dem Schnitt.
 */
export function shallowBookPuzzle(): void {
  shallow(BookPuzzleComponent, [BookContextCardComponent, BookCommentBlockComponent, BookBoardHintComponent]);
  shallow(BookContextCardComponent, [CourseStatsPanelComponent]);
  shallow(CourseStatsPanelComponent);
  shallow(BookCommentBlockComponent);
  shallow(BookBoardHintComponent);
}
