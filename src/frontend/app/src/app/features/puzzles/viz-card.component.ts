import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatIconModule, TranslateModule],
  template: `
    <mat-card class="viz-card">
      <mat-card-content>
        <div class="viz-header">
          <mat-icon class="viz-icon">visibility_off</mat-icon>
          <span class="viz-level-badge">Level {{ visualizationMode }}</span>
          <span class="viz-desc">{{ vizLevelDescription }}</span>
        </div>
        @if (vizCountdownSeconds > 0) {
          <div class="viz-countdown">{{ countdownKey | translate: { seconds: vizCountdownSeconds } }}</div>
        }
        @if (vizMoveHtml) {
          <div class="viz-moves" [innerHTML]="vizMoveHtml"></div>
        } @else {
          <div class="viz-moves viz-moves--empty">{{ noMoveKey | translate }}</div>
        }
        @if (vizPiecesHidden) {
          <button class="viz-show-btn" (click)="vizShowClicked.emit()">
            <mat-icon>{{ vizShowPressed ? 'visibility' : 'visibility_off' }}</mat-icon>
            {{ (vizShowPressed ? showingKey : showKey) | translate }}
          </button>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .viz-card .viz-header {
      display: flex; align-items: center; gap: 0.4rem; margin-bottom: 0.5rem; flex-wrap: wrap;
    }
    .viz-card .viz-icon { font-size: 18px; width: 18px; height: 18px; color: color-mix(in srgb, currentColor 45%, transparent); flex-shrink: 0; }
    .viz-card .viz-level-badge {
      font-size: 0.7rem; font-weight: 700; padding: 1px 7px; border-radius: 10px;
      background: #e3f2fd; color: #1565c0; white-space: nowrap; flex-shrink: 0;
    }
    .viz-card .viz-desc { font-size: 0.78rem; color: color-mix(in srgb, currentColor 50%, transparent); }
    .viz-card .viz-moves {
      font-size: 1.05em; line-height: 1.6;
      border-left: 3px solid #90caf9; border-radius: 0 6px 6px 0;
      padding: 0.4rem 0.7rem; word-break: break-word;
      background: rgba(25,118,210,0.04);
    }
    .viz-card .viz-moves--empty { color: color-mix(in srgb, currentColor 40%, transparent); font-style: italic; }
    .viz-countdown { font-size: 0.9em; color: #e65100; font-weight: 500; margin-bottom: 0.25rem; }
    .viz-show-btn {
      display: flex; align-items: center; gap: 0.3rem;
      margin-top: 0.5rem; padding: 0.3rem 1rem; border: 1px solid color-mix(in srgb, currentColor 18%, transparent);
      border-radius: 6px; background: var(--mat-sys-surface-container, #fff); cursor: pointer; font-weight: 500; font-size: 0.9em;
      user-select: none; touch-action: manipulation; color: inherit;
    }
    .viz-show-btn mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .viz-show-btn:active { background: color-mix(in srgb, currentColor 10%, transparent); }
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
