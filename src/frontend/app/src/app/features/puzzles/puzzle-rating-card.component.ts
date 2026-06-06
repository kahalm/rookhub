import { Component, Input, Output, EventEmitter } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import { PuzzleTagsComponent } from './puzzle-tags.component';

/**
 * Wiederverwendbare Rating-Info-Card für Standard- und Endless-Puzzle.
 * Zeigt Puzzle-Rating, optionale Level-Info, Tags und optionalen Share-Button.
 * Rendert die mat-card selbst inkl. Wrapper.
 */
@Component({
  selector: 'app-puzzle-rating-card',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule, TranslateModule, PuzzleTagsComponent],
  template: `
    <mat-card class="prc-card">
      <mat-card-content>
        <div class="prc-info">
          <span class="prc-rating">
            {{ ratingKey | translate:ratingParams }}@if (appendRating !== null) {: {{ appendRating }}}
          </span>
          @if (levelText) {
            <span class="prc-level">{{ levelText }}</span>
          }
          <app-puzzle-tags [tags]="tags" />
          @if (shareKey) {
            <button mat-stroked-button class="prc-share" (click)="shareClicked.emit()">
              <mat-icon>share</mat-icon> {{ shareKey | translate }}
            </button>
          }
        </div>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .prc-card { margin-bottom: 0; }
    .prc-info { display: flex; flex-direction: column; gap: 0.5rem; position: relative; }
    .prc-rating { font-weight: bold; font-size: 1.1em; }
    .prc-level { font-size: 0.9em; color: rgba(0,0,0,0.6); }
    .prc-share { width: 100%; }
  `],
})
export class PuzzleRatingCardComponent {
  /** i18n-Key für das Rating-Label (z.B. 'puzzles.info.rating' oder 'endless.game.puzzleRating'). */
  @Input() ratingKey = 'puzzles.info.rating';
  /** Params für den Rating-Key (für endless: { rating: puzzle.rating }). */
  @Input() ratingParams: Record<string, unknown> = {};
  /** Falls gesetzt, wird ': appendRating' nach dem übersetzten Key angehängt (Standard-Modus). */
  @Input() appendRating: number | null = null;
  /** Optionaler Level-Text (z.B. 'Level 0 (705–745)'), bereits übersetzt vom Eltern. */
  @Input() levelText = '';
  /** Puzzle-Themes/Tags-String. */
  @Input() tags = '';
  /** i18n-Key für den Share-Button; leer = kein Share-Button. */
  @Input() shareKey = '';

  @Output() shareClicked = new EventEmitter<void>();
}
