import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, interval } from 'rxjs';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GameAnalysis, GameAnalysisPosition, GameAnalysisService } from './game-analysis.service';

/**
 * Eine Partie-Analyse durchblättern (`/analysis/games/:id`): Brett links, Zugliste rechts, je Zug
 * die Bewertung der Engine. Kein Motor im Browser — alles kommt aus der gespeicherten Analyse.
 *
 * <p>Solange die Partie noch rechnet, frischt sich die Seite alle 10 s auf und die noch offenen
 * Züge stehen grau da; fertig gerechnete zeigen ihre Bewertung. So kann man die Partie ansehen,
 * während hinten weitergerechnet wird.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-game-analysis-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent],
  template: `
    <div class="gad-container">
      @if (loading) {
        <app-loading-spinner />
      } @else if (!analysis) {
        <mat-card><mat-card-content>
          <p>{{ 'gameAnalysis.notFound' | translate }}</p>
          <a mat-stroked-button routerLink="/analysis/games">{{ 'gameAnalysis.back' | translate }}</a>
        </mat-card-content></mat-card>
      } @else {
        <div class="header">
          <div>
            <h1>{{ analysis.title || ('gameAnalysis.untitled' | translate) }}</h1>
            <p class="muted small">
              {{ 'gameAnalysis.depthLines' | translate:{ depth: analysis.targetDepth, lines: analysis.multiPv } }}
              @if (analysis.result) { <span>· {{ analysis.result }}</span> }
            </p>
          </div>
          <a mat-stroked-button routerLink="/analysis/games"><mat-icon>arrow_back</mat-icon> {{ 'gameAnalysis.back' | translate }}</a>
        </div>

        @if (analysis.status !== 'done') {
          <div class="progress">
            <mat-progress-bar mode="determinate" [value]="percent"></mat-progress-bar>
            <span class="muted small">{{ 'gameAnalysis.progress' | translate:{ done: analysis.analyzedPlies, total: analysis.plyCount } }}</span>
          </div>
        }

        <div class="body">
          <div class="board-col">
            <app-chess-board [fen]="currentFen" [lastMove]="lastMove" [flipped]="flipped"
                             [boardTheme]="boardTheme" [pieceSet]="pieceSet" />
            <div class="nav">
              <button mat-icon-button (click)="go(-1)" [disabled]="index < 0"
                      [matTooltip]="'gameAnalysis.prev' | translate" [attr.aria-label]="'gameAnalysis.prev' | translate">
                <mat-icon>chevron_left</mat-icon>
              </button>
              <span class="pos-label">{{ label }}</span>
              <button mat-icon-button (click)="go(1)" [disabled]="index >= positions.length - 1"
                      [matTooltip]="'gameAnalysis.next' | translate" [attr.aria-label]="'gameAnalysis.next' | translate">
                <mat-icon>chevron_right</mat-icon>
              </button>
              <button mat-icon-button (click)="flipped = !flipped"
                      [matTooltip]="'gameAnalysis.flip' | translate" [attr.aria-label]="'gameAnalysis.flip' | translate">
                <mat-icon>swap_vert</mat-icon>
              </button>
            </div>
          </div>

          <mat-card class="moves-col">
            <mat-card-content>
              <div class="moves">
                @for (p of positions; track p.ply) {
                  <button class="move" type="button" [class.current]="p.ply === index" [class.pending]="!p.analyzed"
                          (click)="select(p.ply)">
                    <span class="no">@if (p.white) { {{ p.moveNumber }}. } @else { {{ p.moveNumber }}… }</span>
                    <span class="san">{{ p.san }}</span>
                    <span class="ev">{{ p.analyzed ? (p.evalText || '') : '…' }}</span>
                  </button>
                }
              </div>
            </mat-card-content>
          </mat-card>
        </div>
      }
    </div>
  `,
  styles: [`
    .gad-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.4rem; }
    .progress { display: flex; align-items: center; gap: 10px; margin: 10px 0; flex-wrap: wrap; }
    .progress mat-progress-bar { flex: 1 1 240px; }
    .body { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; margin-top: 12px; }
    .board-col { flex: 1 1 320px; max-width: 520px; }
    .nav { display: flex; align-items: center; gap: 4px; justify-content: center; margin-top: 6px; }
    .pos-label { min-width: 90px; text-align: center; font-variant-numeric: tabular-nums; }
    .moves-col { flex: 1 1 260px; max-width: 420px; }
    /* Die Zugliste kann länger sein als der Bildschirm hoch ist — sie scrollt IN SICH,
       damit die Seite im Hochformat nicht seitlich oder endlos wächst. */
    .moves { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 2px;
             max-height: 60vh; overflow-y: auto; }
    .move { display: flex; align-items: baseline; gap: 6px; background: none; border: 1px solid transparent;
            border-radius: 4px; padding: 3px 6px; font: inherit; color: inherit; cursor: pointer; text-align: left; }
    .move:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .move.current { border-color: #1976d2; background: color-mix(in srgb, #1976d2 12%, transparent); }
    .move.pending .ev { opacity: .5; }
    .no { color: color-mix(in srgb, currentColor 55%, transparent); font-size: .8rem; min-width: 34px; }
    .san { font-weight: 600; }
    .ev { margin-left: auto; font-size: .8rem; font-variant-numeric: tabular-nums;
          color: color-mix(in srgb, currentColor 70%, transparent); }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GameAnalysisDetailComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private service = inject(GameAnalysisService);
  private prefs = inject(PreferencesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  analysis: GameAnalysis | null = null;
  positions: GameAnalysisPosition[] = [];
  loading = true;
  flipped = false;
  /** Angezeigter Halbzug; -1 = Ausgangsstellung vor dem ersten Zug. */
  index = -1;

  private poll?: Subscription;

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }

  get percent(): number {
    const a = this.analysis;
    return a && a.plyCount > 0 ? Math.round((100 * a.analyzedPlies) / a.plyCount) : 0;
  }

  /** Stellung NACH dem gewählten Zug — also die FEN des nächsten Halbzugs; am Ende die letzte. */
  get currentFen(): string {
    if (this.positions.length === 0) return 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    const next = this.positions[this.index + 1];
    return next ? next.fen : this.positions[this.positions.length - 1].fen;
  }

  get lastMove(): [string, string] | undefined {
    const p = this.positions[this.index];
    if (!p?.uci || p.uci.length < 4) return undefined;
    return [p.uci.slice(0, 2), p.uci.slice(2, 4)];
  }

  get label(): string {
    const p = this.positions[this.index];
    if (!p) return this.translate.instant('gameAnalysis.startPosition');
    return `${p.moveNumber}${p.white ? '.' : '…'} ${p.san}`;
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.load(id);
    this.poll = interval(10000).subscribe(() => {
      if (this.analysis && this.analysis.status !== 'done' && this.analysis.status !== 'failed') this.load(id, true);
    });
  }

  ngOnDestroy(): void { this.poll?.unsubscribe(); }

  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (e.key === 'ArrowLeft') { this.go(-1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { this.go(1); e.preventDefault(); }
  }

  select(ply: number): void { this.index = ply; }

  go(delta: number): void {
    const next = this.index + delta;
    if (next >= -1 && next < this.positions.length) this.index = next;
  }

  private load(id: number, silent = false): void {
    if (!silent) this.loading = true;
    this.service.get(id).subscribe({
      next: a => {
        this.analysis = a;
        this.positions = a.positions ?? [];
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        if (!silent) this.snackbar.warn(this.translate.instant('gameAnalysis.loadFailed'));
        this.cdr.markForCheck();
      },
    });
  }
}
