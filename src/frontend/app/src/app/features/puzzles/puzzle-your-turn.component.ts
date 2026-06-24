import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule } from '@ngx-translate/core';

export type ActivePuzzleState = 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING';
export type PuzzleMode = 'standard' | 'endless' | 'book';

const STATUS_KEYS = {
  standard: {
    yourTurn: 'puzzles.status.yourTurn',
    gaveUp: 'puzzles.status.gaveUpPlayOut',
    thinking: 'puzzles.status.thinking',
    yourMove: 'puzzles.status.yourTurn',
  },
  endless: {
    yourTurn: 'endless.game.yourTurn',
    gaveUp: '',
    thinking: 'endless.game.opponentThinking',
    yourMove: 'endless.game.yourMove',
  },
  book: {
    yourTurn: 'book.status.yourTurn',
    gaveUp: 'book.status.gaveUp',
    thinking: 'book.status.stockfishThinking',
    yourMove: 'book.status.yourMoveVsStockfish',
  },
} as const;

const EVAL_KEYS = {
  standard: { show: 'puzzles.eval.show', hide: 'puzzles.eval.hide', start: 'puzzles.eval.start', now: 'puzzles.eval.now' },
  endless: { show: 'endless.game.showEval', hide: 'endless.game.hideEval', start: 'endless.game.evalStart', now: 'endless.game.evalNow' },
  book: { show: 'puzzles.eval.show', hide: 'puzzles.eval.hide', start: 'puzzles.eval.start', now: 'puzzles.eval.now' },
} as const;

const ACTION_KEYS = {
  standard: { reset: 'puzzles.actions.reset', mouseslip: 'puzzles.actions.mouseslip', giveUp: 'puzzles.actions.giveUp' },
  endless: { reset: 'endless.game.reset', mouseslip: 'endless.game.mouseslip', giveUp: 'endless.game.giveUp' },
  book: { reset: 'book.actions.reset', mouseslip: 'book.actions.mouseslip', giveUp: 'book.actions.giveUp' },
} as const;

/**
 * Wiederverwendbares „Your turn!"-Panel für Standard-, Endless- und Buch-Puzzle.
 * Zeigt Statustext, Eval-Vergleich und Aktions-Buttons für die AWAITING/THINKING/PLAYING-States.
 * Soll innerhalb von mat-card-content des Eltern-Status-Cards gerendert werden.
 */
@Component({
  selector: 'app-puzzle-your-turn',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslateModule],
  template: `
    <div class="ytp-center">
      @if (state === 'THINKING') {
        <mat-spinner diameter="24"></mat-spinner>
      }
      <p class="ytp-status">{{ statusKey | translate }}</p>
      @if (timerSeconds !== null) {
        <p class="ytp-timer">{{ formatTime(timerSeconds) }}</p>
      }
      @if (showEval) {
        <div class="ytp-eval">
          @if (evalLoading && state !== 'THINKING') {
            <mat-spinner diameter="16"></mat-spinner>
          } @else {
            <span class="ytp-eval-item"><span class="ytp-eval-label">{{ ek.start | translate }}</span> <span class="ytp-eval-val">{{ initialEval || '...' }}</span></span>
            <span class="ytp-eval-arrow">→</span>
            <span class="ytp-eval-item"><span class="ytp-eval-label">{{ ek.now | translate }}</span> <span class="ytp-eval-val">{{ currentEval || '...' }}</span></span>
          }
        </div>
      }
      <div class="ytp-actions">
        <button mat-button (click)="evalToggled.emit()">
          <mat-icon>analytics</mat-icon>
          {{ (showEval ? ek.hide : ek.show) | translate }}
        </button>
        @if (hasMadeFirstMove || state !== 'AWAITING_USER_MOVE') {
          <button mat-button (click)="resetClicked.emit()">
            <mat-icon>replay</mat-icon>
            {{ ak.reset | translate }}
          </button>
        }
        @if (showMouseslip && (hasMadeFirstMove || state === 'PLAYING' || (state === 'THINKING' && showMouseslipInThinking))) {
          <button mat-button (click)="mouseslipClicked.emit()">
            <mat-icon>mouse</mat-icon>
            {{ ak.mouseslip | translate }}
          </button>
        }
        <button mat-button color="warn" (click)="giveUpClicked.emit()">
          <mat-icon>flag</mat-icon>
          {{ ak.giveUp | translate }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .ytp-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 1rem 0; }
    .ytp-status { font-size: 1.05em; font-weight: 500; margin: 0; text-align: center; }
    .ytp-timer { font-size: 1.5em; font-weight: bold; font-variant-numeric: tabular-nums; margin: 0; }
    .ytp-eval { display: flex; align-items: center; gap: 0.5rem; font-size: 0.95em; flex-wrap: wrap; justify-content: center; }
    .ytp-eval-item { display: flex; gap: 0.25rem; align-items: center; }
    .ytp-eval-label { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.85em; }
    .ytp-eval-val { font-weight: 600; }
    .ytp-eval-arrow { color: color-mix(in srgb, currentColor 40%, transparent); }
    .ytp-actions { display: flex; gap: 0.25rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
  `],
})
export class PuzzleYourTurnComponent {
  @Input() state: ActivePuzzleState = 'AWAITING_USER_MOVE';
  @Input() mode: PuzzleMode = 'standard';
  @Input() showEval = false;
  @Input() evalLoading = false;
  @Input() initialEval = '';
  @Input() currentEval = '';
  @Input() gaveUp = false;
  /** null = keinen Timer anzeigen (Endless). */
  @Input() timerSeconds: number | null = null;
  /** !mouseslipUsed && (!onSolutionPath || hasMadeFirstMove) – berechnet im Eltern. */
  @Input() showMouseslip = false;
  /** Endless zeigt Mouseslip auch im THINKING-State. */
  @Input() showMouseslipInThinking = false;
  /** User hat mindestens einen Zug gemacht – steuert Reset/Mouseslip-Sichtbarkeit auf Korrektpfad. */
  @Input() hasMadeFirstMove = false;

  @Output() evalToggled = new EventEmitter<void>();
  @Output() resetClicked = new EventEmitter<void>();
  @Output() mouseslipClicked = new EventEmitter<void>();
  @Output() giveUpClicked = new EventEmitter<void>();

  get statusKey(): string {
    const k = STATUS_KEYS[this.mode];
    if (this.state === 'THINKING') return k.thinking;
    if (this.state === 'PLAYING') {
      if (this.gaveUp && this.mode === 'standard') return k.gaveUp;
      return k.yourMove;
    }
    if (this.gaveUp && this.mode !== 'endless') return k.gaveUp;
    return k.yourTurn;
  }

  get ek() { return EVAL_KEYS[this.mode]; }
  get ak() { return ACTION_KEYS[this.mode]; }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }
}
