import { Component, OnInit, OnDestroy, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { ActivatedRoute } from '@angular/router';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleService, BookPuzzleDto } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';

type BookPuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED';

const BOOK_PUZZLE_CONFIG_KEY = 'rookhub_book_puzzle_config';
const BOARD_THEME_KEY = 'rookhub_board_theme';
const PIECE_SET_KEY = 'rookhub_piece_set';

@Component({
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatChipsModule, MatInputModule, MatFormFieldModule,
    PuzzleBoardComponent
  ],
  template: `
    <div class="puzzle-page">
      <div class="puzzle-layout">
        <div class="board-section">
          <app-puzzle-board
            [fen]="boardFen"
            [orientation]="orientation"
            [turnColor]="turnColor"
            [dests]="dests"
            [lastMove]="lastMove"
            [viewOnly]="state !== 'AWAITING_USER_MOVE' && state !== 'PLAYING' && state !== 'THINKING'"
            [premovable]="state === 'THINKING'"
            [check]="isCheck"
            [boardTheme]="boardTheme"
            [pieceSet]="pieceSet"
            (moveMade)="onMoveMade($event)"
          />
        </div>

        <div class="info-section">
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
                @case ('SETUP') {
                  <div class="status-center">
                    <p class="status-text">Watch the opponent's move...</p>
                  </div>
                }
                @case ('AWAITING_USER_MOVE') {
                  <div class="status-center">
                    <p class="status-text">Your turn! Find the best move.</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                  </div>
                }
                @case ('THINKING') {
                  <div class="status-center">
                    <mat-spinner diameter="24"></mat-spinner>
                    <p class="status-text">Stockfish denkt...</p>
                    <div class="play-actions">
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
                    <p class="status-text">Dein Zug gegen Stockfish...</p>
                    <div class="play-actions">
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      @if (!mouseslipUsed && !onSolutionPath) {
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
                      <p class="alt-hint">Alternative Loesung — das Puzzle hatte eine andere beabsichtigte Zugfolge.</p>
                    } @else {
                      <p class="status-text">Correct!</p>
                    }
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                  </div>
                }
                @case ('FAILED') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">cancel</mat-icon>
                    <p class="status-text">Incorrect</p>
                    <div class="fail-actions">
                      <button mat-button (click)="retry()">Retry</button>
                      <button mat-button (click)="showSolution()">Show Solution</button>
                    </div>
                  </div>
                }
              }
            </mat-card-content>
          </mat-card>

          @if (puzzle) {
            <mat-card class="info-card">
              <mat-card-content>
                <div class="puzzle-meta">
                  @if (puzzle.title) {
                    <p class="meta-title">{{ puzzle.title }}</p>
                  }
                  @if (puzzle.chapter) {
                    <p class="meta-chapter">{{ puzzle.chapter }}</p>
                  }
                  @if (puzzle.comment) {
                    <p class="meta-comment">{{ puzzle.comment }}</p>
                  }
                  <div class="meta-row">
                    <span class="book-name">{{ displayBookName }}</span>
                    @if (puzzle.difficulty) {
                      <span class="difficulty-badge" [class]="'diff-' + puzzle.difficulty.toLowerCase()">{{ puzzle.difficulty }}</span>
                    }
                    @if (puzzle.bookRating) {
                      <span class="rating-badge">Schwierigkeit: {{ puzzle.bookRating }}/10</span>
                    }
                  </div>
                  @if (puzzle.tags) {
                    <div class="tags">
                      @for (tag of puzzle.tags.split(' '); track tag) {
                        <span class="tag-chip">{{ tag }}</span>
                      }
                    </div>
                  }
                </div>
              </mat-card-content>
            </mat-card>
          }

          <mat-card class="config-card" #settingsPanel>
            <mat-card-content>
              @if (showSettings) {
              <mat-form-field appearance="outline" class="depth-field">
                <mat-label>Stockfish Depth</mat-label>
                <input matInput type="number" [(ngModel)]="stockfishDepth" (ngModelChange)="saveConfig()" min="1" max="24" step="1">
                <mat-hint>1 (schwach) – 24 (stark)</mat-hint>
              </mat-form-field>
              <div class="theme-section">
                <div class="theme-label">Board Theme</div>
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
              </div>
              }
            </mat-card-content>
          </mat-card>
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
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .puzzle-meta { display: flex; flex-direction: column; gap: 0.5rem; }
    .meta-title { font-weight: bold; font-size: 1.1em; margin: 0; }
    .meta-chapter { color: rgba(0,0,0,0.7); margin: 0; }
    .meta-comment { font-style: italic; color: rgba(0,0,0,0.6); margin: 0; font-size: 0.9em; }
    .meta-row { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .book-name { font-size: 0.85em; color: rgba(0,0,0,0.5); }
    .difficulty-badge {
      font-size: 0.8em; padding: 2px 8px; border-radius: 12px; font-weight: 500;
    }
    .diff-anfänger, .diff-anfaenger { background: #c8e6c9; color: #2e7d32; }
    .diff-fortgeschritten { background: #fff3e0; color: #e65100; }
    .diff-meister { background: #ffcdd2; color: #c62828; }
    .rating-badge { font-weight: bold; font-size: 0.9em; }
    .tags { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .tag-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
    .depth-field { width: 100%; }
    .config-card mat-card-content { padding-bottom: 0; }
    .theme-section { margin-top: 0.75rem; }
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
    }
  `]
})
export class BookPuzzleComponent implements OnInit, OnDestroy {
  state: BookPuzzleState = 'LOADING';
  puzzle: BookPuzzleDto | null = null;

  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  lastMove?: [Key, Key];
  isCheck = false;

  stockfishDepth = 16;
  boardTheme = 'brown';
  readonly boardThemes: { key: string; name: string; light: string; dark: string; img?: string }[] = [
    { key: 'brown', name: 'Brown', light: '#f0d9b5', dark: '#b58863' },
    { key: 'blue', name: 'Blue', light: '#d4e3ed', dark: '#5882a1' },
    { key: 'green', name: 'Green', light: '#eeeed2', dark: '#769656' },
    { key: 'gray', name: 'Gray', light: '#f0f0f0', dark: '#8a8a8a' },
    { key: 'wood', name: 'Wood', light: '#e6d1a0', dark: '#8b5e3c' },
    { key: 'realwood', name: 'Holz', light: '#d8b98a', dark: '#8a5a33', img: '/board/wood4.jpg' },
    { key: 'water', name: 'Wasser', light: '#6f93b8', dark: '#3c5a78', img: '/board/blue3.jpg' },
    { key: 'marble', name: 'Marmor', light: '#e8e8e8', dark: '#9a9a9a', img: '/board/marble.jpg' },
    { key: 'metal', name: 'Metall', light: '#cfcfcf', dark: '#7a7a7a', img: '/board/metal.jpg' },
    { key: 'leather', name: 'Leder', light: '#a87c4f', dark: '#5a3d23', img: '/board/leather.jpg' },
    { key: 'maple', name: 'Ahorn', light: '#e8cfa0', dark: '#b5895a', img: '/board/maple.jpg' },
  ];

  pieceSet = 'cburnett';
  showSettings = false;
  @ViewChild('settingsPanel', { read: ElementRef }) settingsPanel?: ElementRef<HTMLElement>;
  readonly pieceSets = [
    { key: 'cburnett', name: 'Classic', preview: 'https://raw.githubusercontent.com/lichess-org/lila/master/public/piece/cburnett/wN.svg' },
    { key: 'merida', name: 'Merida', preview: '/piece/merida/wN.svg' },
    { key: 'fantasy', name: 'Fantasy', preview: '/piece/fantasy/wN.svg' },
    { key: 'spatial', name: 'Spatial', preview: '/piece/spatial/wN.svg' },
    { key: 'celtic', name: 'Celtic', preview: '/piece/celtic/wN.svg' },
    { key: 'chessnut', name: 'Chessnut', preview: '/piece/chessnut/wN.svg' },
    { key: 'rhosgfx', name: 'RhosGFX', preview: '/piece/rhosgfx/wN.svg' },
  ];

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  private chess = new Chess();
  private solutionMoves: string[] = [];
  private moveIndex = 0;
  private autoAdvanceTimer?: ReturnType<typeof setTimeout>;
  private aborted = false;
  onSolutionPath = true;
  alternativeSolve = false;
  mouseslipUsed = false;

  get displayBookName(): string {
    if (!this.puzzle) return '';
    return this.puzzle.bookFileName.replace(/_firstkey\.pgn$/, '').replace(/_/g, ' ');
  }

  constructor(
    private puzzleService: PuzzleService,
    private stockfish: StockfishService,
    private route: ActivatedRoute
  ) {
    this.loadConfig();
    this.stockfish.init().catch(() => {});
  }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.loadPuzzle(Number(idParam));
    }
  }

  ngOnDestroy(): void {
    this.stopTimer();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.stockfish.destroy();
  }

  private loadPuzzle(id: number): void {
    this.state = 'LOADING';
    this.stopTimer();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;

    this.puzzleService.getBookPuzzleById(id).subscribe({
      next: puzzle => {
        this.puzzle = puzzle;
        this.setupPuzzle(puzzle);
      },
      error: () => {
        this.state = 'LOADING';
        this.puzzle = null;
      }
    });
  }

  private setupPuzzle(puzzle: BookPuzzleDto): void {
    this.solutionMoves = puzzle.moves.split(' ');
    this.moveIndex = 0;
    this.chess = new Chess(puzzle.fen);
    this.onSolutionPath = true;
    this.aborted = false;
    this.mouseslipUsed = false;

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
      this.updateBoard();
      this.startTimer();
    }, 600);
  }

  onMoveMade(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (this.state === 'PLAYING') {
      this.handleOffPathMove(event);
      return;
    }
    if (this.state !== 'AWAITING_USER_MOVE') return;

    if (this.onSolutionPath) {
      const expectedUci = this.solutionMoves[this.moveIndex];
      const userUci = event.orig + event.dest + (event.promotion || '');

      if (userUci === expectedUci.substring(0, userUci.length)) {
        this.playMove(expectedUci);
        this.moveIndex++;

        if (this.moveIndex >= this.solutionMoves.length) {
          this.state = 'SOLVED';
          this.stopTimer();
          this.updateBoard();
        } else {
          this.state = 'THINKING';
          this.updateBoard();
          this.autoAdvanceTimer = setTimeout(() => {
            if (this.aborted) return;
            this.playMove(this.solutionMoves[this.moveIndex]);
            this.moveIndex++;
            this.updateBoard();

            if (this.moveIndex >= this.solutionMoves.length) {
              this.state = 'SOLVED';
              this.stopTimer();
            } else {
              this.state = 'AWAITING_USER_MOVE';
              this.updateBoard();
            }
          }, 400);
        }
      } else {
        if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
        this.onSolutionPath = false;
        if (this.chess.isGameOver()) { this.handleGameOver(); return; }
        this.opponentRespond();
      }
    } else {
      this.handleOffPathMove(event);
    }
  }

  private handleOffPathMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
    if (this.chess.isGameOver()) { this.handleGameOver(); return; }
    this.opponentRespond();
  }

  private async opponentRespond(): Promise<void> {
    this.state = 'THINKING';
    this.updateBoard();

    try {
      const result = await this.stockfish.getBestMove(this.chess.fen(), this.stockfishDepth);
      if (this.aborted) return;
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
      if (!this.aborted) {
        this.state = 'FAILED';
        this.stopTimer();
      }
    }
  }

  private handleGameOver(): void {
    if (this.chess.isCheckmate()) {
      const loserColor = this.chess.turn();
      const userColor = this.orientation === 'white' ? 'w' : 'b';
      if (loserColor !== userColor) {
        this.alternativeSolve = true;
        this.state = 'SOLVED';
        this.stopTimer();
        this.updateBoard();
        return;
      }
    }
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
  }

  mouseslip(): void {
    if (this.mouseslipUsed || this.onSolutionPath) return;
    this.mouseslipUsed = true;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.state === 'PLAYING') {
      this.chess.undo();
      this.chess.undo();
    } else {
      this.chess.undo();
    }
    this.aborted = false;
    this.state = 'PLAYING';
    this.updateBoard();
  }

  giveUp(): void {
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
  }

  retry(): void {
    if (!this.puzzle) return;
    this.setupPuzzle(this.puzzle);
  }

  showSolution(): void {
    if (!this.puzzle) return;
    this.solutionMoves = this.puzzle.moves.split(' ');
    this.chess = new Chess(this.puzzle.fen);
    this.playMove(this.solutionMoves[0]);
    this.updateBoard();

    let i = 1;
    const playNext = () => {
      if (i >= this.solutionMoves.length) return;
      this.playMove(this.solutionMoves[i]);
      i++;
      this.updateBoard();
      if (i < this.solutionMoves.length) {
        this.autoAdvanceTimer = setTimeout(playNext, 600);
      }
    };
    this.autoAdvanceTimer = setTimeout(playNext, 400);
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.setupPuzzle(this.puzzle);
  }

  private playMove(uci: string): void {
    const from = uci.substring(0, 2) as Square;
    const to = uci.substring(2, 4) as Square;
    const promotion = uci.length > 4 ? uci[4] as 'q' | 'r' | 'b' | 'n' : undefined;
    this.chess.move({ from, to, promotion });
    this.lastMove = [from as Key, to as Key];
  }

  private playFreeMove(orig: Key, dest: Key, promotion?: string): boolean {
    const from = orig as string as Square;
    const to = dest as string as Square;
    if (promotion) {
      try { this.chess.move({ from, to, promotion: promotion as 'q' | 'r' | 'b' | 'n' }); } catch { return false; }
    } else {
      const moves = this.chess.moves({ verbose: true });
      const match = moves.find(m => m.from === from && m.to === to);
      if (match) {
        this.chess.move(match);
      } else {
        try { this.chess.move({ from, to, promotion: 'q' }); } catch { return false; }
      }
    }
    this.lastMove = [orig, dest];
    return true;
  }

  private updateBoard(): void {
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = (this.state === 'AWAITING_USER_MOVE' || this.state === 'PLAYING') && this.turnColor === this.orientation;
    this.dests = interactive ? this.calcDests() : new Map();
  }

  private calcDests(): Map<Key, Key[]> {
    const dests = new Map<Key, Key[]>();
    const moves = this.chess.moves({ verbose: true });
    for (const m of moves) {
      const from = m.from as Key;
      if (!dests.has(from)) dests.set(from, []);
      dests.get(from)!.push(m.to as Key);
    }
    return dests;
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

  private loadConfig(): void {
    try {
      const raw = localStorage.getItem(BOOK_PUZZLE_CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved.stockfishDepth) this.stockfishDepth = saved.stockfishDepth;
      }
    } catch {}
    if (this.stockfishDepth < 1) this.stockfishDepth = 16;
    if (this.stockfishDepth > 24) this.stockfishDepth = 24;
    try { this.boardTheme = localStorage.getItem(BOARD_THEME_KEY) || 'brown'; } catch {}
    try { this.pieceSet = localStorage.getItem(PIECE_SET_KEY) || 'cburnett'; } catch {}
  }

  saveConfig(): void {
    try {
      localStorage.setItem(BOOK_PUZZLE_CONFIG_KEY, JSON.stringify({ stockfishDepth: this.stockfishDepth }));
    } catch {}
  }

  setBoardTheme(theme: string): void {
    this.boardTheme = theme;
    try { localStorage.setItem(BOARD_THEME_KEY, theme); } catch {}
  }

  setPieceSet(set: string): void {
    this.pieceSet = set;
    try { localStorage.setItem(PIECE_SET_KEY, set); } catch {}
  }

  toggleSettings(): void {
    this.showSettings = !this.showSettings;
    if (this.showSettings) {
      setTimeout(() => this.settingsPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
    }
  }
}
