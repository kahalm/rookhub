import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { CourseService } from './course.service';
import { BookPuzzleDto } from '../puzzles/puzzle.service';
import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { ReviewNavComponent } from '../puzzles/review-nav.component';
import { parseMoveShapes } from '../puzzles/move-shapes.util';
import { applyUci } from '../puzzles/puzzle-move.util';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

/** Eine Gruppe von Linien unter einem Kapitel (name=null → „ohne Kapitel"). */
interface ChapterGroup {
  name: string | null;
  lines: BookPuzzleDto[];
}

/**
 * „Durchsehen"-Ansicht eines Kurses: eine Liste ALLER Linien (optional auf ein Kapitel beschränkt)
 * links, rechts ein schreibgeschütztes Brett, auf dem die gewählte Linie Zug für Zug durchgeklickt
 * werden kann (◀/▶, Anfang/Ende, Auto-Wiedergabe, Pfeiltasten) — inkl. Zug-Kommentaren und
 * Chessable-Board-Annotationen wie in der Lösungs-Durchsicht des Buch-Puzzle-Solvers.
 *
 * Reines Ansehen: kein Quiz, kein Fortschritt, keine Zeitmessung. Stepping-Logik gespiegelt aus
 * `BookPuzzleComponent.reviewGoTo` (ganze Linie ab FEN).
 */
@Component({
  selector: 'app-course-browse',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    TranslateModule, PuzzleBoardComponent, ReviewNavComponent, LoadingSpinnerComponent,
  ],
  template: `
    <div class="browse-container">
      <div class="browse-head">
        <button mat-icon-button [routerLink]="['/courses']" [matTooltip]="'courses.browse.back' | translate"
                [attr.aria-label]="'courses.browse.back' | translate">
          <mat-icon>arrow_back</mat-icon>
        </button>
        <div class="head-text">
          <h1>{{ bookTitle || ('courses.browse.title' | translate) }}</h1>
          <p class="sub">
            {{ chapterName !== undefined ? (chapterName || ('courses.browse.noChapter' | translate)) : ('courses.browse.allChapters' | translate) }}
            <span class="dot">·</span>{{ 'courses.browse.lineCount' | translate:{ count: lines.length } }}
          </p>
        </div>
      </div>

      @if (loading) {
        <app-loading-spinner />
      } @else if (lines.length === 0) {
        <p class="empty-hint">{{ 'courses.browse.empty' | translate }}</p>
      } @else {
        <div class="browse-body">
          <!-- Linien-Liste -->
          <aside class="line-list" role="listbox">
            @for (g of groups; track g.name) {
              @if (chapterName === undefined && groups.length > 1) {
                <div class="chapter-head">{{ g.name || ('courses.browse.noChapter' | translate) }}</div>
              }
              @for (line of g.lines; track line.id) {
                <button class="line-item" role="option"
                        [class.active]="selected?.id === line.id"
                        [attr.aria-selected]="selected?.id === line.id"
                        (click)="selectLine(line)">
                  <span class="line-idx">{{ lineNumber(line) }}</span>
                  <span class="line-label">{{ line.title || line.round || ('courses.browse.line' | translate) }}</span>
                  @if (line.isInfoOnly) {
                    <mat-icon class="info-badge" [matTooltip]="'courses.browse.infoLine' | translate">menu_book</mat-icon>
                  }
                </button>
              }
            }
          </aside>

          <!-- Brett + Steuerung -->
          <section class="board-pane">
            @if (selected) {
              <div class="board-wrap">
                <app-puzzle-board
                  [fen]="boardFen"
                  [orientation]="orientation"
                  [turnColor]="turnColor"
                  [lastMove]="lastMove"
                  [viewOnly]="true"
                  [check]="isCheck"
                  [boardTheme]="boardTheme"
                  [pieceSet]="pieceSet"
                  [reviewShapes]="reviewShapes"
                />
              </div>

              <div class="controls">
                <button mat-icon-button (click)="first()" [disabled]="plyIndex === 0"
                        [matTooltip]="'courses.browse.first' | translate"><mat-icon>first_page</mat-icon></button>
                <app-review-nav [currentIndex]="plyIndex" [totalSteps]="totalPlies"
                                (prev)="prevPly()" (next)="nextPly()" />
                <button mat-icon-button (click)="last()" [disabled]="plyIndex >= totalPlies"
                        [matTooltip]="'courses.browse.last' | translate"><mat-icon>last_page</mat-icon></button>
                <button mat-icon-button (click)="toggleAutoplay()"
                        [matTooltip]="(autoplay ? 'courses.browse.pause' : 'courses.browse.play') | translate">
                  <mat-icon>{{ autoplay ? 'pause' : 'play_arrow' }}</mat-icon>
                </button>
              </div>

              @if (sanMoves.length) {
                <div class="move-strip">
                  @for (m of sanMoves; track $index) {
                    @if ($index % 2 === 0) { <span class="move-no">{{ ($index / 2) + 1 }}.</span> }
                    <button class="san" [class.current]="plyIndex === $index + 1"
                            (click)="goTo($index + 1)">{{ m }}</button>
                  }
                </div>
              }

              @if (comment) {
                <mat-card class="comment-card">
                  <mat-card-content>
                    @for (p of commentLines; track $index) { <p>{{ p }}</p> }
                  </mat-card-content>
                </mat-card>
              }

              <div class="line-nav">
                <button mat-stroked-button (click)="prevLine()" [disabled]="selectedIndex <= 0">
                  <mat-icon>chevron_left</mat-icon>{{ 'courses.browse.prevLine' | translate }}
                </button>
                <button mat-stroked-button (click)="nextLine()" [disabled]="selectedIndex >= lines.length - 1">
                  {{ 'courses.browse.nextLine' | translate }}<mat-icon>chevron_right</mat-icon>
                </button>
              </div>
            } @else {
              <p class="select-hint">{{ 'courses.browse.selectHint' | translate }}</p>
            }
          </section>
        </div>
      }
    </div>
  `,
  styles: [`
    .browse-container { max-width: 1200px; margin: 16px auto; padding: 0 16px; }
    .browse-head { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
    .head-text h1 { margin: 0; font-size: 1.25rem; }
    .sub { margin: 2px 0 0; color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.85rem; }
    .dot { margin: 0 6px; opacity: 0.5; }
    .empty-hint, .select-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }

    .browse-body { display: flex; gap: 16px; align-items: flex-start; }
    .line-list {
      flex: 0 0 300px; max-height: calc(100vh - 160px); overflow-y: auto;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 8px; padding: 4px;
    }
    .chapter-head {
      font-size: 0.72rem; font-weight: 700; text-transform: uppercase; letter-spacing: .04em;
      color: color-mix(in srgb, currentColor 55%, transparent); padding: 10px 8px 4px;
    }
    .line-item {
      display: flex; align-items: center; gap: 8px; width: 100%; text-align: left;
      background: none; border: none; cursor: pointer; color: inherit;
      padding: 7px 8px; border-radius: 6px; font-size: 0.85rem;
    }
    .line-item:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .line-item.active { background: color-mix(in srgb, var(--mat-sys-primary, #1565c0) 18%, transparent); font-weight: 600; }
    .line-idx { font-variant-numeric: tabular-nums; opacity: 0.55; min-width: 22px; text-align: right; }
    .line-label { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .info-badge { font-size: 16px; width: 16px; height: 16px; opacity: 0.6; }

    .board-pane { flex: 1; min-width: 0; display: flex; flex-direction: column; align-items: center; gap: 12px; }
    .board-wrap { width: 100%; max-width: min(60vw, 560px); }
    .controls { display: flex; align-items: center; gap: 6px; }
    .move-strip {
      display: flex; flex-wrap: wrap; gap: 2px 4px; align-items: baseline; width: 100%;
      max-width: 560px; max-height: 120px; overflow-y: auto; font-size: 0.85rem;
    }
    .move-no { opacity: 0.5; font-variant-numeric: tabular-nums; margin-left: 4px; }
    .san { background: none; border: none; cursor: pointer; color: inherit; padding: 1px 4px; border-radius: 4px; font-size: 0.85rem; }
    .san:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .san.current { background: color-mix(in srgb, var(--mat-sys-primary, #1565c0) 22%, transparent); font-weight: 700; }
    .comment-card { width: 100%; max-width: 560px; }
    .comment-card p { margin: 0 0 6px; }
    .comment-card p:last-child { margin-bottom: 0; }
    .line-nav { display: flex; gap: 8px; }

    @media (max-width: 768px) {
      .browse-body { flex-direction: column; }
      .line-list { flex: none; width: 100%; max-height: 260px; }
      .board-wrap { max-width: 100%; }
    }
  `]
})
export class CourseBrowseComponent implements OnInit, OnDestroy {
  bookId!: number;
  /** undefined = ganzes Buch; sonst der (aufgelöste) Kapitelname (null = „ohne Kapitel"). */
  chapterName: string | null | undefined = undefined;
  bookTitle = '';

  loading = true;
  lines: BookPuzzleDto[] = [];
  groups: ChapterGroup[] = [];

  selected: BookPuzzleDto | null = null;
  selectedIndex = -1;

  // Zug-Durchsicht-Zustand der aktuellen Linie
  plyIndex = 0;
  totalPlies = 0;
  boardFen = '';
  orientation: 'white' | 'black' = 'white';
  turnColor: 'white' | 'black' = 'white';
  lastMove?: [Key, Key];
  isCheck = false;
  comment: string | null = null;
  reviewShapes: DrawShape[] = [];
  sanMoves: string[] = [];

  boardTheme = 'brown';
  pieceSet = 'cburnett';

  autoplay = false;

  private uciMoves: string[] = [];
  private shapesByPly: Record<number, DrawShape[]> = {};
  private autoplayTimer: ReturnType<typeof setInterval> | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private courseService: CourseService,
    private prefs: PreferencesService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  get commentLines(): string[] {
    return (this.comment ?? '').split(/\n{2,}|\n/).map(s => s.trim()).filter(Boolean);
  }

  ngOnInit(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    const chapIdxRaw = this.route.snapshot.paramMap.get('chapterIndex');
    const chapterIndex = chapIdxRaw != null ? Number(chapIdxRaw) : null;
    this.load(chapterIndex);
  }

  ngOnDestroy(): void {
    this.stopAutoplay();
  }

  private load(chapterIndex: number | null): void {
    this.loading = true;
    this.courseService.getBookPuzzles(this.bookId).subscribe({
      next: puzzles => {
        this.bookTitle = puzzles.find(p => p.bookTitle)?.bookTitle || puzzles[0]?.bookFileName || '';
        if (chapterIndex != null) {
          // Kapitelname über die Kapitel-Übersicht auflösen, dann Linien darauf filtern.
          this.courseService.getChapters(this.bookId).subscribe({
            next: chapters => {
              const ch = chapters.find(c => c.index === chapterIndex);
              this.chapterName = ch ? ch.name : null;
              this.setLines(puzzles.filter(p => (p.chapter || null) === (this.chapterName ?? null)));
              this.loading = false;
            },
            error: () => { this.setLines(puzzles); this.loading = false; }
          });
        } else {
          this.chapterName = undefined;
          this.setLines(puzzles);
          this.loading = false;
        }
      },
      error: () => {
        this.loading = false;
        this.snackbar.info(this.translate.instant('courses.browse.loadFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  private setLines(lines: BookPuzzleDto[]): void {
    this.lines = lines;
    this.groups = this.groupByChapter(lines);
    if (lines.length) this.selectLine(lines[0]);
  }

  /** Gruppiert die Linien nach Kapitel in Vorkommens-Reihenfolge (Server liefert bereits nach Round sortiert). */
  private groupByChapter(lines: BookPuzzleDto[]): ChapterGroup[] {
    const groups: ChapterGroup[] = [];
    const byName = new Map<string | null, ChapterGroup>();
    for (const line of lines) {
      const name = line.chapter || null;
      let g = byName.get(name);
      if (!g) { g = { name, lines: [] }; byName.set(name, g); groups.push(g); }
      g.lines.push(line);
    }
    return groups;
  }

  /** 1-basierte laufende Nummer der Linie in der aktuellen (gefilterten) Liste. */
  lineNumber(line: BookPuzzleDto): number {
    return this.lines.indexOf(line) + 1;
  }

  selectLine(line: BookPuzzleDto): void {
    this.stopAutoplay();
    this.selected = line;
    this.selectedIndex = this.lines.indexOf(line);
    this.shapesByPly = parseMoveShapes(line.moveShapes);
    this.uciMoves = (line.moves || '').split(' ').filter(Boolean);
    this.totalPlies = this.uciMoves.length;
    // SAN-Folge einmal aus der FEN ableiten (für den Zug-Streifen).
    this.sanMoves = this.buildSan(line.fen, this.uciMoves);
    // Orientierung aus der Seite am Zug in der Startstellung (wie Info-Durchsicht).
    const chess = new Chess(line.fen);
    this.orientation = chess.turn() === 'w' ? 'white' : 'black';
    this.goTo(0);
  }

  private buildSan(fen: string, ucis: string[]): string[] {
    const chess = new Chess(fen);
    const san: string[] = [];
    for (const uci of ucis) {
      try { san.push(applyUci(chess, uci).san); } catch { san.push(uci); }
    }
    return san;
  }

  /** Ganze Linie ab FEN durchklicken (index = Anzahl gespielter Halbzüge). Spiegelt reviewGoTo. */
  goTo(index: number): void {
    if (!this.selected) return;
    index = Math.max(0, Math.min(index, this.uciMoves.length));
    this.plyIndex = index;
    const chess = new Chess(this.selected.fen);
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      applyUci(chess, this.uciMoves[i]);
      last = [this.uciMoves[i].substring(0, 2) as Key, this.uciMoves[i].substring(2, 4) as Key];
    }
    this.lastMove = last;
    this.boardFen = chess.fen();
    this.turnColor = chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = chess.isCheck();
    this.comment = this.latestCommentUpTo(index - 1);
    this.reviewShapes = this.shapesByPly[index - 1] ?? [];
    if (this.autoplay && index >= this.totalPlies) this.stopAutoplay();
  }

  /** Kommentar des zuletzt kommentierten Halbzugs im Bereich [-1 .. plyPlayed] (rückwärts). */
  private latestCommentUpTo(plyPlayed: number): string | null {
    const mc = this.selected?.moveComments;
    if (!mc) return null;
    for (let ply = plyPlayed; ply >= -1; ply--) {
      const c = mc[String(ply)];
      if (c) return c;
    }
    return null;
  }

  nextPly(): void { this.stopAutoplay(); this.goTo(this.plyIndex + 1); }
  prevPly(): void { this.stopAutoplay(); this.goTo(this.plyIndex - 1); }
  first(): void { this.stopAutoplay(); this.goTo(0); }
  last(): void { this.stopAutoplay(); this.goTo(this.totalPlies); }

  toggleAutoplay(): void {
    if (this.autoplay) { this.stopAutoplay(); return; }
    if (this.plyIndex >= this.totalPlies) this.goTo(0);
    this.autoplay = true;
    this.autoplayTimer = setInterval(() => {
      if (this.plyIndex >= this.totalPlies) { this.stopAutoplay(); return; }
      this.goTo(this.plyIndex + 1);
    }, 1100);
  }

  private stopAutoplay(): void {
    this.autoplay = false;
    if (this.autoplayTimer) { clearInterval(this.autoplayTimer); this.autoplayTimer = null; }
  }

  prevLine(): void {
    if (this.selectedIndex > 0) this.selectLine(this.lines[this.selectedIndex - 1]);
  }
  nextLine(): void {
    if (this.selectedIndex < this.lines.length - 1) this.selectLine(this.lines[this.selectedIndex + 1]);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    const t = e.target as HTMLElement | null;
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
    if (!this.selected) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); this.prevPly(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); this.nextPly(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); this.prevLine(); }
    else if (e.key === 'ArrowDown') { e.preventDefault(); this.nextLine(); }
  }
}
