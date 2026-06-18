import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { GamesService, SavedGame } from './games.service';
import { PgnViewerComponent } from '../../shared/pgn-viewer/pgn-viewer.component';
import { SnackbarService } from '../../core/snackbar.service';

@Component({
  selector: 'app-games-list',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, MatDialogModule, TranslateModule,
  ],
  template: `
    <div class="games-page">
      <div class="head">
        <h1>{{ 'games.title' | translate }}</h1>
        <p class="hint">{{ 'games.hint' | translate }}</p>
      </div>

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (games.length === 0) {
        <mat-card class="empty">
          <mat-icon>sports_esports</mat-icon>
          <p>{{ 'games.empty' | translate }}</p>
        </mat-card>
      } @else {
        <div class="list">
          @for (g of games; track g.id) {
            <mat-card class="game">
              <div class="info">
                <mat-icon class="src" [matTooltip]="g.source">{{ sourceIcon(g.source) }}</mat-icon>
                <div class="players">
                  <span class="vs"><strong>{{ g.white || '?' }}</strong> – <strong>{{ g.black || '?' }}</strong></span>
                  <span class="meta">
                    @if (g.result && g.result !== '*') { <span class="result">{{ g.result }}</span> }
                    <span>{{ g.moveCount }} {{ 'games.moves' | translate }}</span>
                    <span class="date">{{ (g.playedAt || g.createdAt) | date:'mediumDate' }}</span>
                  </span>
                </div>
              </div>
              <div class="actions">
                <button mat-icon-button (click)="replay(g)" [matTooltip]="'games.replay' | translate" [attr.aria-label]="'games.replay' | translate">
                  <mat-icon>play_arrow</mat-icon>
                </button>
                <button mat-icon-button (click)="openInAnalysis(g)" [matTooltip]="'games.openInAnalysis' | translate" [attr.aria-label]="'games.openInAnalysis' | translate">
                  <mat-icon>biotech</mat-icon>
                </button>
                <button mat-icon-button (click)="share(g)" [matTooltip]="'games.share' | translate" [attr.aria-label]="'games.share' | translate">
                  <mat-icon>share</mat-icon>
                </button>
                @if (g.sourceUrl) {
                  <a mat-icon-button [href]="g.sourceUrl" target="_blank" rel="noopener" [matTooltip]="'games.openOriginal' | translate" [attr.aria-label]="'games.openOriginal' | translate">
                    <mat-icon>open_in_new</mat-icon>
                  </a>
                }
                <button mat-icon-button color="warn" (click)="remove(g)" [matTooltip]="'common.delete' | translate" [attr.aria-label]="'common.delete' | translate">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .games-page { max-width: 900px; margin: 0 auto; padding: 16px; }
    .head h1 { margin: 0 0 4px; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; font-size: 0.9rem; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .list { display: flex; flex-direction: column; gap: 8px; }
    .game { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 12px; }
    .info { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .src { flex-shrink: 0; opacity: 0.7; }
    .players { display: flex; flex-direction: column; min-width: 0; }
    .vs { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .meta { display: flex; gap: 10px; font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .result { color: #1976d2; font-weight: 600; }
    .actions { display: flex; flex-shrink: 0; }
    @media (max-width: 600px) {
      .game { flex-direction: column; align-items: stretch; }
      .actions { justify-content: flex-end; }
    }
  `]
})
export class GamesListComponent implements OnInit {
  games: SavedGame[] = [];
  loading = true;
  private destroyRef = inject(DestroyRef);

  constructor(
    private service: GamesService,
    private dialog: MatDialog,
    private router: Router,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.service.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.games = list; this.loading = false; },
      error: () => { this.loading = false; },
    });
  }

  sourceIcon(source: string): string {
    return source === 'lichess' ? 'public' : 'sports_esports';
  }

  /** PGN nachladen und im wiederverwendbaren PGN-Viewer-Dialog durchspielen. */
  replay(g: SavedGame): void {
    this.service.get(g.id).subscribe({
      next: detail => {
        this.dialog.open(PgnViewerComponent, {
          data: { pgn: detail.pgn },
          width: '90vw',
          maxWidth: '900px',
        });
      },
      error: () => this.snackbar.warn(this.translate.instant('games.loadError')),
    });
  }

  /** PGN nachladen und in der Analyse-Seite öffnen (Übergabe via Router-State). */
  openInAnalysis(g: SavedGame): void {
    this.service.get(g.id).subscribe({
      next: detail => this.router.navigate(['/analysis'], { state: { pgn: detail.pgn }, queryParams: { from: '/games' } }),
      error: () => this.snackbar.warn(this.translate.instant('games.loadError')),
    });
  }

  /** Eindeutigen Teilen-Link in die Zwischenablage kopieren. */
  share(g: SavedGame): void {
    const url = this.service.shareUrl(g.shareToken);
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareCopied')),
      () => this.snackbar.warn(url),
    );
  }

  remove(g: SavedGame): void {
    if (!confirm(this.translate.instant('games.deleteConfirm'))) return;
    this.service.delete(g.id).subscribe({
      next: () => { this.games = this.games.filter(x => x.id !== g.id); },
      error: () => this.snackbar.warn(this.translate.instant('games.deleteError')),
    });
  }
}
