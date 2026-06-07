import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { TranslateModule } from '@ngx-translate/core';
import { PuzzleYourTurnComponent, PuzzleMode } from './puzzle-your-turn.component';
import { ReviewNavComponent } from './review-nav.component';

const CK = {
  standard: {
    loading: 'puzzles.status.loading',
    setup: 'puzzles.status.watchOpponent',
    correct: 'puzzles.result.correct',
    checkmate: 'puzzles.result.checkmate',
    altSolution: 'puzzles.result.alternativeSolution',
    incorrect: 'puzzles.result.incorrect',
    gaveUp: 'puzzles.result.solutionPlayedOut',
    gaveUpSubtext: 'puzzles.result.tryYourselfNextTime',
    nextPuzzle: 'puzzles.actions.nextPuzzle',
    failedNext: 'puzzles.actions.nextPuzzle',
    analyze: 'puzzles.actions.analyze',
  },
  endless: {
    loading: 'endless.game.loadingPuzzle',
    setup: 'endless.game.watchOpponent',
    correct: 'endless.game.correct',
    checkmate: 'endless.game.checkmate',
    altSolution: 'endless.game.alternativeSolution',
    incorrect: 'endless.game.wrong',
    gaveUp: 'endless.game.gaveUp',
    gaveUpSubtext: '',
    nextPuzzle: 'endless.game.continue',
    failedNext: 'endless.game.continue',
    analyze: 'endless.game.analyze',
  },
  book: {
    loading: 'book.status.loading',
    setup: 'book.status.watchOpponent',
    correct: 'book.status.correct',
    checkmate: 'book.status.checkmate',
    altSolution: 'book.status.altSolution',
    incorrect: 'book.status.incorrect',
    gaveUp: 'book.status.gaveUpSolution',
    gaveUpSubtext: '',
    nextPuzzle: 'book.actions.nextPuzzle',
    failedNext: 'book.actions.nextPuzzle',
    analyze: 'book.actions.analyze',
  },
} as const;

/**
 * Einheitliche Status-Card für alle drei Puzzle-Modi (Standard/Endless/Buch).
 * Kapselt Zahnrad-Button + den kompletten State-Switch (LOADING/SETUP/YourTurn/SOLVED/FAILED).
 */
@Component({
  selector: 'app-puzzle-status-card',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatCardModule, TranslateModule, PuzzleYourTurnComponent, ReviewNavComponent,
  ],
  template: `
    <mat-card class="psc-card">
      <mat-card-content>
        <button mat-icon-button class="psc-gear" (click)="settingsClicked.emit()">
          <mat-icon>settings</mat-icon>
        </button>

        @if (fullGameReview) {
          <div class="psc-center">
            <p class="psc-text">{{ 'book.status.fullGame' | translate }}</p>
            <app-review-nav [currentIndex]="reviewIndex" [totalSteps]="reviewTotal"
              (prev)="reviewPrev.emit()" (next)="reviewNext.emit()" />
            <button mat-button (click)="exitReviewClicked.emit()">
              <mat-icon>close</mat-icon> {{ 'book.actions.backToPuzzle' | translate }}
            </button>
          </div>
        } @else {
          @switch (state) {
            @case ('LOADING') {
              <div class="psc-center">
                <mat-spinner diameter="40"></mat-spinner>
                <p>{{ ck.loading | translate }}</p>
              </div>
            }
            @case ('SETUP') {
              <div class="psc-center">
                <p class="psc-text">{{ ck.setup | translate }}</p>
              </div>
            }
            @case ('AWAITING_USER_MOVE') {
              <app-puzzle-your-turn
                state="AWAITING_USER_MOVE"
                [mode]="mode"
                [showEval]="showEval"
                [evalLoading]="evalLoading"
                [initialEval]="initialEval"
                [currentEval]="currentEval"
                [gaveUp]="gaveUp"
                [timerSeconds]="elapsedSeconds"
                [showMouseslip]="showMouseslip"
                [showMouseslipInThinking]="showMouseslipInThinking"
                [hasMadeFirstMove]="hasMadeFirstMove"
                (evalToggled)="evalToggled.emit()"
                (resetClicked)="resetClicked.emit()"
                (mouseslipClicked)="mouseslipClicked.emit()"
                (giveUpClicked)="giveUpClicked.emit()"
              />
            }
            @case ('THINKING') {
              <app-puzzle-your-turn
                state="THINKING"
                [mode]="mode"
                [showEval]="showEval"
                [evalLoading]="evalLoading"
                [initialEval]="initialEval"
                [currentEval]="currentEval"
                [showMouseslip]="showMouseslip"
                [showMouseslipInThinking]="showMouseslipInThinking"
                [hasMadeFirstMove]="hasMadeFirstMove"
                (evalToggled)="evalToggled.emit()"
                (resetClicked)="resetClicked.emit()"
                (mouseslipClicked)="mouseslipClicked.emit()"
                (giveUpClicked)="giveUpClicked.emit()"
              />
            }
            @case ('PLAYING') {
              <app-puzzle-your-turn
                state="PLAYING"
                [mode]="mode"
                [showEval]="showEval"
                [evalLoading]="evalLoading"
                [initialEval]="initialEval"
                [currentEval]="currentEval"
                [gaveUp]="gaveUp"
                [timerSeconds]="elapsedSeconds"
                [showMouseslip]="showMouseslip"
                [showMouseslipInThinking]="showMouseslipInThinking"
                [hasMadeFirstMove]="hasMadeFirstMove"
                (evalToggled)="evalToggled.emit()"
                (resetClicked)="resetClicked.emit()"
                (mouseslipClicked)="mouseslipClicked.emit()"
                (giveUpClicked)="giveUpClicked.emit()"
              />
            }
            @case ('SOLVED') {
              <div class="psc-center psc-solved">
                <mat-icon class="psc-result-icon">check_circle</mat-icon>
                @if (alternativeSolve) {
                  <p class="psc-text">{{ ck.checkmate | translate }}</p>
                  <p class="psc-hint">{{ ck.altSolution | translate }}</p>
                } @else {
                  <p class="psc-text">{{ ck.correct | translate }}</p>
                }
                @if (lastEloChange != null) {
                  @if (lastEloChange < 0) {
                    <span class="psc-elo psc-elo-down">{{ lastEloChange }}</span>
                  } @else {
                    <span class="psc-elo psc-elo-up">+{{ lastEloChange }}</span>
                  }
                }
                @if (elapsedSeconds !== null) {
                  <p class="psc-timer">{{ formatTime(elapsedSeconds) }}</p>
                }
                <app-review-nav [currentIndex]="reviewIndex" [totalSteps]="reviewTotal"
                  (prev)="reviewPrev.emit()" (next)="reviewNext.emit()" />
                <div class="psc-actions">
                  <button mat-raised-button color="primary" (click)="nextClicked.emit()">
                    {{ ck.nextPuzzle | translate }}@if (solvedCountdown > 0) { ({{ solvedCountdown }})}
                  </button>
                  <button mat-button (click)="analyzeClicked.emit()">
                    <mat-icon>biotech</mat-icon> {{ ck.analyze | translate }}
                  </button>
                </div>
              </div>
            }
            @case ('FAILED') {
              <div class="psc-center psc-failed">
                <mat-icon class="psc-result-icon" [class.psc-gave-up-icon]="gaveUp">
                  {{ gaveUp ? 'flag' : 'cancel' }}
                </mat-icon>
                <p class="psc-text">{{ (gaveUp ? ck.gaveUp : ck.incorrect) | translate }}</p>
                @if (gaveUp && ck.gaveUpSubtext) {
                  <p class="psc-hint">{{ ck.gaveUpSubtext | translate }}</p>
                }
                @if (lastEloChange != null) {
                  <span class="psc-elo psc-elo-down">{{ lastEloChange }}</span>
                }
                <app-review-nav [currentIndex]="reviewIndex" [totalSteps]="reviewTotal"
                  (prev)="reviewPrev.emit()" (next)="reviewNext.emit()" />
                <div class="psc-actions">
                  <button mat-button (click)="retryClicked.emit()">{{ 'common.retry' | translate }}</button>
                  <button mat-button (click)="analyzeClicked.emit()">
                    <mat-icon>biotech</mat-icon> {{ ck.analyze | translate }}
                  </button>
                  <button mat-raised-button color="primary" (click)="failedNextClicked.emit()">
                    {{ ck.failedNext | translate }}
                  </button>
                </div>
              </div>
            }
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .psc-card { min-height: 120px; position: relative; }
    .psc-gear { position: absolute; top: 4px; right: 4px; z-index: 2; }
    .psc-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 1rem 0; }
    .psc-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .psc-hint { font-size: 0.85em; color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; text-align: center; }
    .psc-timer { font-size: 1.5em; font-weight: bold; font-variant-numeric: tabular-nums; margin: 0; }
    .psc-result-icon { font-size: 48px; width: 48px; height: 48px; }
    .psc-solved .psc-result-icon { color: #4caf50; }
    .psc-failed .psc-result-icon { color: #f44336; }
    .psc-gave-up-icon { color: #ff9800 !important; }
    .psc-elo { font-size: 1.2em; font-weight: bold; }
    .psc-elo-up { color: #4caf50; }
    .psc-elo-down { color: #f44336; }
    .psc-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
  `],
})
export class PuzzleStatusCardComponent {
  @Input() mode: PuzzleMode = 'standard';
  @Input() state = 'LOADING';
  @Input() showEval = false;
  @Input() evalLoading = false;
  @Input() initialEval = '';
  @Input() currentEval = '';
  @Input() gaveUp = false;
  /** null = keinen Per-Puzzle-Timer anzeigen (Endless). */
  @Input() elapsedSeconds: number | null = null;
  @Input() showMouseslip = false;
  @Input() showMouseslipInThinking = false;
  @Input() hasMadeFirstMove = false;
  @Input() alternativeSolve = false;
  @Input() lastEloChange: number | null = null;
  @Input() solvedCountdown = 0;
  @Input() reviewIndex = 0;
  @Input() reviewTotal = 0;
  /** Buch-Modus: Ganze-Partie-Review (statt State-Switch). */
  @Input() fullGameReview = false;

  @Output() settingsClicked = new EventEmitter<void>();
  @Output() evalToggled = new EventEmitter<void>();
  @Output() resetClicked = new EventEmitter<void>();
  @Output() mouseslipClicked = new EventEmitter<void>();
  @Output() giveUpClicked = new EventEmitter<void>();
  @Output() retryClicked = new EventEmitter<void>();
  @Output() nextClicked = new EventEmitter<void>();
  @Output() failedNextClicked = new EventEmitter<void>();
  @Output() analyzeClicked = new EventEmitter<void>();
  @Output() reviewPrev = new EventEmitter<void>();
  @Output() reviewNext = new EventEmitter<void>();
  @Output() exitReviewClicked = new EventEmitter<void>();

  get ck() { return CK[this.mode]; }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }
}
