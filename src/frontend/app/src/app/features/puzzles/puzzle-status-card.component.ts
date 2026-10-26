import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { formatPuzzleTime } from './puzzle-format.util';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { PuzzleYourTurnComponent, PuzzleMode } from './puzzle-your-turn.component';
import { ReviewNavComponent } from './review-nav.component';

const CK = {
  standard: {
    loading: 'solver.status.loading',
    setup: 'solver.status.watchOpponent',
    correct: 'solver.status.correct',
    checkmate: 'solver.status.checkmate',
    altSolution: 'solver.status.altSolution',
    incorrect: 'solver.status.incorrect',
    gaveUp: 'puzzles.result.solutionPlayedOut',
    gaveUpSubtext: 'puzzles.result.tryYourselfNextTime',
    nextPuzzle: 'solver.actions.nextPuzzle',
    failedNext: 'solver.actions.nextPuzzle',
    analyze: 'solver.actions.analyze',
    showOriginalSolution: 'solver.actions.showOriginalSolution',
  },
  endless: {
    loading: 'solver.status.loading',
    setup: 'solver.status.watchOpponent',
    correct: 'solver.status.correct',
    checkmate: 'solver.status.checkmate',
    altSolution: 'solver.status.altSolution',
    incorrect: 'solver.status.incorrect',
    gaveUp: 'endless.game.gaveUp',
    gaveUpSubtext: '',
    nextPuzzle: 'endless.game.continue',
    failedNext: 'endless.game.continue',
    analyze: 'solver.actions.analyze',
    showOriginalSolution: 'solver.actions.showOriginalSolution',
  },
  book: {
    loading: 'solver.status.loading',
    setup: 'solver.status.watchOpponent',
    correct: 'solver.status.correct',
    checkmate: 'solver.status.checkmate',
    altSolution: 'solver.status.altSolution',
    incorrect: 'solver.status.incorrect',
    gaveUp: 'book.status.gaveUpSolution',
    gaveUpSubtext: '',
    nextPuzzle: 'solver.actions.nextPuzzle',
    failedNext: 'solver.actions.nextPuzzle',
    analyze: 'solver.actions.analyze',
    showOriginalSolution: 'solver.actions.showOriginalSolution',
  },
} as const;

/**
 * Einheitliche Status-Card für alle drei Puzzle-Modi (Standard/Endless/Buch).
 * Kapselt den kompletten State-Switch (LOADING/SETUP/YourTurn/SOLVED/FAILED);
 * Einstellungen liegen seit UI-Welle 2 im ⋮-Menü der PuzzleActionBar.
 */
@Component({
  selector: 'app-puzzle-status-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatCardModule, MatTooltipModule, TranslatePipe, PuzzleYourTurnComponent, ReviewNavComponent,
  ],
  template: `
    <!-- data-state: fester Halt fuer die E2E-Specs — der sichtbare Statustext haengt an Modus
         und Visualisierungsstufe (im Wartezustand steht er gar nicht in der Karte). -->
    <mat-card class="psc-card" [attr.data-state]="state">
      <mat-card-content>
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
                @if (awaitingSolveMode) {
                  <!-- Aufgabe steht schon auf dem Brett, es fehlt nur die Spielweise (Dialog darüber). -->
                  <p class="psc-text">{{ 'solveMode.awaitingChoice' | translate }}</p>
                } @else {
                  <mat-spinner diameter="40"></mat-spinner>
                  <p>{{ ck.loading | translate }}</p>
                }
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
                [showStatus]="showTurnStatus"
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
                [showSkip]="showSkip"
                (skipClicked)="skipClicked.emit()"
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
                [showSkip]="showSkip"
                (skipClicked)="skipClicked.emit()"
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
                [showSkip]="showSkip"
                (skipClicked)="skipClicked.emit()"
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
                  <!-- d3-rk-puzzle-zeitanzeige-ohne-beschriftung: wie beim Lösen mit Uhr-Symbol + „Lösezeit“. -->
                  <p class="psc-timer" role="timer" [matTooltip]="'puzzles.solveTime' | translate"
                     [attr.aria-label]="('puzzles.solveTime' | translate) + ': ' + formatTime(elapsedSeconds)">
                    <mat-icon class="timer-icon" aria-hidden="true">timer</mat-icon>{{ formatTime(elapsedSeconds) }}
                  </p>
                }
                <app-review-nav [currentIndex]="reviewIndex" [totalSteps]="reviewTotal"
                  (prev)="reviewPrev.emit()" (next)="reviewNext.emit()" />
                <div class="psc-actions">
                  @if (alternativeSolve) {
                    <button mat-button (click)="showOriginalSolutionClicked.emit()">
                      <mat-icon>replay</mat-icon> {{ ck.showOriginalSolution | translate }}
                    </button>
                  }
                  @if (showNext) {
                    <button mat-flat-button color="primary" (click)="nextClicked.emit()">
                      {{ ck.nextPuzzle | translate }}@if (solvedCountdown > 0) { ({{ solvedCountdown }})}
                    </button>
                  }
                  <button mat-button (click)="analyzeClicked.emit()">
                    <mat-icon>biotech</mat-icon> {{ ck.analyze | translate }}
                  </button>
                  @if (canFavorite) {
                    <button mat-button class="psc-love" [class.psc-loved]="isFavorite" (click)="favoriteToggled.emit()"
                            [attr.aria-pressed]="isFavorite"
                            [attr.aria-label]="(isFavorite ? 'favorites.loved' : 'favorites.love') | translate">
                      <mat-icon>{{ isFavorite ? 'favorite' : 'favorite_border' }}</mat-icon>
                      {{ (isFavorite ? 'favorites.loved' : 'favorites.love') | translate }}
                    </button>
                  }
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
                @if (solutionHidden) {
                  <!-- Matt nach Fehlzug: das Matt bleibt stehen, die Lösung erst auf Wunsch (2026-10-23). -->
                  <p class="psc-hint">{{ 'puzzles.result.matedHint' | translate }}</p>
                } @else {
                  <app-review-nav [currentIndex]="reviewIndex" [totalSteps]="reviewTotal"
                    (prev)="reviewPrev.emit()" (next)="reviewNext.emit()" />
                }
                <div class="psc-actions">
                  <button mat-button (click)="retryClicked.emit()">{{ 'common.retry' | translate }}</button>
                  @if (solutionHidden) {
                    <button mat-button class="psc-show-solution" (click)="showSolutionClicked.emit()">
                      <mat-icon>visibility</mat-icon> {{ 'puzzles.result.showSolution' | translate }}
                    </button>
                  }
                  <button mat-button (click)="analyzeClicked.emit()">
                    <mat-icon>biotech</mat-icon> {{ ck.analyze | translate }}
                  </button>
                  @if (canFavorite) {
                    <button mat-button class="psc-love" [class.psc-loved]="isFavorite" (click)="favoriteToggled.emit()"
                            [attr.aria-pressed]="isFavorite"
                            [attr.aria-label]="(isFavorite ? 'favorites.loved' : 'favorites.love') | translate">
                      <mat-icon>{{ isFavorite ? 'favorite' : 'favorite_border' }}</mat-icon>
                      {{ (isFavorite ? 'favorites.loved' : 'favorites.love') | translate }}
                    </button>
                  }
                  <button mat-flat-button color="primary" (click)="failedNextClicked.emit()">
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
    .psc-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 1rem 0; }
    .psc-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .psc-hint { font-size: 0.85em; color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; text-align: center; }
    .psc-timer { display: inline-flex; align-items: center; gap: 6px; font-size: 1.25em; font-weight: bold; font-variant-numeric: tabular-nums; margin: 0; }
    .timer-icon { font-size: 20px; width: 20px; height: 20px; opacity: .7; }
    .psc-result-icon { font-size: 48px; width: 48px; height: 48px; }
    .psc-solved .psc-result-icon { color: var(--rh-success); }
    .psc-failed .psc-result-icon { color: var(--rh-error); }
    .psc-gave-up-icon { color: var(--rh-warn) !important; }
    .psc-elo { font-size: 1.2em; font-weight: bold; }
    .psc-elo-up { color: var(--rh-success); }
    .psc-elo-down { color: var(--rh-error); }
    .psc-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
    .psc-loved { color: #e91e63; }
  `],
})
export class PuzzleStatusCardComponent {
  @Input() mode: PuzzleMode = 'standard';
  @Input() state = 'LOADING';
  /** LOADING, aber das Puzzle ist schon da und wartet nur auf die Spielweise-Wahl → Bitte um die Wahl statt
   *  Lade-Spinner (UX-045). */
  @Input() awaitingSolveMode = false;
  /** „Du bist am Zug!" im WARTE-Zustand anzeigen. Die Solver setzen das auf false, solange unter dem
   *  Brett schon „Weiß/Schwarz am Zug" steht (Doppelaussage); THINKING/PLAYING behalten ihren Text,
   *  weil der dort etwas anderes sagt („Stockfish denkt…" / „Du spielst weiter"). */
  @Input() showTurnStatus = true;
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
  /** „Weiter"-Knopf im Gelöst-Zustand zeigen. Aus für direkt geteilte Einzel-Puzzles (stehen bleiben). */
  @Input() showNext = true;
  @Input() reviewIndex = 0;
  @Input() reviewTotal = 0;
  /** Buch-Modus: Ganze-Partie-Review (statt State-Switch). */
  @Input() fullGameReview = false;
  /** Herz-Button („geliebtes Puzzle") im Gelöst-/Gescheitert-Zustand zeigen (nur eingeloggt). */
  @Input() canFavorite = false;
  /** Ist das aktuelle Puzzle bereits favorisiert? */
  @Input() isFavorite = false;

  @Output() evalToggled = new EventEmitter<void>();
  @Output() resetClicked = new EventEmitter<void>();
  @Output() mouseslipClicked = new EventEmitter<void>();
  @Output() giveUpClicked = new EventEmitter<void>();
  /** Kurs/Wochenpost: „Überspringen“ neben „Aufgeben“ (an app-puzzle-your-turn durchgereicht). */
  @Input() showSkip = false;
  @Output() skipClicked = new EventEmitter<void>();
  @Output() retryClicked = new EventEmitter<void>();
  /** Fehlgeschlagen, aber die Lösung ist noch nicht gezeigt (Matt nach Fehlzug): statt der Lösungs-Navigation „Lösung zeigen“. */
  @Input() solutionHidden = false;
  @Output() showSolutionClicked = new EventEmitter<void>();
  @Output() nextClicked = new EventEmitter<void>();
  @Output() failedNextClicked = new EventEmitter<void>();
  @Output() analyzeClicked = new EventEmitter<void>();
  @Output() reviewPrev = new EventEmitter<void>();
  @Output() reviewNext = new EventEmitter<void>();
  @Output() exitReviewClicked = new EventEmitter<void>();
  /** „Originale Lösung zeigen" — nur bei alternativem (eigenem) Mattweg sichtbar. */
  @Output() showOriginalSolutionClicked = new EventEmitter<void>();
  /** Herz umschalten (aktuelles Puzzle lieben/entlieben). */
  @Output() favoriteToggled = new EventEmitter<void>();

  get ck() { return CK[this.mode]; }

  formatTime = formatPuzzleTime;
}
