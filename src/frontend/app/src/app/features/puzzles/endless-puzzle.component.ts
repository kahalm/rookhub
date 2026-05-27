import { Component, OnDestroy } from '@angular/core';
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
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleService, PuzzleDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';

// AWAITING_USER_MOVE = first move only (no buttons)
// THINKING = opponent responding (buttons visible, board locked)
// PLAYING = user's turn after first move (buttons visible, board active)
type EndlessState = 'CONFIG' | 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE'
  | 'THINKING' | 'PLAYING' | 'CORRECT' | 'WRONG' | 'GAME_OVER' | 'EXHAUSTED';

interface EndlessConfig {
  startElo: number;
  step: number;
  themes: string;
  fasttrack: boolean;
  fasttrackThreshold1?: number;
  fasttrackThreshold2?: number;
}

interface EndlessSession {
  timestamp: number;
  config: EndlessConfig;
  totalSolved: number;
  maxRating: number;
  durationSeconds: number;
  mistakeAtRatings: number[];
}

const CONFIG_KEY = 'rookhub_endless_config';
const HIGHSCORE_KEY = 'rookhub_endless_highscore';
const HISTORY_KEY = 'rookhub_endless_history';
const MAX_HISTORY_SESSIONS = 50;
const FASTTRACK_SESSION_COUNT = 10;

@Component({
  selector: 'app-endless-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, MatSlideToggleModule,
    PuzzleBoardComponent
  ],
  template: `
    <div class="endless-page">
      @switch (screen) {
        @case ('config') {
          <div class="config-screen">
            <mat-card class="config-card">
              <mat-card-header>
                <mat-card-title>Endless Puzzle Mode</mat-card-title>
                <mat-card-subtitle>Progressive difficulty — how far can you go?</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="config-fields">
                  <mat-form-field appearance="outline">
                    <mat-label>Start Rating</mat-label>
                    <input matInput type="number" [(ngModel)]="config.startElo" [min]="puzzleRange.min" [max]="puzzleRange.max" step="50">
                    <mat-hint>{{ puzzleRange.min }}–{{ puzzleRange.max }}</mat-hint>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Step Size</mat-label>
                    <input matInput type="number" [(ngModel)]="config.step" min="10" max="200" step="5">
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Themes (optional)</mat-label>
                    <input matInput [(ngModel)]="config.themes" placeholder="e.g. fork pin">
                  </mat-form-field>
                </div>

                <div class="fasttrack-section">
                  <mat-slide-toggle [(ngModel)]="config.fasttrack" (change)="onFasttrackToggle()">
                    Fasttrack
                  </mat-slide-toggle>
                </div>

                <div class="level-preview">
                  @if (config.fasttrack && fasttrackPhase1Step > 0) {
                    <p class="threshold-explain">Ratings where you typically lose lives. Adjust to skip easier puzzles faster or slower.</p>
                    <div class="threshold-fields">
                      <div class="threshold-field-wrap">
                        <mat-form-field appearance="outline">
                          <mat-label>1st Mistake Rating</mat-label>
                          <input matInput type="number" [(ngModel)]="fasttrackAvgFirst" (ngModelChange)="onThresholdChange()" [min]="puzzleRange.min" [max]="puzzleRange.max" step="50">
                        </mat-form-field>
                        @if (fasttrackAvgFirst !== fasttrackAutoFirst) {
                          <span class="auto-hint" (click)="resetThreshold(1)">Auto: {{ fasttrackAutoFirst }}</span>
                        }
                      </div>
                      <div class="threshold-field-wrap">
                        <mat-form-field appearance="outline">
                          <mat-label>2nd Mistake Rating</mat-label>
                          <input matInput type="number" [(ngModel)]="fasttrackAvgSecond" (ngModelChange)="onThresholdChange()" [min]="puzzleRange.min" [max]="puzzleRange.max" step="50">
                        </mat-form-field>
                        @if (fasttrackAvgSecond !== fasttrackAutoSecond) {
                          <span class="auto-hint" (click)="resetThreshold(2)">Auto: {{ fasttrackAutoSecond }}</span>
                        }
                      </div>
                    </div>
                    <div class="fasttrack-preview">
                      <div class="fasttrack-phase">
                        <span class="phase-label">Phase 1 (Lv 1–5)</span>
                        <span class="phase-detail">Step {{ fasttrackPhase1Step }} | {{ config.startElo }} → {{ fasttrackAvgFirst }}</span>
                      </div>
                      <div class="fasttrack-phase">
                        <span class="phase-label">Phase 2 (Lv 6–10)</span>
                        <span class="phase-detail">Step {{ fasttrackPhase2Step }} | {{ fasttrackAvgFirst }} → {{ fasttrackAvgSecond }}</span>
                      </div>
                      <div class="fasttrack-phase">
                        <span class="phase-label">Phase 3 (Lv 11+)</span>
                        <span class="phase-detail">Step 20</span>
                      </div>
                    </div>
                  } @else {
                    <h4>Level Preview</h4>
                    <div class="preview-levels">
                      @for (lvl of previewLevels; track lvl.level) {
                        <span class="preview-chip">Lv {{ lvl.level }}: {{ lvl.min }}–{{ lvl.max }}</span>
                      }
                    </div>
                  }
                </div>

                <div class="lives-display config-lives">
                  @for (i of [1,2,3]; track i) {
                    <mat-icon class="heart full">favorite</mat-icon>
                  }
                </div>

                @if (highscore > 0) {
                  <div class="highscore-badge">
                    <mat-icon>emoji_events</mat-icon>
                    Highscore: {{ highscore }} Rating
                  </div>
                }

                @if (sessionHistory.length > 0) {
                  <p class="session-count">{{ sessionHistory.length }} sessions played</p>
                }

                <button mat-raised-button color="primary" class="start-btn" (click)="startGame()">
                  <mat-icon>play_arrow</mat-icon>
                  Start
                </button>
              </mat-card-content>
            </mat-card>
          </div>
        }

        @case ('play') {
          <div class="play-screen">
            <div class="board-section">
              <app-puzzle-board
                [fen]="boardFen"
                [orientation]="orientation"
                [turnColor]="turnColor"
                [dests]="dests"
                [lastMove]="lastMove"
                [viewOnly]="state !== 'AWAITING_USER_MOVE' && state !== 'PLAYING'"
                [check]="isCheck"
                (moveMade)="onMoveMade($event)"
              />
            </div>

            <div class="info-section">
              <mat-card class="status-card">
                <mat-card-content>
                  @switch (state) {
                    @case ('LOADING') {
                      <div class="status-center">
                        <mat-spinner diameter="40"></mat-spinner>
                        <p>Loading puzzle...</p>
                      </div>
                    }
                    @case ('SETUP') {
                      <div class="status-center">
                        <p class="status-text">Watch the opponent's move...</p>
                      </div>
                    }
                    @case ('AWAITING_USER_MOVE') {
                      <div class="status-center">
                        <p class="status-text">Your turn! Find the best move.</p>
                      </div>
                    }
                    @case ('THINKING') {
                      <div class="status-center">
                        <mat-spinner diameter="24"></mat-spinner>
                        <p class="status-text">Opponent is thinking...</p>
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
                        <p class="status-text">Your move...</p>
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
                          <button mat-button color="warn" (click)="giveUp()">
                            <mat-icon>flag</mat-icon>
                            Give Up
                          </button>
                        </div>
                      </div>
                    }
                    @case ('CORRECT') {
                      <div class="status-center solved">
                        <mat-icon class="result-icon">check_circle</mat-icon>
                        @if (alternativeSolve) {
                          <p class="status-text">Checkmate!</p>
                          <p class="alt-hint">Alternative solution — the puzzle had a different intended line.</p>
                          <div class="alt-actions">
                            <button mat-raised-button color="primary" (click)="continueAfterSolve()">
                              <mat-icon>arrow_forward</mat-icon> Continue
                            </button>
                            <button mat-button (click)="showIntendedSolution()">
                              <mat-icon>visibility</mat-icon> Show Solution
                            </button>
                          </div>
                        } @else {
                          <p class="status-text">Correct!</p>
                        }
                      </div>
                    }
                    @case ('WRONG') {
                      <div class="status-center failed">
                        <mat-icon class="result-icon">cancel</mat-icon>
                        <p class="status-text">Wrong!</p>
                      </div>
                    }
                  }
                </mat-card-content>
              </mat-card>

              <mat-card class="stats-card">
                <mat-card-content>
                  <div class="stats-grid">
                    <div class="stat">
                      <span class="stat-value">{{ currentRating }}</span>
                      <span class="stat-label">Rating</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ level }}</span>
                      <span class="stat-label">Level</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ solved }}</span>
                      <span class="stat-label">Solved</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ formatTime(sessionSeconds) }}</span>
                      <span class="stat-label">Time</span>
                    </div>
                  </div>
                  <div class="lives-display">
                    @for (i of [1,2,3]; track i) {
                      <mat-icon [class]="i <= lives ? 'heart full' : 'heart empty'">
                        {{ i <= lives ? 'favorite' : 'favorite_border' }}
                      </mat-icon>
                    }
                  </div>
                  @if (config.fasttrack) {
                    <div class="phase-indicator">{{ currentPhaseLabel }}</div>
                  }
                </mat-card-content>
              </mat-card>

              @if (puzzle) {
                <mat-card class="info-card">
                  <mat-card-content>
                    <div class="puzzle-info">
                      <span class="rating-badge">Puzzle Rating: {{ puzzle.rating }}</span>
                      <span class="level-badge">Level {{ level }} ({{ currentMinRating }}–{{ currentMaxRating }})</span>
                      @if (puzzle.themes) {
                        <span class="themes-toggle" (click)="showThemes = !showThemes">
                          {{ showThemes ? 'Hide tags' : 'Show tags' }}
                        </span>
                        @if (showThemes) {
                          <div class="themes">
                            @for (theme of puzzle.themes.split(' '); track theme) {
                              <span class="theme-chip">{{ theme }}</span>
                            }
                          </div>
                        }
                      }
                    </div>
                  </mat-card-content>
                </mat-card>
              }
            </div>
          </div>
        }

        @case ('exhausted') {
          <div class="gameover-screen">
            <mat-card class="gameover-card exhausted-card">
              <mat-card-header>
                <mat-card-title>Ausgespielt!</mat-card-title>
                <mat-card-subtitle>Du hast alle Puzzle-Rating-Bereiche durchgespielt.</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="stockfish-question">
                  <span class="stockfish-fish">&#x1F41F;</span>
                  <p class="stockfish-text">Bist du ein Stockfisch?</p>
                </div>
                <div class="gameover-stats">
                  <div class="gameover-stat">
                    <mat-icon>trending_up</mat-icon>
                    <span class="go-value">{{ maxRatingReached }}</span>
                    <span class="go-label">Max Rating</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>extension</mat-icon>
                    <span class="go-value">{{ solved }}</span>
                    <span class="go-label">Puzzles Solved</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>favorite</mat-icon>
                    <span class="go-value">{{ lives }}</span>
                    <span class="go-label">Lives Left</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>timer</mat-icon>
                    <span class="go-value">{{ formatTime(sessionSeconds) }}</span>
                    <span class="go-label">Time</span>
                  </div>
                </div>
                @if (isNewHighscore) {
                  <div class="new-highscore">
                    <mat-icon>emoji_events</mat-icon>
                    New Highscore!
                  </div>
                }
                <div class="gameover-actions">
                  <button mat-raised-button color="primary" (click)="playAgain()">
                    <mat-icon>replay</mat-icon>
                    Play Again
                  </button>
                  <button mat-button (click)="backToPuzzles()">
                    <mat-icon>arrow_back</mat-icon>
                    Back to Puzzles
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
                <mat-card-title>Game Over</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                <div class="gameover-stats">
                  <div class="gameover-stat">
                    <mat-icon>trending_up</mat-icon>
                    <span class="go-value">{{ maxRatingReached }}</span>
                    <span class="go-label">Max Rating</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>extension</mat-icon>
                    <span class="go-value">{{ solved }}</span>
                    <span class="go-label">Puzzles Solved</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>stacked_line_chart</mat-icon>
                    <span class="go-value">{{ level }}</span>
                    <span class="go-label">Level Reached</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>timer</mat-icon>
                    <span class="go-value">{{ formatTime(sessionSeconds) }}</span>
                    <span class="go-label">Time</span>
                  </div>
                </div>
                @if (currentSessionMistakes.length > 0) {
                  <div class="mistake-ratings">
                    <mat-icon>heart_broken</mat-icon>
                    <span>Lives lost at: {{ currentSessionMistakes.join(', ') }}</span>
                  </div>
                }
                @if (isNewHighscore) {
                  <div class="new-highscore">
                    <mat-icon>emoji_events</mat-icon>
                    New Highscore!
                  </div>
                }
                <div class="gameover-actions">
                  <button mat-raised-button color="primary" (click)="playAgain()">
                    <mat-icon>replay</mat-icon>
                    Play Again
                  </button>
                  <button mat-button (click)="backToPuzzles()">
                    <mat-icon>arrow_back</mat-icon>
                    Back to Puzzles
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

    .config-screen { display: flex; justify-content: center; padding-top: 2rem; }
    .config-card { max-width: 500px; width: 100%; }
    .config-fields { display: grid; grid-template-columns: 1fr 1fr; gap: 0 1rem; margin-top: 1rem; }
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
    .config-lives { justify-content: center; margin: 1rem 0; }
    .highscore-badge {
      display: flex; align-items: center; gap: 0.5rem; justify-content: center;
      color: #ff9800; font-weight: 500; margin-bottom: 1rem;
    }
    .session-count { text-align: center; font-size: 0.85em; color: rgba(0,0,0,0.5); margin: 0 0 1rem; }
    .start-btn { width: 100%; height: 48px; font-size: 1.1em; }

    .play-screen { display: flex; gap: 1.5rem; align-items: flex-start; }
    .board-section { flex: 0 0 auto; width: min(60vw, 560px); min-width: 280px; }
    .info-section { flex: 1; min-width: 250px; display: flex; flex-direction: column; gap: 1rem; }
    .status-card { min-height: 80px; }
    .status-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 0.75rem 0; }
    .status-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .result-icon { font-size: 48px; width: 48px; height: 48px; }
    .solved .result-icon { color: #4caf50; }
    .failed .result-icon { color: #f44336; }
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .alt-actions { display: flex; gap: 0.5rem; margin-top: 0.5rem; }
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
    .puzzle-info { display: flex; flex-direction: column; gap: 0.5rem; }
    .rating-badge { font-weight: bold; font-size: 1.1em; }
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

    @media (max-width: 768px) {
      .play-screen { flex-direction: column; }
      .board-section { width: 100%; }
      .stats-grid { grid-template-columns: repeat(2, 1fr); }
      .config-fields { grid-template-columns: 1fr; }
    }
  `]
})
export class EndlessPuzzleComponent implements OnDestroy {
  get screen(): 'config' | 'play' | 'gameover' | 'exhausted' {
    if (this.state === 'EXHAUSTED') return 'exhausted';
    if (this.state === 'GAME_OVER') return 'gameover';
    if (this.state === 'CONFIG') return 'config';
    return 'play';
  }

  state: EndlessState = 'CONFIG';
  config: EndlessConfig = { startElo: 700, step: 40, themes: '', fasttrack: false };

  lives = 3;
  level = 0;
  solved = 0;
  maxRatingReached = 0;
  isNewHighscore = false;
  highscore = 0;
  alternativeSolve = false;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';
  currentEval = '';

  // Session timer
  sessionSeconds = 0;
  private sessionInterval?: ReturnType<typeof setInterval>;
  private sessionStart = 0;

  // Session history
  sessionHistory: EndlessSession[] = [];
  currentSessionMistakes: number[] = [];
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

  // Puzzle DB range
  puzzleRange: PuzzleRatingRange = { min: 100, max: 3000 };

  // Board
  puzzle: PuzzleDto | null = null;
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  lastMove?: [Key, Key];
  isCheck = false;

  // Puzzle logic
  private chess = new Chess();
  private solutionMoves: string[] = [];
  private moveIndex = 0;
  private onSolutionPath = true;
  private initialFen = '';
  private prefetchedPuzzle: PuzzleDto | null = null;
  private autoAdvanceTimer?: ReturnType<typeof setTimeout>;
  private aborted = false;

  constructor(
    private puzzleService: PuzzleService,
    private stockfish: StockfishService,
    private authService: AuthService,
    private router: Router
  ) {
    this.loadConfig();
    this.loadHighscore();
    this.loadSessionHistory();
    if (this.config.fasttrack) this.computeFasttrackSteps();
    this.puzzleService.getRatingRange().subscribe({
      next: r => { this.puzzleRange = r; this.clampConfig(); },
      error: () => {}
    });
    this.stockfish.init().catch(() => {});
  }

  ngOnDestroy(): void {
    this.stopSessionTimer();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.stockfish.destroy();
  }

  // --- Config ---

  get previewLevels(): { level: number; min: number; max: number }[] {
    const levels = [];
    for (let i = 0; i < 3; i++) {
      const min = this.config.startElo + i * this.config.step;
      levels.push({ level: i, min, max: min + this.config.step });
    }
    return levels;
  }

  get currentMinRating(): number { return this._currentMinRating; }
  get currentMaxRating(): number { return this._currentMinRating + this.config.step; }
  get currentRating(): number { return this._currentMinRating; }

  get currentPhaseLabel(): string {
    if (!this.config.fasttrack) return '';
    if (this.solved < 5) return `Phase 1 (step ${this.fasttrackPhase1Step})`;
    if (this.solved < 10) return `Phase 2 (step ${this.fasttrackPhase2Step})`;
    return 'Phase 3 (step 20)';
  }

  private clampConfig(): void {
    this.config.startElo = Math.max(this.puzzleRange.min, Math.min(this.puzzleRange.max, this.config.startElo));
  }

  onFasttrackToggle(): void {
    if (this.config.fasttrack) {
      this.computeFasttrackSteps();
    }
  }

  // --- Game lifecycle ---

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
    if (this.config.fasttrack) this.computeFasttrackSteps();
    this.startSessionTimer();
    this.loadPuzzle();
  }

  playAgain(): void {
    this.state = 'CONFIG';
    if (this.config.fasttrack) this.computeFasttrackSteps();
  }

  backToPuzzles(): void { this.router.navigate(['/puzzles']); }

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
      this.state = 'EXHAUSTED';
      return;
    }

    const min = this._currentMinRating;
    const max = this._currentMinRating + this.config.step;

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
    const max = min + this.config.step;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandom(min, max, themes).subscribe({
      next: p => this.prefetchedPuzzle = p,
      error: () => this.prefetchedPuzzle = null
    });
  }

  // --- Puzzle setup ---

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.solutionMoves = puzzle.moves.split(' ');
    this.moveIndex = 0;
    this.chess = new Chess(puzzle.fen);
    this.onSolutionPath = true;
    this.aborted = false;

    const setupMove = this.solutionMoves[0];
    const setupFrom = setupMove.substring(0, 2) as Square;
    const piece = this.chess.get(setupFrom);
    this.orientation = piece?.color === 'w' ? 'black' : 'white';

    this.updateBoard();
    this.state = 'SETUP';

    setTimeout(() => {
      if (this.state !== 'SETUP') return;
      this.playMove(this.solutionMoves[0]);
      this.moveIndex = 1;
      this.state = 'AWAITING_USER_MOVE';
      this.initialFen = this.chess.fen();
      this.updateBoard();
    }, 600);
  }

  // --- Move handling (unified for all states after first move) ---

  onMoveMade(event: { orig: Key; dest: Key }): void {
    if (this.state === 'PLAYING') {
      this.handleMove(event);
      return;
    }
    if (this.state !== 'AWAITING_USER_MOVE') return;
    // First move — transition to unified flow
    this.handleMove(event);
  }

  private handleMove(event: { orig: Key; dest: Key }): void {
    if (this.onSolutionPath) {
      const expectedUci = this.solutionMoves[this.moveIndex];
      const userUci = event.orig + event.dest;

      if (userUci === expectedUci.substring(0, 4)) {
        // Correct — follow solution
        this.playMove(expectedUci);
        this.moveIndex++;
        this.advanceAfterCorrectMove();
      } else {
        // Wrong — leave solution path
        this.playFreeMove(event.orig, event.dest);
        this.onSolutionPath = false;
        if (this.chess.isGameOver()) { this.handleGameOver(); return; }
        this.opponentRespond();
      }
    } else {
      // Off-path: accept any legal move
      this.playFreeMove(event.orig, event.dest);
      if (this.chess.isGameOver()) { this.handleGameOver(); return; }
      this.opponentRespond();
    }
  }

  private advanceAfterCorrectMove(): void {
    if (this.moveIndex >= this.solutionMoves.length) {
      this.puzzleSolved(false);
      return;
    }
    // Play opponent's solution response
    this.state = 'THINKING';
    this.updateBoard();
    this.autoAdvanceTimer = setTimeout(() => {
      if (this.aborted) return;
      this.playMove(this.solutionMoves[this.moveIndex]);
      this.moveIndex++;
      this.updateBoard();
      if (this.moveIndex >= this.solutionMoves.length) {
        this.puzzleSolved(false);
        return;
      }
      this.state = 'PLAYING';
      this.updateBoard();
    }, 400);
  }

  private async opponentRespond(): Promise<void> {
    this.state = 'THINKING';
    this.updateBoard();

    try {
      const result = await this.stockfish.getBestMove(this.chess.fen(), 12);
      if (this.aborted) return;
      this.currentEval = result.eval;
      this.playMove(result.move);
      this.updateBoard();

      if (this.chess.isGameOver()) {
        this.handleGameOver();
        return;
      }

      this.autoAdvanceTimer = setTimeout(() => {
        if (this.aborted) return;
        this.state = 'PLAYING';
        this.updateBoard();
      }, 400);
    } catch {
      if (!this.aborted) this.loseLife();
    }
  }

  private handleGameOver(): void {
    if (this.chess.isCheckmate()) {
      const loserColor = this.chess.turn();
      const userColor = this.orientation === 'white' ? 'w' : 'b';
      if (loserColor !== userColor) {
        this.puzzleSolved(true);
        return;
      }
    }
    this.loseLife();
  }

  private puzzleSolved(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.state = 'CORRECT';
    this.solved++;
    this.recordAttempt(true);
    this.updateBoard();

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
    this._currentMinRating += this.getCurrentStep();
    this.level++;
    this.loadPuzzle();
  }

  showIntendedSolution(): void {
    if (!this.puzzle) return;
    // Reset board to puzzle start and play through intended solution
    this.solutionMoves = this.puzzle.moves.split(' ');
    this.chess = new Chess(this.puzzle.fen);
    this.state = 'CORRECT';

    // Play setup move immediately
    this.playMove(this.solutionMoves[0]);
    this.updateBoard();

    // Animate remaining moves
    let i = 1;
    const playNext = () => {
      if (i >= this.solutionMoves.length) return;
      this.playMove(this.solutionMoves[i]);
      i++;
      this.updateBoard();
      this.autoAdvanceTimer = setTimeout(playNext, 600);
    };
    this.autoAdvanceTimer = setTimeout(playNext, 400);
  }

  private loseLife(): void {
    this.currentSessionMistakes.push(this._currentMinRating);
    this.lives--;
    this.recordAttempt(false);
    this.state = 'WRONG';
    this.updateBoard();

    if (this.lives <= 0) {
      this.autoAdvanceTimer = setTimeout(() => this.endGame(), 1200);
    } else {
      this.prefetchedPuzzle = null;
      this.autoAdvanceTimer = setTimeout(() => this.loadPuzzle(), 1200);
    }
  }

  // --- Buttons ---

  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval && this.state === 'PLAYING') {
      this.refreshEval();
    }
  }

  private async refreshEval(): Promise<void> {
    this.evalLoading = true;
    try {
      if (!this.initialEval && this.initialFen) {
        this.initialEval = await this.stockfish.getEval(this.initialFen, 12);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), 12);
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

  giveUp(): void {
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.loseLife();
  }

  private endGame(): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.recordSession();
    this.state = 'GAME_OVER';
  }

  // --- Step calculation ---

  private getCurrentStep(): number {
    if (!this.config.fasttrack) return this.config.step;
    return this.getStepForSolved(this.solved);
  }

  private getStepForSolved(solvedCount: number): number {
    if (!this.config.fasttrack) return this.config.step;
    if (solvedCount <= 5) return this.fasttrackPhase1Step;
    if (solvedCount <= 10) return this.fasttrackPhase2Step;
    return 20;
  }

  private computeFasttrackSteps(): void {
    const withMistakes = this.sessionHistory
      .filter(s => s.mistakeAtRatings.length > 0)
      .slice(-FASTTRACK_SESSION_COUNT);

    // Auto-calculate from history, ensure minimum startElo+400/+800
    const defaultFirst = this.config.startElo + 400;
    const defaultSecond = this.config.startElo + 800;
    if (withMistakes.length > 0) {
      const avgFirst = Math.round(
        withMistakes.reduce((sum, s) => sum + s.mistakeAtRatings[0], 0) / withMistakes.length
      );
      const withSecond = withMistakes.filter(s => s.mistakeAtRatings.length >= 2);
      const avgSecond = withSecond.length > 0
        ? Math.round(withSecond.reduce((sum, s) => sum + s.mistakeAtRatings[1], 0) / withSecond.length)
        : avgFirst + 100;
      this.fasttrackAutoFirst = Math.max(defaultFirst, avgFirst);
      this.fasttrackAutoSecond = Math.max(defaultSecond, avgSecond);
    } else {
      this.fasttrackAutoFirst = defaultFirst;
      this.fasttrackAutoSecond = defaultSecond;
    }

    // Apply manual overrides from config, or use auto values
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
    this.fasttrackPhase1Step = Math.max(10, Math.round((this.fasttrackAvgFirst - this.config.startElo) / 5));
    this.fasttrackPhase2Step = Math.max(10, Math.round((this.fasttrackAvgSecond - this.fasttrackAvgFirst) / 5));
  }

  // --- Board helpers ---

  private playMove(uci: string): void {
    const from = uci.substring(0, 2) as Square;
    const to = uci.substring(2, 4) as Square;
    const promotion = uci.length > 4 ? uci[4] as 'q' | 'r' | 'b' | 'n' : undefined;
    this.chess.move({ from, to, promotion });
    this.lastMove = [from as Key, to as Key];
  }

  private playFreeMove(orig: Key, dest: Key): void {
    const from = orig as string as Square;
    const to = dest as string as Square;
    const moves = this.chess.moves({ verbose: true });
    const match = moves.find(m => m.from === from && m.to === to);
    if (match) {
      this.chess.move(match);
    } else {
      try { this.chess.move({ from, to, promotion: 'q' }); } catch { return; }
    }
    this.lastMove = [orig, dest];
  }

  private updateBoard(): void {
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = this.state === 'AWAITING_USER_MOVE' || this.state === 'PLAYING';
    this.dests = interactive ? this.calcDests() : new Map();
  }

  private calcDests(): Map<Key, Key[]> {
    const dests = new Map<Key, Key[]>();
    for (const m of this.chess.moves({ verbose: true })) {
      const from = m.from as Key;
      if (!dests.has(from)) dests.set(from, []);
      dests.get(from)!.push(m.to as Key);
    }
    return dests;
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
    if (!this.puzzle || !this.authService.isLoggedIn) return;
    this.puzzleService.recordAttempt(this.puzzle.id, solved, 0).subscribe();
  }

  // --- localStorage ---

  private loadConfig(): void {
    try {
      const raw = localStorage.getItem(CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        // Remove deprecated rangeWidth from old configs
        delete saved.rangeWidth;
        this.config = { ...this.config, ...saved };
      }
    } catch {}
    // Clamp step size
    if (this.config.step < 10) this.config.step = 10;
    if (this.config.step > 200) this.config.step = 200;
    // Clear stale thresholds that are at or below startElo
    if (this.config.fasttrackThreshold1 != null && this.config.fasttrackThreshold1 <= this.config.startElo) {
      this.config.fasttrackThreshold1 = undefined;
    }
    if (this.config.fasttrackThreshold2 != null && this.config.fasttrackThreshold2 <= this.config.startElo) {
      this.config.fasttrackThreshold2 = undefined;
    }
  }

  private saveConfig(): void {
    try { localStorage.setItem(CONFIG_KEY, JSON.stringify(this.config)); } catch {}
  }

  private loadHighscore(): void {
    try {
      const raw = localStorage.getItem(HIGHSCORE_KEY);
      if (raw) this.highscore = parseInt(raw, 10) || 0;
    } catch {}
  }

  private checkHighscore(): void {
    if (this.maxRatingReached > this.highscore) {
      this.highscore = this.maxRatingReached;
      this.isNewHighscore = true;
      try { localStorage.setItem(HIGHSCORE_KEY, String(this.highscore)); } catch {}
    }
  }

  // --- Session History ---

  private loadSessionHistory(): void {
    try {
      const raw = localStorage.getItem(HISTORY_KEY);
      if (raw) this.sessionHistory = JSON.parse(raw) || [];
    } catch { this.sessionHistory = []; }
  }

  private saveSessionHistory(): void {
    try { localStorage.setItem(HISTORY_KEY, JSON.stringify(this.sessionHistory)); } catch {}
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
    this.sessionHistory.push(session);
    if (this.sessionHistory.length > MAX_HISTORY_SESSIONS) {
      this.sessionHistory = this.sessionHistory.slice(-MAX_HISTORY_SESSIONS);
    }
    this.saveSessionHistory();
  }
}
