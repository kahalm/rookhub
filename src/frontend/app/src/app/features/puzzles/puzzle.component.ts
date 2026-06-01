import { Component, OnInit, OnDestroy, ViewChild, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatInputModule } from '@angular/material/input';
import { Router, ActivatedRoute } from '@angular/router';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleStatsDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { of } from 'rxjs';

type PuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'ERROR';

// Schwierigkeit → Elo-Offset des Fenster-Zentrums; Fenster ±RATING_WINDOW um (Elo + Offset).
const DIFFICULTY_OFFSET: Record<string, number> = {
  sehr_leicht: -600, leicht: -300, normal: 0, schwer: 300, sehr_schwer: 600,
};
const RATING_WINDOW = 100;

@Component({
  selector: 'app-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule,
    MatChipsModule, MatSlideToggleModule, MatDialogModule, PuzzleBoardComponent
  ],
  template: `
    <div class="puzzle-page">
      <div class="puzzle-layout">
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
            [visualization]="(state !== 'SOLVED' && state !== 'FAILED') ? visualizationMode : 0"
            (moveMade)="onMoveMade($event)"
          />
        </div>

        <div class="info-section">
          @if (visualizationMode && (state === 'AWAITING_USER_MOVE' || state === 'THINKING' || state === 'PLAYING' || state === 'SOLVED' || state === 'FAILED')) {
            <mat-card class="viz-card">
              <mat-card-content>
                <div class="viz-title"><mat-icon>visibility_off</mat-icon> Visualisierung (Level {{ visualizationMode }})</div>
                @if (vizCountdownSeconds > 0) {
                  <div class="viz-countdown">Figuren verschwinden in {{ vizCountdownSeconds }}s...</div>
                }
                <div class="viz-moves">{{ vizMoveText || 'Noch kein Zug — klick Von-Feld → Ziel-Feld.' }}</div>
                @if (vizPiecesHidden) {
                  <button class="viz-show-btn" (click)="onVizShow()">
                    {{ vizShowPressed ? 'Showing...' : 'Show' }}
                  </button>
                }
                <div class="viz-hint">{{ vizLevelDescription }}</div>
              </mat-card-content>
            </mat-card>
          }
          <mat-card class="status-card">
            <mat-card-content>
              <button mat-icon-button class="settings-gear" [class.active]="showSettings" (click)="toggleSettings()" title="Einstellungen">
                <mat-icon>settings</mat-icon>
              </button>
              @switch (state) {
                @case ('LOADING') {
                  <div class="status-center">
                    <mat-spinner diameter="40"></mat-spinner>
                    <p>Loading puzzle...</p>
                  </div>
                }
                @case ('ERROR') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">error_outline</mat-icon>
                    <p class="status-text">Failed to load puzzle</p>
                    <button mat-raised-button color="primary" (click)="loadNext()">
                      <mat-icon>refresh</mat-icon> Retry
                    </button>
                  </div>
                }
                @case ('SETUP') {
                  <div class="status-center">
                    <p class="status-text">Watch the opponent's move...</p>
                  </div>
                }
                @case ('AWAITING_USER_MOVE') {
                  <div class="status-center">
                    <p class="status-text">Your turn!</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    @if (showEval) {
                      <div class="eval-compare">
                        <span class="eval-item"><span class="eval-label">Start</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                        <span class="eval-arrow">→</span>
                        <span class="eval-item"><span class="eval-label">Now</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                      </div>
                    }
                    <div class="play-actions">
                      <button mat-button (click)="toggleEval()">
                        <mat-icon>analytics</mat-icon>
                        {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                      </button>
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      @if (!mouseslipUsed) {
                        <button mat-button (click)="mouseslip()">
                          <mat-icon>mouse</mat-icon>
                          Mouseslip
                        </button>
                      }
                      <button mat-button color="warn" (click)="giveUp()">
                        <mat-icon>flag</mat-icon>
                        Give Up
                      </button>
                    </div>
                  </div>
                }
                @case ('THINKING') {
                  <div class="status-center">
                    <mat-spinner diameter="24"></mat-spinner>
                    <p class="status-text">Stockfish denkt...</p>
                    @if (showEval) {
                      <div class="eval-compare">
                        <span class="eval-item"><span class="eval-label">Start</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                        <span class="eval-arrow">→</span>
                        <span class="eval-item"><span class="eval-label">Now</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                      </div>
                    }
                    <div class="play-actions">
                      <button mat-button (click)="toggleEval()">
                        <mat-icon>analytics</mat-icon>
                        {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                      </button>
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      <button mat-button color="warn" (click)="giveUp()">
                        <mat-icon>flag</mat-icon>
                        Give Up
                      </button>
                    </div>
                  </div>
                }
                @case ('PLAYING') {
                  <div class="status-center">
                    <p class="status-text">Your turn!</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    @if (showEval) {
                      <div class="eval-compare">
                        @if (evalLoading) {
                          <mat-spinner diameter="16"></mat-spinner>
                        } @else {
                          <span class="eval-item"><span class="eval-label">Start</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                          <span class="eval-arrow">→</span>
                          <span class="eval-item"><span class="eval-label">Now</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                        }
                      </div>
                    }
                    <div class="play-actions">
                      <button mat-button (click)="toggleEval()">
                        <mat-icon>analytics</mat-icon>
                        {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                      </button>
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      @if (!mouseslipUsed) {
                        <button mat-button (click)="mouseslip()">
                          <mat-icon>mouse</mat-icon>
                          Mouseslip
                        </button>
                      }
                      <button mat-button color="warn" (click)="giveUp()">
                        <mat-icon>flag</mat-icon>
                        Give Up
                      </button>
                    </div>
                  </div>
                }
                @case ('SOLVED') {
                  <div class="status-center solved">
                    <mat-icon class="result-icon">check_circle</mat-icon>
                    @if (alternativeSolve) {
                      <p class="status-text">Schachmatt!</p>
                      <p class="alt-hint">Alternative Lösung — das Puzzle hatte eine andere beabsichtigte Zugfolge.</p>
                    } @else {
                      <p class="status-text">Correct!</p>
                    }
                    @if (lastEloChange != null) {
                      <span class="elo-change elo-up">+{{ lastEloChange }}</span>
                    }
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    <div class="review-nav">
                      <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                      <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                      <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                    </div>
                    <div class="solved-actions">
                      <button mat-raised-button color="primary" (click)="loadNext()">
                        Next Puzzle @if (solvedCountdown > 0) { ({{ solvedCountdown }}) }
                      </button>
                    </div>
                  </div>
                }
                @case ('FAILED') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">cancel</mat-icon>
                    <p class="status-text">Incorrect</p>
                    @if (lastEloChange != null) {
                      <span class="elo-change elo-down">{{ lastEloChange }}</span>
                    }
                    <div class="review-nav">
                      <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                      <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                      <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                    </div>
                    <div class="fail-actions">
                      <button mat-button (click)="retry()">Retry</button>
                      <button mat-raised-button color="primary" (click)="loadNext()">Next Puzzle</button>
                    </div>
                  </div>
                }
              }
            </mat-card-content>
          </mat-card>

          @if (puzzle) {
            <mat-card class="info-card">
              <mat-card-content>
                <div class="puzzle-info">
                  <span class="rating-badge">Rating: {{ puzzle.rating }}</span>
                  <button mat-icon-button class="share-btn" (click)="sharePuzzle()" title="Puzzle teilen">
                    <mat-icon>share</mat-icon>
                  </button>
                  @if (puzzle.themes) {
                    <div class="themes">
                      @for (theme of puzzle.themes.split(' '); track theme) {
                        <span class="theme-chip">{{ theme }}</span>
                      }
                    </div>
                  }
                </div>
              </mat-card-content>
            </mat-card>
          }

          @if (stats) {
            <mat-card class="stats-card">
              <mat-card-header>
                <mat-card-title>Your Stats</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                <div class="stats-grid">
                  <div class="stat">
                    <span class="stat-value">{{ stats.puzzleElo }}</span>
                    <span class="stat-label">Elo</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.solved }}/{{ stats.totalAttempts }}</span>
                    <span class="stat-label">Solved</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.accuracy }}%</span>
                    <span class="stat-label">Accuracy</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.currentStreak }}</span>
                    <span class="stat-label">Streak</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.bestStreak }}</span>
                    <span class="stat-label">Best</span>
                  </div>
                </div>
              </mat-card-content>
            </mat-card>
          }

          @if (showSettings) {
          <mat-card class="filter-card" #settingsPanel>
            <mat-card-header>
              <mat-card-title>Filters</mat-card-title>
            </mat-card-header>
            <mat-card-content>
              <div class="viz-slider">
                <label>Visualisierung: Level {{ visualizationMode }}</label>
                <input type="range" min="0" max="4" step="1"
                       [value]="visualizationMode"
                       (input)="setVisualizationLevel(+$any($event.target).value)">
                <div class="viz-level-desc">{{ vizLevelDescription }}</div>
              </div>
              <div class="filter-row">
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>Schwierigkeit</mat-label>
                  <mat-select [(ngModel)]="difficulty" (ngModelChange)="onDifficultyChange()">
                    <mat-option value="sehr_leicht">Sehr leicht (Elo −600)</mat-option>
                    <mat-option value="leicht">Leicht (Elo −300)</mat-option>
                    <mat-option value="normal">Normal (Elo ±100)</mat-option>
                    <mat-option value="schwer">Schwer (Elo +300)</mat-option>
                    <mat-option value="sehr_schwer">Sehr schwer (Elo +600)</mat-option>
                  </mat-select>
                  <mat-hint>Puzzles rund um deine Elo ({{ stats?.puzzleElo ?? 1500 }})</mat-hint>
                </mat-form-field>
              </div>
              <div class="filter-row">
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>Stockfish Depth</mat-label>
                  <input matInput type="number" [(ngModel)]="stockfishDepth" (ngModelChange)="saveConfig()" min="1" max="24" step="1">
                  <mat-hint>1 (schwach) – 24 (stark)</mat-hint>
                </mat-form-field>
              </div>
              <div class="filter-actions">
                @if (isLoggedIn) {
                  <mat-slide-toggle [(ngModel)]="excludeSolved">Skip solved</mat-slide-toggle>
                }
                <button mat-raised-button color="primary" (click)="loadNext()">Load Puzzle</button>
              </div>
            </mat-card-content>
          </mat-card>

          <mat-card class="theme-card">
            <mat-card-content>
              <div class="theme-label">Modus</div>
              <div class="theme-chips">
                <div class="theme-chip" [class.active]="themeMode === 'fixed'" (click)="setThemeMode('fixed')">
                  <mat-icon>palette</mat-icon><span class="theme-name">Normal</span>
                </div>
                <div class="theme-chip" [class.active]="themeMode === 'random'" (click)="setThemeMode('random')">
                  <mat-icon>shuffle</mat-icon><span class="theme-name">Random</span>
                </div>
                <div class="theme-chip" [class.active]="themeMode === 'crazy'" (click)="setThemeMode('crazy')">
                  <mat-icon>auto_awesome</mat-icon><span class="theme-name">Crazy</span>
                </div>
              </div>
              @if (themeMode === 'fixed') {
              <div class="theme-label" style="margin-top: 0.75rem;">Board Theme</div>
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
              <div class="theme-label" style="margin-top: 0.75rem;">Figuren</div>
              <div class="theme-chips">
                @for (p of pieceSets; track p.key) {
                  <div class="theme-chip" [class.active]="pieceSet === p.key" (click)="setPieceSet(p.key)">
                    <div class="piece-preview" [style.backgroundImage]="'url(' + p.preview + ')'"></div>
                    <span class="theme-name">{{ p.name }}</span>
                  </div>
                }
              </div>
              }
            </mat-card-content>
          </mat-card>
          }

          @if (lastSolvedPuzzleId) {
            <button mat-stroked-button class="review-btn" (click)="reviewLastPuzzle()">
              <mat-icon>history</mat-icon>
              Review last puzzle
            </button>
          }

          <button mat-stroked-button color="accent" class="endless-btn" (click)="goEndless()">
            <mat-icon>all_inclusive</mat-icon>
            Endless Mode
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .puzzle-page { padding: 1rem; max-width: 1200px; margin: 0 auto; }
    .puzzle-layout { display: flex; gap: 1.5rem; align-items: flex-start; }
    .board-section { flex: 0 0 auto; width: min(60vw, 560px); min-width: 280px; }
    .info-section { flex: 1; min-width: 250px; display: flex; flex-direction: column; gap: 1rem; }
    .status-card { min-height: 120px; }
    .status-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 1rem 0; }
    .status-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .timer { font-size: 1.5em; font-weight: bold; font-variant-numeric: tabular-nums; margin: 0; }
    .result-icon { font-size: 48px; width: 48px; height: 48px; }
    .solved .result-icon { color: #4caf50; }
    .failed .result-icon { color: #f44336; }
    .fail-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; }
    .play-actions { display: flex; gap: 0.25rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
    .eval-compare { display: flex; align-items: center; gap: 0.5rem; font-size: 0.95em; }
    .eval-item { display: flex; align-items: center; gap: 0.25rem; }
    .eval-label { font-size: 0.8em; color: rgba(0,0,0,0.5); }
    .eval-value { font-weight: bold; font-variant-numeric: tabular-nums; }
    .eval-arrow { color: rgba(0,0,0,0.4); }
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .elo-change { font-size: 1.2em; font-weight: bold; }
    .elo-up { color: #4caf50; }
    .elo-down { color: #f44336; }
    .puzzle-info { display: flex; flex-direction: column; gap: 0.5rem; position: relative; }
    .rating-badge { font-weight: bold; font-size: 1.1em; }
    .share-btn { position: absolute; top: 0; right: 0; z-index: 2; }
    .themes { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .theme-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
    .stats-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 0.5rem; text-align: center; }
    .stat-value { font-size: 1.3em; font-weight: bold; display: block; }
    .stat-label { font-size: 0.8em; color: rgba(0,0,0,0.6); }
    .filter-row { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
    .filter-field { flex: 1; }
    .viz-slider { margin-bottom: 0.75rem; }
    .viz-slider label { font-size: 0.9em; font-weight: 500; }
    .viz-slider input[type=range] { width: 100%; margin: 0.25rem 0; }
    .viz-level-desc { font-size: 0.8em; color: rgba(0,0,0,0.55); }
    .viz-card {}
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
    .filter-actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .solved-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; }
    .review-nav { display: flex; align-items: center; gap: 0.5rem; }
    .review-counter { font-variant-numeric: tabular-nums; min-width: 56px; text-align: center; }
    .review-btn { width: 100%; height: 40px; font-size: 0.9em; }
    .review-btn mat-icon { margin-right: 0.25rem; font-size: 18px; width: 18px; height: 18px; }
    .endless-btn { width: 100%; height: 44px; font-size: 1em; }
    .endless-btn mat-icon { margin-right: 0.25rem; }
    .theme-label { font-size: 0.85em; color: rgba(0,0,0,0.6); margin-bottom: 0.5rem; }
    .theme-chips { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .piece-preview { width: 28px; height: 28px; background-size: contain; background-repeat: no-repeat; background-position: center; }
    .theme-img { width: 32px; height: 16px; border-radius: 3px; background-size: cover; background-position: center; }
    .status-card { position: relative; }
    .settings-gear { position: absolute; top: 4px; right: 4px; z-index: 2; }
    .settings-gear.active { color: #1976d2; }
    .theme-chip {
      display: flex; flex-direction: column; align-items: center; gap: 4px;
      cursor: pointer; padding: 6px; border-radius: 8px; border: 2px solid transparent;
      transition: border-color 0.15s;
    }
    .theme-chip.active { border-color: #1976d2; }
    .theme-chip:hover { background: rgba(0,0,0,0.04); }
    .theme-preview { display: flex; width: 32px; height: 16px; border-radius: 3px; overflow: hidden; }
    .tp-light, .tp-dark { flex: 1; }
    .theme-name { font-size: 0.75em; color: rgba(0,0,0,0.7); }

    @media (max-width: 768px) {
      .puzzle-layout { flex-direction: column; }
      .board-section { width: 100%; }
      .stats-grid { grid-template-columns: repeat(2, 1fr); }
    }
  `]
})
export class PuzzleComponent extends BasePuzzleSolver implements OnInit, OnDestroy {
  // state, boardFen, orientation, turnColor, dests, lastMove, isCheck, onSolutionPath,
  // alternativeSolve, mouseslipUsed, currentEval, visualizationMode, vizMoves, chess,
  // solutionMoves, moveIndex, autoAdvanceTimer, aborted, moveLog, moveStartTime → BasePuzzleSolver
  puzzle: PuzzleDto | null = null;
  stats: PuzzleStatsDto | null = null;
  private ratingRangeBounds: PuzzleRatingRange | null = null;

  boardTheme = 'brown';

  difficulty: 'sehr_leicht' | 'leicht' | 'normal' | 'schwer' | 'sehr_schwer' = 'normal';
  excludeSolved = false;
  stockfishDepth = 16;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  private attemptRecorded = false;
  private nextPuzzle: PuzzleDto | null = null;
  lastEloChange: number | null = null;

  // Review mode (Lösungs-Step-Through)
  reviewMode = false;
  reviewIndex = 0;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';
  private initialFen = '';

  private routePuzzleId: number | null = null;
  lastSolvedPuzzleId: number | null = null;
  solvedCountdown = 0;
  private countdownInterval?: ReturnType<typeof setInterval>;

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private authService: AuthService,
    private prefs: PreferencesService,
    private router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog
  ) {
    super(stockfish);
    this.loadConfig();
    this.stockfish.init().catch(() => {});
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.stockfishDepth; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.initialFen = this.chess.fen();
    this.startTimer();
    this.moveStartTime = Date.now();
  }

  protected override handleSolved(): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(true);
    this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
    this.enterSolutionReview();
    this.startSolvedCountdown();
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(false);
    this.enterSolutionReview();
  }

  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  showSettings = false;
  themeMode: ThemeMode = 'fixed';
  @ViewChild('settingsPanel', { read: ElementRef }) settingsPanel?: ElementRef<HTMLElement>;
  readonly pieceSets = PIECE_SETS;

  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }

  goEndless(): void {
    this.router.navigate(['/puzzles/endless']);
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/${this.puzzle.id}`;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url }, width: '400px' });
  }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.routePuzzleId = Number(idParam);
    }

    const stats$ = this.isLoggedIn
      ? this.puzzleService.getStats(this.visualizationMode)
      : this.puzzleService.getAnonymousStats();

    if (this.routePuzzleId) {
      // Deep-Link auf ein konkretes Puzzle → sofort laden; Stats/Range nebenher.
      this.loadNext();
      stats$.subscribe({ next: s => this.stats = s, error: () => {} });
      this.puzzleService.getRatingRange().subscribe({ next: r => this.ratingRangeBounds = r, error: () => {} });
      return;
    }

    // Sonst erst Elo (stats) + DB-Rating-Bereich laden, DANN das erste Zufallspuzzle –
    // sonst würde es mit Default-Elo 1500 / ungeklemmtem Fenster gezogen.
    const loadFirst = () => {
      this.puzzleService.getRatingRange().subscribe({
        next: r => this.ratingRangeBounds = r,
        error: () => this.loadNext(),
        complete: () => this.loadNext(),
      });
    };
    stats$.subscribe({
      next: s => this.stats = s,
      error: () => loadFirst(),
      complete: () => loadFirst(),
    });
  }

  ngOnDestroy(): void {
    this.stopTimer();
    this.stopCountdown();
    this.abortSolver();
    this.stockfish.destroy();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  loadNext(): void {
    this.state = 'LOADING';
    this.attemptRecorded = false;
    this.stopTimer();
    this.stopCountdown();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.lastEloChange = null;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';

    let source$;
    if (this.routePuzzleId) {
      const id = this.routePuzzleId;
      this.routePuzzleId = null;
      source$ = this.puzzleService.getById(id);
    } else if (this.nextPuzzle) {
      source$ = of(this.nextPuzzle);
      this.nextPuzzle = null;
    } else {
      const r = this.ratingRange();
      source$ = this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved);
    }

    source$.subscribe({
        next: puzzle => {
          this.puzzle = puzzle;
          this.setupPuzzle(puzzle);
          this.prefetchNext();
        },
        error: () => {
          this.state = 'ERROR';
          this.puzzle = null;
        }
      });
  }

  private prefetchNext(): void {
    const r = this.ratingRange();
    this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved)
      .subscribe({ next: p => this.nextPuzzle = p, error: () => {} });
  }

  /** Rating-Fenster aus aktueller Elo + Schwierigkeits-Offset (±RATING_WINDOW). */
  private ratingRange(): { min: number; max: number } {
    const elo = this.stats?.puzzleElo ?? 1500;
    let center = elo + (DIFFICULTY_OFFSET[this.difficulty] ?? 0);
    const b = this.ratingRangeBounds;
    if (b && b.max > b.min) {
      // Zentrum so verschieben, dass das ±Fenster im echten DB-Rating-Bereich bleibt
      // (sonst leeres Ergebnis → 404 → ERROR/Retry-Schleife bei extremen Offsets).
      center = Math.min(Math.max(center, b.min + RATING_WINDOW), b.max - RATING_WINDOW);
    }
    return { min: Math.max(0, center - RATING_WINDOW), max: center + RATING_WINDOW };
  }

  onDifficultyChange(): void {
    this.nextPuzzle = null;  // vorab geladenes Puzzle hatte die alte Schwierigkeit
    this.saveConfig();
  }

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewMode = false;
    this.reviewIndex = 0;
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) kommt aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  giveUp(): void {
    this.abortSolver();
    this.handleFailed();
  }

  retry(): void {
    if (!this.puzzle) return;
    this.attemptRecorded = false;
    this.setupPuzzle(this.puzzle);
  }

  private enterSolutionReview(): void {
    this.reviewMode = true;
    this.reviewIndex = this.reviewTotal;
  }

  get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  reviewNext(): void { this.stopCountdown(); this.reviewGoTo(this.reviewIndex + 1); }
  reviewPrev(): void { this.stopCountdown(); this.reviewGoTo(this.reviewIndex - 1); }

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
    if (this.state !== 'SOLVED' && this.state !== 'FAILED') return;
    if (e.key === 'ArrowLeft') this.reviewPrev();
    if (e.key === 'ArrowRight') this.reviewNext();
  }

  private startSolvedCountdown(): void {
    this.solvedCountdown = 3;
    this.countdownInterval = setInterval(() => {
      this.solvedCountdown--;
      if (this.solvedCountdown <= 0) {
        this.stopCountdown();
        this.loadNext();
      }
    }, 1000);
  }

  private stopCountdown(): void {
    if (this.countdownInterval) {
      clearInterval(this.countdownInterval);
      this.countdownInterval = undefined;
    }
    this.solvedCountdown = 0;
  }

  reviewLastPuzzle(): void {
    if (this.lastSolvedPuzzleId) {
      this.router.navigate(['/puzzles', this.lastSolvedPuzzleId]);
    }
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  private startTimer(): void {
    this.startTime = Date.now();
    this.elapsedSeconds = 0;
    this.timerInterval = setInterval(() => {
      this.elapsedSeconds = Math.floor((Date.now() - this.startTime) / 1000);
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = undefined;
    }
  }

  private recordAttempt(solved: boolean): void {
    if (!this.puzzle || this.attemptRecorded) return;
    this.attemptRecorded = true;
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    if (this.isLoggedIn) {
      this.puzzleService.recordAttempt(this.puzzle.id, solved, this.elapsedSeconds, log, this.visualizationMode).subscribe(res => {
        if (res.eloChange != null) this.lastEloChange = res.eloChange;
        this.puzzleService.getStats(this.visualizationMode).subscribe(s => this.stats = s);
      });
    } else {
      this.puzzleService.recordAnonymousAttempt(this.puzzle.id, solved, this.elapsedSeconds, log, this.visualizationMode).subscribe(() => {
        this.puzzleService.getAnonymousStats().subscribe(s => this.stats = s);
      });
    }
  }

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
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.stockfishDepth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.stockfishDepth);
    } catch {}
    this.evalLoading = false;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  // --- Config persistence ---

  private loadConfig(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.stockfishDepth = this.prefs.stockfishDepth;
    this.visualizationMode = this.prefs.visualization;
    const d = this.prefs.puzzleDifficulty;
    if (d && d in DIFFICULTY_OFFSET) this.difficulty = d as typeof this.difficulty;
  }

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.isLoggedIn) {
      this.puzzleService.getStats(level).subscribe(s => this.stats = s);
    }
    if (this.puzzle) this.setupPuzzle(this.puzzle);  // Modus-Wechsel = Puzzle neu starten
  }

  saveConfig(): void {
    this.prefs.setStockfishDepth(this.stockfishDepth);
    this.prefs.setPuzzleDifficulty(this.difficulty);
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

  toggleSettings(): void {
    this.showSettings = !this.showSettings;
    if (this.showSettings) {
      setTimeout(() => this.settingsPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
    }
  }
}
