import { Component, Input, Output, EventEmitter } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Wiederverwendbare Visualisierungs-Karte (Blindschach-Hilfe) für Standard-,
 * Buch- und Endless-Puzzle. Reine Darstellung: alle Werte kommen als Inputs vom
 * Eltern-Solver, der „Aufdecken"-Klick wird als Event hochgereicht. Die Sichtbarkeits-
 * Bedingung (state-abhängig) bleibt im `@if`-Wrapper der Eltern-Komponente; die
 * i18n-Keys unterscheiden sich je Modus und werden als Inputs übergeben.
 */
@Component({
  selector: 'app-viz-card',
  standalone: true,
  imports: [MatCardModule, MatIconModule, TranslateModule],
  template: `
    <mat-card class="viz-card">
      <mat-card-content>
        <div class="viz-title"><mat-icon>visibility_off</mat-icon> {{ titleKey | translate: { level: visualizationMode } }}</div>
        @if (vizCountdownSeconds > 0) {
          <div class="viz-countdown">{{ countdownKey | translate: { seconds: vizCountdownSeconds } }}</div>
        }
        @if (vizMoveHtml) {
          <div class="viz-moves" [innerHTML]="vizMoveHtml"></div>
        } @else {
          <div class="viz-moves">{{ noMoveKey | translate }}</div>
        }
        @if (vizPiecesHidden) {
          <button class="viz-show-btn" (click)="vizShowClicked.emit()">
            {{ (vizShowPressed ? showingKey : showKey) | translate }}
          </button>
        }
        <div class="viz-hint">{{ vizLevelDescription }}</div>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .viz-card .viz-title { display: flex; align-items: center; gap: 0.35rem; font-weight: 600; margin-bottom: 0.4rem; }
    .viz-card .viz-moves {
      font-family: 'Courier New', monospace; font-size: 1.05em; line-height: 1.5;
      background: rgba(0,0,0,0.04); border-radius: 6px; padding: 0.5rem 0.6rem; word-break: break-word;
    }
    .viz-card .viz-hint { font-size: 0.8em; color: rgba(0,0,0,0.55); margin-top: 0.4rem; }
    .viz-countdown { font-size: 0.9em; color: #e65100; font-weight: 500; margin-bottom: 0.25rem; }
    .viz-show-btn {
      margin-top: 0.4rem; padding: 0.35rem 1.2rem; border: 1px solid rgba(0,0,0,0.2);
      border-radius: 6px; background: #fff; cursor: pointer; font-weight: 500;
      user-select: none; touch-action: manipulation;
    }
    .viz-show-btn:active { background: #e3f2fd; }
  `],
})
export class VizCardComponent {
  @Input() visualizationMode = 0;
  @Input() vizCountdownSeconds = 0;
  @Input() vizMoveText = '';
  @Input() vizMoveHtml = '';
  @Input() vizPiecesHidden = false;
  @Input() vizShowPressed = false;
  @Input() vizLevelDescription = '';

  /** i18n-Keys je Modus (puzzles.viz.* / book.viz.* / endless.game.*). */
  @Input() titleKey = '';
  @Input() countdownKey = '';
  @Input() noMoveKey = '';
  @Input() showKey = '';
  @Input() showingKey = '';

  @Output() vizShowClicked = new EventEmitter<void>();
}
