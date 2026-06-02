import { Component, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { EndlessStorageService, EndlessConfig, EndlessSession } from './endless-storage.service';
import { takeFromPool, takeNearestFromPool, buildEndlessRunWindows, autoFasttrackThresholds, fasttrackSteps } from './endless-prefetch.util';
import { OfflineService } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

// AWAITING_USER_MOVE = first move only (no buttons)
// THINKING = opponent responding (buttons visible, board locked)
// PLAYING = user's turn after first move (buttons visible, board active)
type EndlessState = 'CONFIG' | 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE'
  | 'THINKING' | 'PLAYING' | 'CORRECT' | 'WRONG' | 'GAME_OVER' | 'EXHAUSTED';

interface EndlessPuzzleAttempt {
  puzzleNumber: number;
  puzzleId: number;
  lichessId: string;
  rating: number;
  solved: boolean;
  themes?: string;
  /** Start-/Endzeit dieses Puzzles als Unix-Millis (fürs serverseitige Logging). */
  startedAt: number;
  endedAt: number;
}

/** Breite des Rating-Fensters bei der Puzzleauswahl (früher = config.step). */
const RATING_WINDOW = 40;

@Component({
  selector: 'app-endless-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, MatSlideToggleModule,
    MatDialogModule, MatSnackBarModule, TranslateModule, PuzzleBoardComponent
  ],
  template: `
    <div class="endless-page">
      @if (showHelp) {
        <div class="help-overlay" (click)="showHelp = false">
          <div class="help-content" (click)="$event.stopPropagation()">
            <div class="help-header">
              <h2>{{ 'endless.help.title' | translate }}</h2>
              <button mat-icon-button (click)="showHelp = false"><mat-icon>close</mat-icon></button>
            </div>
            <div class="help-body">
              <h3>{{ 'endless.help.gameplayHeading' | translate }}</h3>
              <p>{{ 'endless.help.gameplayP1' | translate }}</p>
              <p [innerHTML]="'endless.help.gameplayP2' | translate"></p>

              <h3>{{ 'endless.help.movesHeading' | translate }}</h3>
              <p [innerHTML]="'endless.help.movesP1' | translate"></p>
              <p [innerHTML]="'endless.help.movesP2' | translate"></p>
              <ul>
                <li [innerHTML]="'endless.help.movesItem1' | translate"></li>
                <li [innerHTML]="'endless.help.movesItem2' | translate"></li>
                <li>{{ 'endless.help.movesItem3' | translate }}</li>
              </ul>

              <h3>{{ 'endless.help.buttonsHeading' | translate }}</h3>
              <ul>
                <li [innerHTML]="'endless.help.buttonsShowEval' | translate"></li>
                <li [innerHTML]="'endless.help.buttonsReset' | translate"></li>
                <li [innerHTML]="'endless.help.buttonsMouseslip' | translate"></li>
                <li [innerHTML]="'endless.help.buttonsGiveUp' | translate"></li>
              </ul>

              <h3>{{ 'endless.help.settingsHeading' | translate }}</h3>
              <ul>
                <li [innerHTML]="'endless.help.settingsStartRating' | translate"></li>
                <li [innerHTML]="'endless.help.settingsStockfishDepth' | translate"></li>
                <li [innerHTML]="'endless.help.settingsThemes' | translate"></li>
              </ul>

              <h3>{{ 'endless.help.fasttrackHeading' | translate }}</h3>
              <p>{{ 'endless.help.fasttrackP1' | translate }}</p>
              <ul>
                <li [innerHTML]="'endless.help.fasttrackPhase1' | translate"></li>
                <li [innerHTML]="'endless.help.fasttrackPhase2' | translate"></li>
                <li [innerHTML]="'endless.help.fasttrackPhase3' | translate"></li>
              </ul>
              <p>{{ 'endless.help.fasttrackP2' | translate }}</p>
            </div>
          </div>
        </div>
      }
      @switch (screen) {
        @case ('config') {
          <div class="config-screen">
            <mat-card class="config-card">
              <mat-card-header>
                <mat-card-title>
                  {{ 'endless.config.title' | translate }}
                  <button mat-icon-button class="help-btn" (click)="showHelp = true"><mat-icon>help_outline</mat-icon></button>
                </mat-card-title>
                <mat-card-subtitle>{{ 'endless.config.subtitle' | translate }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="config-fields">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'endless.config.startRating' | translate }}</mat-label>
                    <input matInput type="number" [(ngModel)]="config.startElo" [min]="puzzleRange.min" [max]="puzzleRange.max" step="50">
                    <mat-hint>{{ puzzleRange.min }}–{{ puzzleRange.max }}</mat-hint>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'endless.config.stockfishDepth' | translate }}</mat-label>
                    <input matInput type="number" [(ngModel)]="config.stockfishDepth" min="1" max="24" step="1">
                    <mat-hint>{{ 'endless.config.stockfishDepthHint' | translate }}</mat-hint>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'endless.config.themes' | translate }}</mat-label>
                    <input matInput [(ngModel)]="config.themes" [placeholder]="'endless.config.themesPlaceholder' | translate">
                  </mat-form-field>
                </div>

                <div class="level-preview">
                  @if (fasttrackPhase1Step > 0) {
                    <p class="threshold-explain">{{ 'endless.config.thresholdExplain' | translate }}</p>
                    <div class="threshold-fields">
                      <div class="threshold-field-wrap">
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'endless.config.firstThreshold' | translate }}</mat-label>
                          <input matInput type="number" [(ngModel)]="fasttrackAvgFirst" (ngModelChange)="onThresholdChange()" [min]="puzzleRange.min" [max]="puzzleRange.max" step="50">
                        </mat-form-field>
                        @if (fasttrackAvgFirst !== fasttrackAutoFirst) {
                          <span class="auto-hint" (click)="resetThreshold(1)">{{ 'endless.config.auto' | translate:{ value: fasttrackAutoFirst } }}</span>
                        }
                      </div>
                      <div class="threshold-field-wrap">
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'endless.config.secondThreshold' | translate }}</mat-label>
                          <input matInput type="number" [(ngModel)]="fasttrackAvgSecond" (ngModelChange)="onThresholdChange()" [min]="puzzleRange.min" [max]="puzzleRange.max" step="50">
                        </mat-form-field>
                        @if (fasttrackAvgSecond !== fasttrackAutoSecond) {
                          <span class="auto-hint" (click)="resetThreshold(2)">{{ 'endless.config.auto' | translate:{ value: fasttrackAutoSecond } }}</span>
                        }
                      </div>
                    </div>
                    <div class="fasttrack-preview">
                      <div class="fasttrack-phase">
                        <span class="phase-label">{{ 'endless.config.phase1Label' | translate }}</span>
                        <span class="phase-detail">{{ 'endless.config.step' | translate:{ step: fasttrackPhase1Step } }} | {{ config.startElo }} → {{ fasttrackAvgFirst }}</span>
                      </div>
                      <div class="fasttrack-phase">
                        <span class="phase-label">{{ 'endless.config.phase2Label' | translate }}</span>
                        <span class="phase-detail">{{ 'endless.config.step' | translate:{ step: fasttrackPhase2Step } }} | {{ fasttrackAvgFirst }} → {{ fasttrackAvgSecond }}</span>
                      </div>
                      <div class="fasttrack-phase">
                        <span class="phase-label">{{ 'endless.config.phase3Label' | translate }}</span>
                        <span class="phase-detail">{{ 'endless.config.step' | translate:{ step: 20 } }}</span>
                      </div>
                    </div>
                  }
                </div>

                <div class="settings-bar">
                  <button mat-icon-button class="settings-gear" [class.active]="showSettings" (click)="showSettings = !showSettings" [attr.title]="'endless.config.settings' | translate">
                    <mat-icon>settings</mat-icon>
                  </button>
                </div>
                @if (showSettings) {
                <div class="viz-slider">
                  <label>{{ 'endless.config.visualizationLevel' | translate:{ level: visualizationMode } }}</label>
                  <input type="range" min="0" max="4" step="1"
                         [value]="visualizationMode"
                         (input)="setVisualizationLevel(+$any($event.target).value)">
                  <div class="viz-level-desc">{{ vizLevelDescription }}</div>
                </div>
                <div class="theme-section">
                  <div class="theme-label">{{ 'endless.config.mode' | translate }}</div>
                  <div class="theme-chips">
                    <div class="theme-chip" [class.active]="themeMode === 'fixed'" (click)="setThemeMode('fixed')">
                      <mat-icon>palette</mat-icon><span class="theme-name">{{ 'endless.config.modeNormal' | translate }}</span>
                    </div>
                    <div class="theme-chip" [class.active]="themeMode === 'random'" (click)="setThemeMode('random')">
                      <mat-icon>shuffle</mat-icon><span class="theme-name">{{ 'endless.config.modeRandom' | translate }}</span>
                    </div>
                    <div class="theme-chip" [class.active]="themeMode === 'crazy'" (click)="setThemeMode('crazy')">
                      <mat-icon>auto_awesome</mat-icon><span class="theme-name">{{ 'endless.config.modeCrazy' | translate }}</span>
                    </div>
                  </div>
                  @if (themeMode === 'fixed') {
                  <div class="theme-label" style="margin-top: 0.75rem;">{{ 'endless.config.boardTheme' | translate }}</div>
                  <div class="theme-chips">
                    @for (t of boardThemes; track t.key) {
                      <div class="theme-chip" [class.active]="boardTheme === t.key" (click)="setBoardTheme(t.key)">
                        @if (t.img) {
                          <div class="theme-img" [style.backgroundImage]="'url(' + t.img + ')'"></div>
                        } @else {
                          <div class="theme-preview">
                            <div class="tp-light" [style.background]="t.light"></div>
                            <div class="tp-dark" [style.background]="t.dark"></div>
                          </div>
                        }
                        <span class="theme-name">{{ t.name }}</span>
                      </div>
                    }
                  </div>
                  <div class="theme-label" style="margin-top: 0.75rem;">{{ 'endless.config.pieces' | translate }}</div>
                  <div class="theme-chips">
                    @for (p of pieceSets; track p.key) {
                      <div class="theme-chip" [class.active]="pieceSet === p.key" (click)="setPieceSet(p.key)">
                        <div class="piece-preview" [style.backgroundImage]="'url(' + p.preview + ')'"></div>
                        <span class="theme-name">{{ p.name }}</span>
                      </div>
                    }
                  </div>
                  }
                </div>
                }

                <div class="lives-display config-lives">
                  @for (i of [1,2,3]; track i) {
                    <mat-icon class="heart full">favorite</mat-icon>
                  }
                </div>

                @if (highscore > 0) {
                  <div class="highscore-badge">
                    <mat-icon>emoji_events</mat-icon>
                    {{ 'endless.config.highscore' | translate:{ rating: highscore } }}
                  </div>
                }

                @if (sessionHistory.length > 0) {
                  <p class="session-count">
                    {{ 'endless.config.sessionsPlayed' | translate:{ count: sessionHistory.length } }}
                    @if (authService.isLoggedIn) {
                      <button mat-button class="history-link" (click)="router.navigate(['/puzzles/endless/history'])">
                        <mat-icon>history</mat-icon> {{ 'endless.config.viewHistory' | translate }}
                      </button>
                    }
                  </p>
                }

                @if (activeGameState && activeGameState.lives > 0) {
                  <div class="resume-banner">
                    <div class="resume-info">
                      <mat-icon>pause_circle</mat-icon>
                      <span>{{ 'endless.config.unfinishedRun' | translate:{ level: activeGameState.level + 1, solved: activeGameState.solved, lives: activeGameState.lives, max: activeGameState.maxRatingReached } }}</span>
                    </div>
                    <div class="resume-actions">
                      <button mat-raised-button color="primary" class="start-btn" (click)="resumeGame()">
                        <mat-icon>play_arrow</mat-icon>
                        {{ 'endless.config.continue' | translate }}
                      </button>
                      <button mat-stroked-button color="warn" (click)="archiveAndStartNew()">
                        <mat-icon>archive</mat-icon>
                        {{ 'endless.config.archiveAndNew' | translate }}
                      </button>
                    </div>
                  </div>
                }

                <button mat-raised-button color="primary" class="start-btn" (click)="startGame()">
                  <mat-icon>play_arrow</mat-icon>
                  {{ (activeGameState ? 'endless.config.newGame' : 'endless.config.start') | translate }}
                </button>
              </mat-card-content>
            </mat-card>
          </div>
        }

        @case ('play') {
          <div class="play-screen">
            <div class="board-section" [class.viz-hidden]="vizPiecesHidden && !vizShowPressed">
              <app-puzzle-board
                [fen]="boardFen"
                [actualFen]="actualFen"
                [orientation]="orientation"
                [turnColor]="turnColor"
                [dests]="dests"
                [lastMove]="lastMove"
                [viewOnly]="reviewMode || (state !== 'AWAITING_USER_MOVE' && state !== 'PLAYING' && state !== 'THINKING')"
                [premovable]="state === 'THINKING'"
                [check]="isCheck"
                [boardTheme]="boardTheme"
                [pieceSet]="pieceSet"
                [visualization]="(state !== 'CORRECT' && state !== 'WRONG') ? visualizationMode : 0"
                (moveMade)="onMoveMade($event)"
              />
            </div>

            <div class="info-section">
              @if (visualizationMode && state !== 'CORRECT' && state !== 'WRONG') {
                <mat-card class="viz-card">
                  <mat-card-content>
                    <div class="viz-title"><mat-icon>visibility_off</mat-icon> {{ 'endless.game.visualization' | translate:{ level: visualizationMode } }}</div>
                    @if (vizCountdownSeconds > 0) {
                      <div class="viz-countdown">{{ 'endless.game.vizCountdown' | translate:{ seconds: vizCountdownSeconds } }}</div>
                    }
                    <div class="viz-moves">{{ vizMoveText || ('endless.game.vizNoMove' | translate) }}</div>
                    @if (vizPiecesHidden) {
                      <button class="viz-show-btn" (click)="onVizShow()">
                        {{ (vizShowPressed ? 'endless.game.vizShowing' : 'endless.game.vizShow') | translate }}
                      </button>
                    }
                    <div class="viz-hint">{{ vizLevelDescription }}</div>
                  </mat-card-content>
                </mat-card>
              }
              <mat-card class="status-card">
                <mat-card-content>
                  @switch (state) {
                    @case ('LOADING') {
                      <div class="status-center">
                        <mat-spinner diameter="40"></mat-spinner>
                        <p>{{ 'endless.game.loadingPuzzle' | translate }}</p>
                      </div>
                    }
                    @case ('SETUP') {
                      <div class="status-center">
                        <p class="status-text">{{ 'endless.game.watchOpponent' | translate }}</p>
                      </div>
                    }
                    @case ('AWAITING_USER_MOVE') {
                      <div class="status-center">
                        <p class="status-text">{{ 'endless.game.yourTurn' | translate }}</p>
                        @if (showEval) {
                          <div class="eval-compare">
                            @if (evalLoading) {
                              <mat-spinner diameter="16"></mat-spinner>
                            } @else {
                              <span class="eval-item"><span class="eval-label">{{ 'endless.game.evalStart' | translate }}</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                              <span class="eval-arrow">→</span>
                              <span class="eval-item"><span class="eval-label">{{ 'endless.game.evalNow' | translate }}</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                            }
                          </div>
                        }
                        <div class="play-actions">
                          <button mat-button (click)="toggleEval()">
                            <mat-icon>analytics</mat-icon>
                            {{ (showEval ? 'endless.game.hideEval' : 'endless.game.showEval') | translate }}
                          </button>
                          <button mat-button color="warn" (click)="giveUp()">
                            <mat-icon>flag</mat-icon>
                            {{ 'endless.game.giveUp' | translate }}
                          </button>
                        </div>
                      </div>
                    }
                    @case ('THINKING') {
                      <div class="status-center">
                        <mat-spinner diameter="24"></mat-spinner>
                        <p class="status-text">{{ 'endless.game.opponentThinking' | translate }}</p>
                        @if (showEval) {
                          <div class="eval-compare">
                            <span class="eval-item"><span class="eval-label">{{ 'endless.game.evalStart' | translate }}</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                            <span class="eval-arrow">→</span>
                            <span class="eval-item"><span class="eval-label">{{ 'endless.game.evalNow' | translate }}</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                          </div>
                        }
                        <div class="play-actions">
                          <button mat-button (click)="toggleEval()">
                            <mat-icon>analytics</mat-icon>
                            {{ (showEval ? 'endless.game.hideEval' : 'endless.game.showEval') | translate }}
                          </button>
                          <button mat-button (click)="resetPuzzle()">
                            <mat-icon>replay</mat-icon>
                            {{ 'endless.game.reset' | translate }}
                          </button>
                          @if (!mouseslipUsed && !onSolutionPath) {
                            <button mat-button (click)="mouseslip()">
                              <mat-icon>mouse</mat-icon>
                              {{ 'endless.game.mouseslip' | translate }}
                            </button>
                          }
                          <button mat-button color="warn" (click)="giveUp()">
                            <mat-icon>flag</mat-icon>
                            {{ 'endless.game.giveUp' | translate }}
                          </button>
                        </div>
                      </div>
                    }
                    @case ('PLAYING') {
                      <div class="status-center">
                        <p class="status-text">{{ 'endless.game.yourMove' | translate }}</p>
                        @if (showEval) {
                          <div class="eval-compare">
                            @if (evalLoading) {
                              <mat-spinner diameter="16"></mat-spinner>
                            } @else {
                              <span class="eval-item"><span class="eval-label">{{ 'endless.game.evalStart' | translate }}</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                              <span class="eval-arrow">→</span>
                              <span class="eval-item"><span class="eval-label">{{ 'endless.game.evalNow' | translate }}</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                            }
                          </div>
                        }
                        <div class="play-actions">
                          <button mat-button (click)="toggleEval()">
                            <mat-icon>analytics</mat-icon>
                            {{ (showEval ? 'endless.game.hideEval' : 'endless.game.showEval') | translate }}
                          </button>
                          <button mat-button (click)="resetPuzzle()">
                            <mat-icon>replay</mat-icon>
                            {{ 'endless.game.reset' | translate }}
                          </button>
                          @if (!mouseslipUsed && !onSolutionPath) {
                            <button mat-button (click)="mouseslip()">
                              <mat-icon>mouse</mat-icon>
                              {{ 'endless.game.mouseslip' | translate }}
                            </button>
                          }
                          <button mat-button color="warn" (click)="giveUp()">
                            <mat-icon>flag</mat-icon>
                            {{ 'endless.game.giveUp' | translate }}
                          </button>
                        </div>
                      </div>
                    }
                    @case ('CORRECT') {
                      <div class="status-center solved">
                        <mat-icon class="result-icon">check_circle</mat-icon>
                        @if (alternativeSolve) {
                          <p class="status-text">{{ 'endless.game.checkmate' | translate }}</p>
                          <p class="alt-hint">{{ 'endless.game.alternativeSolution' | translate }}</p>
                        } @else {
                          <p class="status-text">{{ 'endless.game.correct' | translate }}</p>
                        }
                        <div class="review-nav">
                          <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                          <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                          <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                        </div>
                        <div class="alt-actions">
                          @if (reviewingWrongPuzzle) {
                            <button mat-raised-button color="primary" (click)="continueAfterWrong()">
                              <mat-icon>skip_next</mat-icon> {{ 'endless.game.continue' | translate }}
                            </button>
                          } @else {
                            <button mat-raised-button color="primary" (click)="continueAfterSolve()">
                              <mat-icon>arrow_forward</mat-icon> {{ 'endless.game.continue' | translate }}
                            </button>
                          }
                        </div>
                      </div>
                    }
                    @case ('WRONG') {
                      <div class="status-center failed">
                        @if (gaveUp) {
                          <mat-icon class="result-icon gave-up-icon">flag</mat-icon>
                          <p class="status-text">{{ 'endless.game.gaveUp' | translate }}</p>
                        } @else {
                          <mat-icon class="result-icon">cancel</mat-icon>
                          <p class="status-text">{{ 'endless.game.wrong' | translate }}</p>
                        }
                        <div class="review-nav">
                          <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                          <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                          <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                        </div>
                        <div class="wrong-actions">
                          <button mat-stroked-button (click)="analyzeCurrentPuzzle()">
                            <mat-icon>biotech</mat-icon> {{ 'endless.game.analyze' | translate }}
                          </button>
                          <button mat-raised-button color="primary" (click)="continueAfterWrong()">
                            <mat-icon>skip_next</mat-icon> {{ 'endless.game.continue' | translate }}
                          </button>
                        </div>
                      </div>
                    }
                  }
                </mat-card-content>
              </mat-card>

              @if (lastSolvedPuzzleId) {
                <button mat-stroked-button class="review-last-btn" (click)="reviewLastPuzzle()">
                  <mat-icon>history</mat-icon> {{ 'endless.game.reviewLast' | translate }}
                </button>
              }

              @if (puzzle) {
                <mat-card class="info-card">
                  <mat-card-content>
                    <div class="puzzle-info">
                      <span class="rating-badge">{{ 'endless.game.puzzleRating' | translate:{ rating: puzzle.rating } }}</span>
                      <span class="level-badge">{{ 'endless.game.levelRange' | translate:{ level: level, min: currentMinRating, max: currentMaxRating } }}</span>
                      @if (puzzle.themes) {
                        <span class="themes-toggle" (click)="showThemes = !showThemes">
                          {{ (showThemes ? 'endless.game.hideTags' : 'endless.game.showTags') | translate }}
                        </span>
                        @if (showThemes) {
                          <div class="themes">
                            @for (theme of puzzle.themes.split(' '); track theme) {
                              <span class="theme-chip">{{ theme }}</span>
                            }
                          </div>
                        }
                      }
                      <button mat-stroked-button class="share-puzzle-btn" (click)="sharePuzzle()">
                        <mat-icon>share</mat-icon> {{ 'endless.game.sharePuzzle' | translate }}
                      </button>
                    </div>
                  </mat-card-content>
                </mat-card>
              }

              <mat-card class="stats-card">
                <mat-card-content>
                  <div class="stats-grid">
                    <div class="stat">
                      <span class="stat-value">{{ currentRating }}</span>
                      <span class="stat-label">{{ 'endless.game.statRating' | translate }}</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ level }}</span>
                      <span class="stat-label">{{ 'endless.game.statLevel' | translate }}</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ solved }}</span>
                      <span class="stat-label">{{ 'endless.game.statSolved' | translate }}</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ formatTime(sessionSeconds) }}</span>
                      <span class="stat-label">{{ 'endless.game.statTime' | translate }}</span>
                    </div>
                  </div>
                  <div class="lives-display">
                    @for (i of [1,2,3]; track i) {
                      <mat-icon [class]="i <= lives ? 'heart full' : 'heart empty'">
                        {{ i <= lives ? 'favorite' : 'favorite_border' }}
                      </mat-icon>
                    }
                  </div>
                  <div class="phase-indicator">{{ currentPhaseLabel }}</div>
                  <div class="depth-control">
                    <mat-icon>psychology</mat-icon>
                    <input type="range" [min]="1" [max]="24" [(ngModel)]="config.stockfishDepth" (change)="onDepthChange()">
                    <span class="depth-value">{{ config.stockfishDepth }}</span>
                  </div>
                </mat-card-content>
              </mat-card>
            </div>
          </div>
        }

        @case ('exhausted') {
          <div class="gameover-screen">
            <mat-card class="gameover-card exhausted-card">
              <mat-card-header>
                <mat-card-title>{{ 'endless.gameOver.exhaustedTitle' | translate }}</mat-card-title>
                <mat-card-subtitle>{{ 'endless.gameOver.exhaustedSubtitle' | translate }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="stockfish-question">
                  <span class="stockfish-fish">&#x1F41F;</span>
                  <p class="stockfish-text">{{ 'endless.gameOver.areYouAStockfish' | translate }}</p>
                </div>
                <div class="gameover-stats">
                  <div class="gameover-stat">
                    <mat-icon>trending_up</mat-icon>
                    <span class="go-value">{{ maxRatingReached }}</span>
                    <span class="go-label">{{ 'endless.gameOver.maxRating' | translate }}</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>extension</mat-icon>
                    <span class="go-value">{{ solved }}</span>
                    <span class="go-label">{{ 'endless.gameOver.puzzlesSolved' | translate }}</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>favorite</mat-icon>
                    <span class="go-value">{{ lives }}</span>
                    <span class="go-label">{{ 'endless.gameOver.livesLeft' | translate }}</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>timer</mat-icon>
                    <span class="go-value">{{ formatTime(sessionSeconds) }}</span>
                    <span class="go-label">{{ 'endless.gameOver.time' | translate }}</span>
                  </div>
                </div>
                @if (currentSessionPuzzles.length > 0) {
                  <div class="puzzle-review">
                    <h4 class="review-title">{{ 'endless.gameOver.puzzleReview' | translate }}</h4>
                    <div class="review-list">
                      @for (attempt of currentSessionPuzzles; track attempt.puzzleNumber) {
                        <div class="review-item" [class.review-failed]="!attempt.solved" (click)="openPuzzle(attempt.puzzleId)">
                          <span class="review-number">#{{ attempt.puzzleNumber }}</span>
                          <span class="review-rating">{{ attempt.rating }}</span>
                          <mat-icon [class]="attempt.solved ? 'review-icon solved' : 'review-icon failed'">
                            {{ attempt.solved ? 'check_circle' : 'cancel' }}
                          </mat-icon>
                        </div>
                      }
                    </div>
                  </div>
                }
                @if (isNewHighscore) {
                  <div class="new-highscore">
                    <mat-icon>emoji_events</mat-icon>
                    {{ 'endless.gameOver.newHighscore' | translate }}
                  </div>
                }
                @if (authService.isLoggedIn && lastSessionId && !lastSessionArchived) {
                  <button mat-stroked-button color="warn" class="archive-btn" (click)="archiveLastSession()" [disabled]="archiving">
                    <mat-icon>archive</mat-icon>
                    {{ (archiving ? 'endless.gameOver.archiving' : 'endless.gameOver.archiveRun') | translate }}
                  </button>
                }
                @if (lastSessionArchived) {
                  <div class="archived-hint">
                    <mat-icon>check</mat-icon> {{ 'endless.gameOver.archived' | translate }}
                  </div>
                }
                <div class="gameover-actions">
                  <button mat-raised-button color="primary" (click)="playAgain()">
                    <mat-icon>replay</mat-icon>
                    {{ 'endless.gameOver.playAgain' | translate }}
                  </button>
                  <button mat-button (click)="backToPuzzles()">
                    <mat-icon>arrow_back</mat-icon>
                    {{ 'endless.gameOver.backToPuzzles' | translate }}
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          </div>
        }

        @case ('gameover') {
          <div class="gameover-screen">
            <mat-card class="gameover-card">
              <mat-card-header>
                <mat-card-title>{{ 'endless.gameOver.title' | translate }}</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                <div class="gameover-stats">
                  <div class="gameover-stat">
                    <mat-icon>trending_up</mat-icon>
                    <span class="go-value">{{ maxRatingReached }}</span>
                    <span class="go-label">{{ 'endless.gameOver.maxRating' | translate }}</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>extension</mat-icon>
                    <span class="go-value">{{ solved }}</span>
                    <span class="go-label">{{ 'endless.gameOver.puzzlesSolved' | translate }}</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>stacked_line_chart</mat-icon>
                    <span class="go-value">{{ level }}</span>
                    <span class="go-label">{{ 'endless.gameOver.levelReached' | translate }}</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>timer</mat-icon>
                    <span class="go-value">{{ formatTime(sessionSeconds) }}</span>
                    <span class="go-label">{{ 'endless.gameOver.time' | translate }}</span>
                  </div>
                </div>
                @if (currentSessionMistakes.length > 0) {
                  <div class="mistake-ratings">
                    <mat-icon>heart_broken</mat-icon>
                    <span>{{ 'endless.gameOver.livesLostAt' | translate:{ ratings: currentSessionMistakes.join(', ') } }}</span>
                  </div>
                }
                @if (currentSessionPuzzles.length > 0) {
                  <div class="puzzle-review">
                    <h4 class="review-title">{{ 'endless.gameOver.puzzleReview' | translate }}</h4>
                    <div class="review-list">
                      @for (attempt of currentSessionPuzzles; track attempt.puzzleNumber) {
                        <div class="review-item" [class.review-failed]="!attempt.solved" (click)="openPuzzle(attempt.puzzleId)">
                          <span class="review-number">#{{ attempt.puzzleNumber }}</span>
                          <span class="review-rating">{{ attempt.rating }}</span>
                          <mat-icon [class]="attempt.solved ? 'review-icon solved' : 'review-icon failed'">
                            {{ attempt.solved ? 'check_circle' : 'cancel' }}
                          </mat-icon>
                        </div>
                      }
                    </div>
                  </div>
                }
                @if (isNewHighscore) {
                  <div class="new-highscore">
                    <mat-icon>emoji_events</mat-icon>
                    {{ 'endless.gameOver.newHighscore' | translate }}
                  </div>
                }
                @if (authService.isLoggedIn && lastSessionId && !lastSessionArchived) {
                  <button mat-stroked-button color="warn" class="archive-btn" (click)="archiveLastSession()" [disabled]="archiving">
                    <mat-icon>archive</mat-icon>
                    {{ (archiving ? 'endless.gameOver.archiving' : 'endless.gameOver.archiveRun') | translate }}
                  </button>
                }
                @if (lastSessionArchived) {
                  <div class="archived-hint">
                    <mat-icon>check</mat-icon> {{ 'endless.gameOver.archived' | translate }}
                  </div>
                }
                <div class="gameover-actions">
                  <button mat-raised-button color="primary" (click)="playAgain()">
                    <mat-icon>replay</mat-icon>
                    {{ 'endless.gameOver.playAgain' | translate }}
                  </button>
                  <button mat-button (click)="backToPuzzles()">
                    <mat-icon>arrow_back</mat-icon>
                    {{ 'endless.gameOver.backToPuzzles' | translate }}
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .endless-page { padding: 1rem; max-width: 1200px; margin: 0 auto; }

    .help-overlay {
      position: fixed; inset: 0; background: rgba(0,0,0,0.5); z-index: 1000;
      display: flex; justify-content: center; align-items: flex-start; padding: 2rem; overflow-y: auto;
    }
    .help-content {
      background: white; border-radius: 12px; max-width: 600px; width: 100%;
      max-height: 90vh; overflow-y: auto; box-shadow: 0 8px 32px rgba(0,0,0,0.3);
    }
    .help-header {
      display: flex; justify-content: space-between; align-items: center;
      padding: 1rem 1.5rem 0; position: sticky; top: 0; background: white; z-index: 1;
    }
    .help-header h2 { margin: 0; }
    .help-body { padding: 0 1.5rem 1.5rem; }
    .help-body h3 { margin: 1.25rem 0 0.5rem; color: #1976d2; }
    .help-body p { margin: 0.25rem 0; line-height: 1.5; }
    .help-body ul { margin: 0.25rem 0; padding-left: 1.5rem; }
    .help-body li { margin: 0.25rem 0; line-height: 1.5; }
    .help-btn { margin-left: 0.5rem; vertical-align: middle; }

    .config-screen { display: flex; justify-content: center; padding-top: 2rem; }
    .config-card { max-width: 500px; width: 100%; }
    .config-fields { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem 1rem; margin-top: 1rem; }
    .config-fields mat-form-field:last-child { grid-column: 1 / -1; }
    .fasttrack-section { margin-bottom: 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
    .threshold-explain { font-size: 0.8em; color: rgba(0,0,0,0.5); margin: 0 0 0.5rem; }
    .threshold-fields { display: grid; grid-template-columns: 1fr 1fr; gap: 0 1rem; }
    .threshold-field-wrap { position: relative; }
    .auto-hint {
      display: inline-block; font-size: 0.75em; color: #1976d2; cursor: pointer;
      margin-top: -0.75rem; margin-bottom: 0.25rem; padding: 2px 8px;
      border-radius: 8px; background: rgba(25,118,210,0.08);
    }
    .auto-hint:hover { background: rgba(25,118,210,0.16); }
    .fasttrack-preview { display: flex; flex-direction: column; gap: 0.25rem; margin-top: 0.5rem; }
    .fasttrack-phase { display: flex; justify-content: space-between; font-size: 0.85em; }
    .phase-label { font-weight: 500; }
    .phase-detail { color: rgba(0,0,0,0.6); font-variant-numeric: tabular-nums; }
    .level-preview { margin-bottom: 1rem; }
    .level-preview h4 { margin: 0 0 0.5rem; color: rgba(0,0,0,0.6); font-size: 0.9em; }
    .preview-levels { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .preview-chip {
      background: rgba(0,0,0,0.06); border-radius: 12px; padding: 4px 12px;
      font-size: 0.85em; font-variant-numeric: tabular-nums;
    }
    .theme-section { margin-bottom: 1rem; }
    .theme-label { font-size: 0.85em; color: rgba(0,0,0,0.6); margin-bottom: 0.5rem; }
    .viz-slider { margin-bottom: 0.75rem; }
    .viz-slider label { font-size: 0.9em; font-weight: 500; }
    .viz-slider input[type=range] { width: 100%; margin: 0.25rem 0; }
    .viz-level-desc { font-size: 0.8em; color: rgba(0,0,0,0.55); }
    .viz-card {}
    .viz-card .viz-title { display: flex; align-items: center; gap: 0.35rem; font-weight: 600; margin-bottom: 0.4rem; }
    .viz-card .viz-moves { font-family: 'Courier New', monospace; font-size: 1.05em; line-height: 1.5; background: rgba(0,0,0,0.04); border-radius: 6px; padding: 0.5rem 0.6rem; word-break: break-word; }
    .viz-card .viz-hint { font-size: 0.8em; color: rgba(0,0,0,0.55); margin-top: 0.4rem; }
    .viz-countdown { font-size: 0.9em; color: #e65100; font-weight: 500; margin-bottom: 0.25rem; }
    .viz-show-btn {
      margin-top: 0.4rem; padding: 0.35rem 1.2rem; border: 1px solid rgba(0,0,0,0.2);
      border-radius: 6px; background: #fff; cursor: pointer; font-weight: 500;
      user-select: none; touch-action: manipulation;
    }
    .viz-show-btn:active { background: #e3f2fd; }
    .theme-chips { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .theme-chip {
      display: flex; flex-direction: column; align-items: center; gap: 4px;
      cursor: pointer; padding: 6px; border-radius: 8px; border: 2px solid transparent;
      transition: border-color 0.15s;
    }
    .theme-chip.active { border-color: #1976d2; }
    .theme-chip:hover { background: rgba(0,0,0,0.04); }
    .theme-preview { display: flex; width: 32px; height: 16px; border-radius: 3px; overflow: hidden; }
    .piece-preview { width: 28px; height: 28px; background-size: contain; background-repeat: no-repeat; background-position: center; }
    .theme-img { width: 32px; height: 16px; border-radius: 3px; background-size: cover; background-position: center; }
    .settings-bar { display: flex; justify-content: flex-end; }
    .settings-gear.active { color: #1976d2; }
    .tp-light, .tp-dark { flex: 1; }
    .theme-name { font-size: 0.75em; color: rgba(0,0,0,0.7); }
    .config-lives { justify-content: center; margin: 1rem 0; }
    .highscore-badge {
      display: flex; align-items: center; gap: 0.5rem; justify-content: center;
      color: #ff9800; font-weight: 500; margin-bottom: 1rem;
    }
    .session-count { text-align: center; font-size: 0.85em; color: rgba(0,0,0,0.5); margin: 0 0 1rem; display: flex; align-items: center; justify-content: center; gap: 0.5rem; flex-wrap: wrap; }
    .history-link { font-size: 0.85em; min-height: 0; padding: 0 8px; line-height: 28px; }
    .history-link mat-icon { font-size: 16px; width: 16px; height: 16px; margin-right: 2px; }
    .start-btn { width: 100%; height: 48px; font-size: 1.1em; }
    .resume-banner {
      background: rgba(25,118,210,0.06); border: 1px solid rgba(25,118,210,0.2);
      border-radius: 12px; padding: 1rem; margin-bottom: 1rem;
    }
    .resume-info {
      display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.75rem;
      font-size: 0.9em; color: rgba(0,0,0,0.7);
    }
    .resume-info mat-icon { color: #1976d2; }
    .resume-actions { display: flex; flex-direction: column; gap: 0.5rem; }
    .archive-btn { margin-bottom: 0.5rem; }
    .archived-hint {
      display: flex; align-items: center; gap: 0.25rem; justify-content: center;
      color: #4caf50; font-size: 0.9em; margin-bottom: 0.5rem;
    }

    .play-screen { display: flex; gap: 1.5rem; align-items: flex-start; }
    .board-section { flex: 0 0 auto; width: min(60vw, 560px); min-width: 280px; }
    .info-section { flex: 1; min-width: 250px; display: flex; flex-direction: column; gap: 1rem; }
    .status-card { min-height: 80px; }
    .status-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 0.75rem 0; }
    .status-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .result-icon { font-size: 48px; width: 48px; height: 48px; }
    .solved .result-icon { color: #4caf50; }
    .failed .result-icon { color: #f44336; }
    .failed .gave-up-icon { color: #ff9800; }
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .alt-actions { display: flex; gap: 0.5rem; margin-top: 0.5rem; }
    .review-last-btn { width: 100%; height: 40px; font-size: 0.9em; }
    .review-last-btn mat-icon { margin-right: 0.25rem; font-size: 18px; width: 18px; height: 18px; }
    .wrong-actions { display: flex; gap: 0.5rem; margin-top: 0.75rem; }
    .review-nav { display: flex; align-items: center; gap: 0.5rem; }
    .review-counter { font-variant-numeric: tabular-nums; min-width: 56px; text-align: center; }
    .solution-review { color: rgba(0,0,0,0.6); }
    .eval-compare {
      display: flex; align-items: center; gap: 0.5rem;
      padding: 6px 14px; border-radius: 8px; background: rgba(0,0,0,0.04);
      font-variant-numeric: tabular-nums;
    }
    .eval-item { display: flex; flex-direction: column; align-items: center; }
    .eval-label { font-size: 0.7em; color: rgba(0,0,0,0.5); text-transform: uppercase; }
    .eval-value { font-size: 1.2em; font-weight: bold; }
    .eval-arrow { color: rgba(0,0,0,0.3); font-size: 1.2em; }
    .play-actions { display: flex; gap: 0.25rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
    .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem; text-align: center; }
    .stat-value { font-size: 1.3em; font-weight: bold; display: block; }
    .stat-label { font-size: 0.8em; color: rgba(0,0,0,0.6); }
    .lives-display { display: flex; gap: 0.25rem; justify-content: center; margin-top: 0.75rem; }
    .heart { font-size: 28px; width: 28px; height: 28px; }
    .heart.full { color: #f44336; }
    .heart.empty { color: rgba(0,0,0,0.2); }
    .phase-indicator {
      text-align: center; font-size: 0.8em; color: rgba(0,0,0,0.5);
      margin-top: 0.25rem; font-style: italic;
    }
    .depth-control {
      display: flex; align-items: center; justify-content: center; gap: 0.4rem;
      margin-top: 0.5rem; font-size: 0.85em; color: rgba(0,0,0,0.6);
    }
    .depth-control mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .depth-control input[type="range"] { width: 100px; cursor: pointer; }
    .depth-value { font-weight: bold; min-width: 1.5em; text-align: center; }
    .puzzle-info { display: flex; flex-direction: column; gap: 0.5rem; position: relative; }
    .rating-badge { font-weight: bold; font-size: 1.1em; }
    .share-puzzle-btn { margin-top: 0.25rem; }
    .level-badge { font-size: 0.9em; color: rgba(0,0,0,0.6); }
    .themes { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .theme-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
    .themes-toggle {
      font-size: 0.8em; color: #1976d2; cursor: pointer;
    }
    .themes-toggle:hover { text-decoration: underline; }

    .gameover-screen { display: flex; justify-content: center; padding-top: 2rem; }
    .gameover-card { max-width: 500px; width: 100%; text-align: center; }
    .gameover-stats { display: grid; grid-template-columns: repeat(2, 1fr); gap: 1.5rem; margin: 1.5rem 0; }
    .gameover-stat { display: flex; flex-direction: column; align-items: center; gap: 0.25rem; }
    .gameover-stat mat-icon { color: rgba(0,0,0,0.5); }
    .go-value { font-size: 1.5em; font-weight: bold; }
    .go-label { font-size: 0.85em; color: rgba(0,0,0,0.6); }
    .mistake-ratings {
      display: flex; align-items: center; gap: 0.5rem; justify-content: center;
      color: #f44336; font-size: 0.9em; margin-bottom: 1rem;
    }
    .new-highscore {
      display: flex; align-items: center; gap: 0.5rem; justify-content: center;
      color: #ff9800; font-size: 1.2em; font-weight: bold; margin-bottom: 1rem;
    }
    .gameover-actions { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 1rem; }
    .stockfish-question { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; margin: 1.5rem 0; }
    .stockfish-fish { font-size: 4rem; }
    .stockfish-text { font-size: 1.3em; font-weight: bold; color: #1976d2; margin: 0; }

    .puzzle-review { margin: 1rem 0; text-align: left; }
    .review-title { margin: 0 0 0.5rem; font-size: 0.95em; color: rgba(0,0,0,0.6); }
    .review-list {
      max-height: 240px; overflow-y: auto; border: 1px solid rgba(0,0,0,0.08);
      border-radius: 8px;
    }
    .review-item {
      display: flex; align-items: center; gap: 0.75rem; padding: 8px 12px;
      cursor: pointer; transition: background 0.15s;
      font-variant-numeric: tabular-nums;
    }
    .review-item:hover { background: rgba(0,0,0,0.04); }
    .review-item:not(:last-child) { border-bottom: 1px solid rgba(0,0,0,0.06); }
    .review-failed { background: rgba(244,67,54,0.06); }
    .review-failed:hover { background: rgba(244,67,54,0.12); }
    .review-number { font-weight: 500; min-width: 32px; color: rgba(0,0,0,0.6); }
    .review-rating { flex: 1; font-weight: bold; }
    .review-icon { font-size: 20px; width: 20px; height: 20px; }
    .review-icon.solved { color: #4caf50; }
    .review-icon.failed { color: #f44336; }

    @media (max-width: 768px) {
      .endless-page { padding: 0.5rem; }
      .play-screen { flex-direction: column; }
      .board-section { width: 100%; }
      .stats-grid { grid-template-columns: repeat(2, 1fr); }
      .config-fields { grid-template-columns: 1fr; }
      .config-screen { padding-top: 0.5rem; }
      .gameover-stats { grid-template-columns: repeat(2, 1fr); gap: 1rem; }
      .gameover-card { text-align: center; }
      .help-overlay { padding: 0.5rem; }
      .help-content { border-radius: 8px; }
      .threshold-fields { grid-template-columns: 1fr; }
    }
  `]
})
export class EndlessPuzzleComponent extends BasePuzzleSolver implements OnDestroy {
  get screen(): 'config' | 'play' | 'gameover' | 'exhausted' {
    if (this.state === 'EXHAUSTED') return 'exhausted';
    if (this.state === 'GAME_OVER') return 'gameover';
    if (this.state === 'CONFIG') return 'config';
    return 'play';
  }

  config: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16 };

  lives = 3;
  level = 0;
  solved = 0;
  maxRatingReached = 0;
  isNewHighscore = false;
  highscore = 0;

  // Board theme
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  showSettings = false;
  themeMode: ThemeMode = 'fixed';
  readonly pieceSets = PIECE_SETS;

  // Help
  showHelp = false;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';

  // Session timer
  sessionSeconds = 0;
  private sessionInterval?: ReturnType<typeof setInterval>;
  private sessionStart = 0;

  // Session history
  sessionHistory: EndlessSession[] = [];
  currentSessionMistakes: number[] = [];
  currentSessionPuzzles: EndlessPuzzleAttempt[] = [];
  showThemes = false;

  // Fasttrack
  fasttrackPhase1Step = 0;
  fasttrackPhase2Step = 0;
  fasttrackAvgFirst = 0;
  fasttrackAvgSecond = 0;
  fasttrackAutoFirst = 0;
  fasttrackAutoSecond = 0;

  // Dynamic rating
  _currentMinRating = 0;

  // Resume
  activeGameState: any = null;

  // Archive
  lastSessionId: number | null = null;
  lastSessionArchived = false;
  archiving = false;

  // Puzzle DB range
  puzzleRange: PuzzleRatingRange = { min: 100, max: 3000 };

  // Board (Brett/Viz/Lös-State in BasePuzzleSolver)
  puzzle: PuzzleDto | null = null;
  private initialFen = '';
  private prefetchedPuzzle: PuzzleDto | null = null;
  /** Vorab geladene Puzzles für Offline-Spiel (ein ganzer Run). */
  private offlinePool: PuzzleDto[] = [];
  reviewingWrongPuzzle = false;
  reviewMode = false;
  reviewIndex = 0;
  gaveUp = false;
  private puzzleStartTime = 0;

  // Zuletzt gelöstes Puzzle — für „Letztes Puzzle analysieren" (bleibt auch nach dem
  // Auto-Advance auf das nächste Puzzle erhalten, da der CORRECT-Status nur kurz sichtbar ist).
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private storage: EndlessStorageService,
    public authService: AuthService,
    private prefs: PreferencesService,
    public router: Router,
    private dialog: MatDialog,
    private translate: TranslateService,
    private offline: OfflineService,
    private snackBar: MatSnackBar,
    private offlineQueue: OfflineQueueService
  ) {
    super(stockfish);
    this.state = 'CONFIG';
    // Load board theme from preferences service
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.visualizationMode = this.prefs.visualization;
    // 1. Load from localStorage immediately (no latency)
    this.config = this.storage.loadConfig(this.config);
    this.highscore = this.storage.loadHighscore();
    this.sessionHistory = this.storage.loadSessionHistory();
    this.offlinePool = this.storage.loadOfflinePool();   // evtl. vorhandener Run-Cache (Offline/Resume)
    this.computeFasttrackSteps();

    // Load local active game state for immediate display
    const localGame = this.storage.loadActiveGameLocal();
    if (localGame) this.activeGameState = localGame;
    // Defensiv: ein resumebarer Run mit 0 Lives existiert nicht — entweder
    // Zombie-State aus aelterer Logik oder Race vor endGame(). Aufraeumen.
    if (this.activeGameState && this.activeGameState.lives <= 0) {
      this.activeGameState = null;
      this.storage.saveActiveGameLocal(null);
    }

    // 2. Load from server (async) and merge
    this.storage.loadFromServer().subscribe(serverData => {
      if (serverData) {
        if (serverData.progress || serverData.sessions.length > 0) {
          const merged = this.storage.mergeServerData(
            this.config, this.highscore, this.sessionHistory, serverData
          );
          this.config = this.storage.loadConfig(merged.config);
          this.highscore = merged.highscore;
          this.sessionHistory = merged.history;
          this.computeFasttrackSteps();

          // Server active game state takes priority
          if (serverData.progress?.activeGameState) {
            try { this.activeGameState = JSON.parse(serverData.progress.activeGameState); } catch {}
          }
          // Auch Server-State auf 0-Lives-Zombie pruefen (Legacy aus aelteren Builds).
          if (this.activeGameState && this.activeGameState.lives <= 0) {
            this.activeGameState = null;
            this.storage.saveActiveGameLocal(null);
            this.storage.saveProgressImmediate(this.config, this.highscore, null);
          }
        } else {
          // Server empty: migrate localStorage data up (one-time)
          this.storage.migrateLocalToServer(this.config, this.highscore, this.sessionHistory);
        }
      }
    });

    this.puzzleService.getRatingRange().subscribe({
      next: r => {
        this.puzzleRange = r;
        this.clampConfig();
        // Schon beim Öffnen der Config einen Run vorab laden, damit Endless später
        // auch offline gestartet werden kann (nur online + wenn noch kein Cache da ist).
        if (navigator.onLine && this.offlinePool.length === 0) this.prefetchRun();
      },
      error: () => {}
    });
    this.stockfish.init().catch(() => {});
  }

  ngOnDestroy(): void {
    this.stopSessionTimer();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  // --- Config ---

  get currentMinRating(): number { return this._currentMinRating; }
  get currentMaxRating(): number { return this._currentMinRating + RATING_WINDOW; }
  get currentRating(): number { return this._currentMinRating; }

  get currentPhaseLabel(): string {
    if (this.solved < 5) return this.translate.instant('endless.game.phaseLabel', { phase: 1, step: this.fasttrackPhase1Step });
    if (this.solved < 10) return this.translate.instant('endless.game.phaseLabel', { phase: 2, step: this.fasttrackPhase2Step });
    return this.translate.instant('endless.game.phaseLabel', { phase: 3, step: 20 });
  }

  private clampConfig(): void {
    this.config.startElo = Math.max(this.puzzleRange.min, Math.min(this.puzzleRange.max, this.config.startElo));
  }

  setBoardTheme(theme: string): void {
    this.boardTheme = theme;
    this.prefs.setBoardTheme(theme);
  }

  setPieceSet(set: string): void {
    this.pieceSet = set;
    this.prefs.setPieceSet(set);
  }

  setThemeMode(mode: ThemeMode): void {
    this.themeMode = mode;
    this.prefs.setThemeMode(mode);
    const applied = applyThemeMode(mode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.puzzle && this.isSolving) this.setupPuzzle(this.puzzle);  // laufendes Puzzle neu starten
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.config.stockfishDepth; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.initialFen = this.chess.fen();
    this.puzzleStartTime = Date.now();
  }

  protected override handleSolved(alternative: boolean): void { this.puzzleSolved(alternative); }
  protected override handleFailed(): void { this.loseLife(); }

  // --- Game lifecycle ---

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/${this.puzzle.id}`;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url }, width: '400px' });
  }

  startGame(): void {
    this.clampConfig();
    this.saveConfig();
    this.lives = 3;
    this.level = 0;
    this.solved = 0;
    this._currentMinRating = this.config.startElo;
    this.maxRatingReached = this.config.startElo;
    this.isNewHighscore = false;
    this.prefetchedPuzzle = null;
    this.sessionSeconds = 0;
    this.currentSessionMistakes = [];
    this.currentSessionPuzzles = [];
    this.activeGameState = null;
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
    this.startSessionTimer();
    this.syncActiveGameToServer();
    this.prefetchRun();
    this.loadPuzzle();
  }

  /**
   * Lädt im Hintergrund einen ganzen Run an Puzzles vorab und legt ihn im Storage ab,
   * damit auch offline gepuzzelt werden kann. Run-Größe = max(gelöste der letzten 5 Runs) + 10.
   */
  private prefetchRun(): void {
    const runs = Math.max(1, this.offline.endlessRuns);   // konfigurierbar im Profil (Standard 2)
    const windows = buildEndlessRunWindows(this.config, this.sessionHistory, this.puzzleRange.max, runs);
    if (!windows.length) return;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandomBatch(windows, themes).subscribe({
      next: pool => { this.offlinePool = pool || []; this.storage.saveOfflinePool(this.offlinePool); },
      error: () => { /* offline/Fehler: bestehenden Pool behalten */ }
    });
  }

  resumeGame(): void {
    if (!this.activeGameState) return;
    const g = this.activeGameState;
    this.lives = g.lives ?? 3;
    this.solved = g.solved ?? 0;
    this.level = g.level ?? 0;
    this._currentMinRating = g.currentMinRating ?? this.config.startElo;
    this.maxRatingReached = g.maxRatingReached ?? this._currentMinRating;
    this.sessionSeconds = g.sessionSeconds ?? 0;
    this.currentSessionMistakes = g.mistakes ?? [];
    this.currentSessionPuzzles = [];
    this.isNewHighscore = false;
    this.prefetchedPuzzle = null;
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
    // Resume timer from where it left off
    this.sessionStart = Date.now() - this.sessionSeconds * 1000;
    this.sessionInterval = setInterval(() => {
      this.sessionSeconds = Math.floor((Date.now() - this.sessionStart) / 1000);
    }, 1000);
    this.loadPuzzle();
  }

  archiveAndStartNew(): void {
    if (!this.activeGameState) { this.startGame(); return; }
    const g = this.activeGameState;
    const session: EndlessSession = {
      timestamp: Date.now(),
      config: { ...this.config },
      totalSolved: g.solved ?? 0,
      maxRating: g.maxRatingReached ?? 0,
      durationSeconds: g.sessionSeconds ?? 0,
      mistakeAtRatings: g.mistakes ?? []
    };
    this.storage.recordSessionToServer(session).subscribe(id => {
      if (id && this.authService.isLoggedIn) {
        this.storage.archiveSession(id).subscribe();
      }
      this.activeGameState = null;
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.sessionHistory = this.storage.recordSession(this.sessionHistory, session);
      this.startGame();
    });
  }

  archiveLastSession(): void {
    if (!this.lastSessionId || this.archiving) return;
    this.archiving = true;
    this.storage.archiveSession(this.lastSessionId).subscribe(() => {
      this.lastSessionArchived = true;
      this.archiving = false;
    });
  }

  playAgain(): void {
    this.state = 'CONFIG';
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
  }

  backToPuzzles(): void { this.router.navigate(['/puzzles']); }

  openPuzzle(id: number): void {
    this.router.navigate(['/puzzles', id]);
  }

  // --- Loading ---

  private loadPuzzle(): void {
    this.state = 'LOADING';
    this.alternativeSolve = false;
    this.showEval = false;
    this.showThemes = false;
    this.initialEval = '';
    this.currentEval = '';

    // Check if we exceeded the max puzzle rating
    if (this._currentMinRating > this.puzzleRange.max) {
      this.stopSessionTimer();
      this.checkHighscore();
      this.recordSession();
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.state = 'EXHAUSTED';
      return;
    }

    const min = this._currentMinRating;
    const max = this._currentMinRating + RATING_WINDOW;

    // Offline: ausschließlich aus dem vorab geladenen Run-Pool bedienen.
    if (!navigator.onLine) {
      const pooled = takeFromPool(this.offlinePool, min, max)
        ?? takeNearestFromPool(this.offlinePool, (min + max) / 2);   // sonst rating-nächstes statt blind shift()
      if (pooled) {
        this.storage.saveOfflinePool(this.offlinePool);
        this.prefetchedPuzzle = null;
        this.onPuzzleLoaded(pooled);
      } else if (this.solved === 0) {
        // Offline gestartet, aber kein Run gecacht → kein „Run beendet", sondern Hinweis + zurück zur Config.
        this.stopSessionTimer();
        this.storage.saveActiveGameLocal(null);
        this.snackBar.open(this.translate.instant('endless.offlineNoCache'), this.translate.instant('common.ok'), { duration: 5000 });
        this.state = 'CONFIG';
      } else {
        // Mitten im Run offline und Pool leer → Run hier regulär beenden.
        this.stopSessionTimer();
        this.checkHighscore();
        this.recordSession();
        this.storage.saveActiveGameLocal(null);
        this.state = 'EXHAUSTED';
      }
      return;
    }

    if (this.prefetchedPuzzle &&
        this.prefetchedPuzzle.rating >= min &&
        this.prefetchedPuzzle.rating <= max) {
      const p = this.prefetchedPuzzle;
      this.prefetchedPuzzle = null;
      this.onPuzzleLoaded(p);
      return;
    }

    this.prefetchedPuzzle = null;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandom(min, max, themes).subscribe({
      next: p => this.onPuzzleLoaded(p),
      error: () => this.onPuzzleLoadError()
    });
  }

  private onPuzzleLoadError(): void {
    // No puzzles in this range — skip to next range
    this._currentMinRating += this.getCurrentStep();
    this.level++;
    // Check if we've gone beyond the max
    if (this._currentMinRating > this.puzzleRange.max) {
      this.stopSessionTimer();
      this.checkHighscore();
      this.recordSession();
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.state = 'EXHAUSTED';
      return;
    }
    this.loadPuzzle();
  }

  private onPuzzleLoaded(puzzle: PuzzleDto): void {
    this.puzzle = puzzle;
    this.trackMaxRating(puzzle.rating);
    this.setupPuzzle(puzzle);
    this.prefetchNext();
  }

  private prefetchNext(): void {
    const nextSolved = this.solved + 1;
    const nextStep = this.getStepForSolved(nextSolved);
    const min = this._currentMinRating + nextStep;
    const max = min + RATING_WINDOW;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandom(min, max, themes).subscribe({
      next: p => this.prefetchedPuzzle = p,
      error: () => this.prefetchedPuzzle = null
    });
  }

  // --- Puzzle setup ---

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewingWrongPuzzle = false;
    this.gaveUp = false;
    this.reviewMode = false;
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  private puzzleSolved(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.state = 'CORRECT';
    this.solved++;
    if (this.puzzle) {
      this.pushSessionPuzzle(true);
      // Für „Letztes Puzzle analysieren" merken (überlebt den Auto-Advance).
      this.lastSolvedPuzzleId = this.puzzle.id;
      this.lastSolvedFen = this.puzzle.fen;
      this.lastSolvedMoves = this.puzzle.moves;
      this.lastSolvedOrientation = this.orientation;
    }
    this.recordAttempt(true);
    this.syncActiveGameToServer();
    this.updateBoard();
    this.enterSolutionReview();

    if (alternative) {
      // Don't auto-advance — let user choose Continue or Show Solution
      return;
    }

    this.autoAdvanceTimer = setTimeout(() => {
      this._currentMinRating += this.getCurrentStep();
      this.level++;
      this.loadPuzzle();
    }, 800);
  }

  continueAfterSolve(): void {
    this.reviewMode = false;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);  // pending Auto-Advance verwerfen
    this._currentMinRating += this.getCurrentStep();
    this.level++;
    this.loadPuzzle();
  }

  continueAfterWrong(): void {
    this.reviewMode = false;
    this.reviewingWrongPuzzle = false;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.lives <= 0) {
      this.endGame();
    } else {
      this.prefetchedPuzzle = null;
      this.loadPuzzle();
    }
  }

  private enterSolutionReview(): void {
    this.reviewMode = true;
    this.reviewIndex = this.reviewTotal;
  }

  /** Aktuelles Puzzle (z.B. nach dem Aufgeben) im Analysemodus öffnen. */
  analyzeCurrentPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.puzzle) return;
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.puzzle.fen,
        moves: this.puzzle.moves.split(' ').filter(m => m).join(','),
        orientation: this.orientation,
        from: '/puzzles/endless',
      },
    });
  }

  /** Zuletzt gelöstes Puzzle im Analysemodus öffnen (auch nach dem Auto-Advance verfügbar). */
  reviewLastPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.lastSolvedFen) return;
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.lastSolvedFen,
        moves: this.lastSolvedMoves.split(' ').filter(m => m).join(','),
        orientation: this.lastSolvedOrientation,
        from: '/puzzles/endless',
      },
    });
  }

  showIntendedSolution(): void {
    if (!this.puzzle) return;
    if (this.state === 'WRONG') this.reviewingWrongPuzzle = true;
    this.state = 'CORRECT';
    this.reviewMode = true;
    this.reviewGoTo(0);
  }

  get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  reviewNext(): void { if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex + 1); }
  reviewPrev(): void { if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex - 1); }

  private reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    const moves = this.puzzle.moves.split(' ').filter(m => m);
    index = Math.max(0, Math.min(index, moves.length));
    this.reviewIndex = index;
    this.chess = new Chess(this.puzzle.fen);
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      this.applyUci(moves[i]);
      last = [moves[i].substring(0, 2) as Key, moves[i].substring(2, 4) as Key];
    }
    this.lastMove = last;
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    this.dests = new Map();
  }

  exitReview(): void {
    this.reviewMode = false;
  }

  /** Zug aufs Brett anwenden ohne lastMove-Highlight (Review-Aufbau). */
  private applyUci(uci: string): void {
    applyUci(this.chess, uci);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'CORRECT' && this.state !== 'WRONG') return;
    if (e.key === 'ArrowLeft') this.reviewPrev();
    if (e.key === 'ArrowRight') this.reviewNext();
  }

  private loseLife(): void {
    this.currentSessionMistakes.push(this._currentMinRating);
    this.pushSessionPuzzle(false);
    this.lives--;
    this.recordAttempt(false);
    // Bei 0 Lives ist der Run faktisch vorbei — nicht den Zombie-State (0 Lives) auf den
    // Server schreiben. endGame() raeumt nach Klick auf Continue endgueltig auf; falls der
    // User vorher die Seite verlaesst, ist dann kein 0-Lives-Run als "unfinished" gemerkt.
    if (this.lives > 0) {
      this.syncActiveGameToServer();
    } else {
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
    }
    this.state = 'WRONG';
    this.updateBoard();
    this.enterSolutionReview();
  }

  // --- Buttons ---

  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval && (this.state === 'PLAYING' || this.state === 'AWAITING_USER_MOVE')) {
      this.refreshEval();
    }
  }

  private async refreshEval(): Promise<void> {
    this.evalLoading = true;
    try {
      if (!this.initialEval && this.initialFen) {
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.config.stockfishDepth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.config.stockfishDepth);
    } catch {}
    this.evalLoading = false;
  }

  onDepthChange(): void {
    if (this.config.stockfishDepth < 1) this.config.stockfishDepth = 1;
    if (this.config.stockfishDepth > 24) this.config.stockfishDepth = 24;
    this.saveConfig();
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    // Reset costs a life
    this.lives--;
    if (this.lives <= 0) {
      this.currentSessionMistakes.push(this._currentMinRating);
      this.pushSessionPuzzle(false);
      this.recordAttempt(false);
      // 0 Lives = Run ist vorbei. Active-State (mit jetzt veralteten Werten) auf
      // dem Server loeschen, damit kein "Unfinished run | 0 lives"-Zombie zurueckbleibt.
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.state = 'WRONG';
      this.updateBoard();
      this.enterSolutionReview();
      return;
    }
    this.setupPuzzle(this.puzzle);
  }

  giveUp(): void {
    this.abortSolver();
    this.gaveUp = true;
    this.loseLife();
  }

  private endGame(): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.recordSession();
    // Clear active game and sync final state to server
    this.storage.saveActiveGameLocal(null);
    this.storage.saveProgressImmediate(this.config, this.highscore, null);
    this.state = 'GAME_OVER';
  }

  // --- Step calculation ---

  private getCurrentStep(): number {
    return this.getStepForSolved(this.solved);
  }

  private getStepForSolved(solvedCount: number): number {
    if (solvedCount <= 5) return this.fasttrackPhase1Step;
    if (solvedCount <= 10) return this.fasttrackPhase2Step;
    return 20;
  }

  private computeFasttrackSteps(): void {
    const auto = autoFasttrackThresholds(this.config, this.sessionHistory);
    this.fasttrackAutoFirst = auto.first;
    this.fasttrackAutoSecond = auto.second;
    // Manuelle Overrides aus der Config, sonst Auto-Werte
    this.fasttrackAvgFirst = this.config.fasttrackThreshold1 ?? this.fasttrackAutoFirst;
    this.fasttrackAvgSecond = this.config.fasttrackThreshold2 ?? this.fasttrackAutoSecond;
    this.recalcStepsFromThresholds();
  }

  onThresholdChange(): void {
    // Persist manual overrides
    this.config.fasttrackThreshold1 = this.fasttrackAvgFirst !== this.fasttrackAutoFirst
      ? this.fasttrackAvgFirst : undefined;
    this.config.fasttrackThreshold2 = this.fasttrackAvgSecond !== this.fasttrackAutoSecond
      ? this.fasttrackAvgSecond : undefined;
    this.recalcStepsFromThresholds();
  }

  resetThreshold(which: number): void {
    if (which === 1) {
      this.fasttrackAvgFirst = this.fasttrackAutoFirst;
      this.config.fasttrackThreshold1 = undefined;
    } else {
      this.fasttrackAvgSecond = this.fasttrackAutoSecond;
      this.config.fasttrackThreshold2 = undefined;
    }
    this.recalcStepsFromThresholds();
  }

  private recalcStepsFromThresholds(): void {
    const steps = fasttrackSteps(this.config.startElo, this.fasttrackAvgFirst, this.fasttrackAvgSecond);
    this.fasttrackPhase1Step = steps.phase1Step;
    this.fasttrackPhase2Step = steps.phase2Step;
  }

  // --- Timer ---

  private startSessionTimer(): void {
    this.sessionStart = Date.now();
    this.sessionSeconds = 0;
    this.sessionInterval = setInterval(() => {
      this.sessionSeconds = Math.floor((Date.now() - this.sessionStart) / 1000);
    }, 1000);
  }

  private stopSessionTimer(): void {
    if (this.sessionInterval) {
      clearInterval(this.sessionInterval);
      this.sessionInterval = undefined;
    }
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  // --- Tracking ---

  private trackMaxRating(rating: number): void {
    if (rating > this.maxRatingReached) this.maxRatingReached = rating;
  }

  private recordAttempt(solved: boolean): void {
    if (!this.puzzle) return;
    const timeSpent = this.puzzleStartTime > 0 ? Math.floor((Date.now() - this.puzzleStartTime) / 1000) : 0;
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    const id = this.puzzle.id;
    const loggedIn = this.authService.isLoggedIn;
    const url = loggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
    const body: Record<string, unknown> = {
      solved, timeSpentSeconds: timeSpent, moveLog: log ?? null, visualizationLevel: this.visualizationMode,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight,
    };
    if (!loggedIn) body['sessionId'] = this.puzzleService.ensureSessionId();
    // Offline gelöste Endless-Puzzles nicht verlieren → vormerken (Sync bei Reconnect).
    if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
    const obs = loggedIn
      ? this.puzzleService.recordAttempt(id, solved, timeSpent, log, this.visualizationMode)
      : this.puzzleService.recordAnonymousAttempt(id, solved, timeSpent, log, this.visualizationMode);
    obs.subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
  }

  // --- localStorage ---

  private saveConfig(): void {
    this.storage.saveConfig(this.config);
    this.storage.saveProgressToServer(this.config, this.highscore, null);
  }

  private checkHighscore(): void {
    const result = this.storage.checkHighscore(this.maxRatingReached, this.highscore);
    this.highscore = result.highscore;
    if (result.isNew) this.isNewHighscore = true;
  }

  private syncActiveGameToServer(): void {
    const gameState = {
      lives: this.lives,
      solved: this.solved,
      level: this.level,
      currentMinRating: this._currentMinRating,
      maxRatingReached: this.maxRatingReached,
      sessionSeconds: this.sessionSeconds,
      mistakes: this.currentSessionMistakes
    };
    this.storage.saveActiveGameLocal(gameState);
    this.storage.saveProgressToServer(this.config, this.highscore, gameState);
  }

  private recordSession(): void {
    const session: EndlessSession = {
      timestamp: Date.now(),
      config: { ...this.config },
      totalSolved: this.solved,
      maxRating: this.maxRatingReached,
      durationSeconds: this.sessionSeconds,
      mistakeAtRatings: [...this.currentSessionMistakes]
    };
    this.sessionHistory = this.storage.recordSession(this.sessionHistory, session);
    // Per-Puzzle-Daten (mit Start-/Lösungszeit) nur an den Server für das Logging mitgeben,
    // nicht in die lokale History (würde localStorage aufblähen).
    this.storage.recordSessionToServer(session, this.currentSessionPuzzles).subscribe(id => {
      if (id) this.lastSessionId = id;
    });
  }

  /** Hängt das aktuelle Puzzle (mit Start-/Endzeit) an die Session-Liste an. */
  private pushSessionPuzzle(solved: boolean): void {
    if (!this.puzzle) return;
    const now = Date.now();
    this.currentSessionPuzzles.push({
      puzzleNumber: this.currentSessionPuzzles.length + 1,
      puzzleId: this.puzzle.id,
      lichessId: this.puzzle.lichessId,
      rating: this.puzzle.rating,
      solved,
      themes: this.puzzle.themes,
      startedAt: this.puzzleStartTime > 0 ? this.puzzleStartTime : now,
      endedAt: now,
    });
  }
}
