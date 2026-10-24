import { Component, OnInit, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FavoritesService, FavoritePuzzle } from '../../core/favorites.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { PuzzleThemePipe } from '../puzzles/puzzle-theme.pipe';
import { whiteToMove } from '../../shared/chess/fen-side';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-favorites',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule, IconLabelDirective,
    MatProgressSpinnerModule, TranslatePipe, LoadErrorComponent, RouterModule, PuzzleThemePipe,
  ],
  template: `
    <div class="fav-page">
      <div class="header">
        <h1><mat-icon class="title-heart">favorite</mat-icon> {{ 'favorites.title' | translate }}</h1>
        <!-- UI-Sweep 2026-10-11 (r2-rp-favorites-empty): der Untertitel wiederholte den Leertext fast wörtlich —
             bei leerer Liste steht die Erklärung nur noch im Leerzustand. -->
        @if (loading || loadError || favorites.length > 0) {
          <p class="hint">{{ 'favorites.hint' | translate }}</p>
        }
      </div>

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (loadError) {
        <app-load-error (retry)="load()" />
      } @else if (favorites.length === 0) {
        <mat-card class="empty-state">
          <mat-icon>favorite_border</mat-icon>
          <p class="empty-title">{{ 'favorites.empty' | translate }}</p>
          <p class="empty-hint">{{ 'favorites.emptyHint' | translate }}</p>
          <a mat-flat-button color="primary" routerLink="/puzzles">
            <mat-icon>extension</mat-icon>{{ 'favorites.solvePuzzles' | translate }}
          </a>
        </mat-card>
      } @else {
        <div class="list">
          @for (f of favorites; track f.id) {
            <mat-card class="fav">
              <div class="info">
                <mat-icon class="src" [matTooltip]="(f.source === 'Book' ? 'favorites.sourceBook' : 'favorites.sourceStandard') | translate">
                  {{ f.source === 'Book' ? 'menu_book' : 'extension' }}
                </mat-icon>
                <div class="meta">
                  <span class="line1">
                    @if (f.title) { <strong class="title">{{ f.title }}</strong> }
                    <span class="rating">{{ 'favorites.rating' | translate }} {{ f.rating }}</span>
                  </span>
                  @if (f.themes) {
                    <span class="themes">
                      @for (t of themeList(f.themes); track t) { <span class="chip">{{ t | puzzleTheme }}</span> }
                    </span>
                  }
                  <span class="date">{{ f.createdAt | date:'mediumDate' }}</span>
                </div>
              </div>
              <div class="actions">
                <button mat-icon-button (click)="replay(f)" [appIconLabel]="'favorites.replay' | translate">
                  <mat-icon>play_arrow</mat-icon>
                </button>
                <button mat-icon-button (click)="analyze(f)" [appIconLabel]="'favorites.analyze' | translate">
                  <mat-icon>biotech</mat-icon>
                </button>
                <button mat-icon-button color="warn" (click)="remove(f)" [appIconLabel]="'favorites.removeTooltip' | translate">
                  <mat-icon>heart_broken</mat-icon>
                </button>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .fav-page { max-width: 900px; margin: 0 auto; padding: 16px; }
    /* UI-Sweep z1 (z1-rk-favorites-abstand): der Abstand hängt am Kopf, nicht am (bei leerer Liste fehlenden) Untertitel. */
    .header { margin-bottom: 16px; }
    .header h1 { margin: 0 0 4px; display: flex; align-items: center; gap: 8px; }
    .title-heart { color: #e91e63; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; font-size: 0.9rem; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty-state mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .empty-state a mat-icon { font-size: 20px; width: 20px; height: 20px; opacity: 1; }
    .empty-state p { margin: 0; }
    .empty-title { font-size: 1.05rem; }
    /* a1-rk-leer-hinweis-blass: .empty-state blasst schon auf 60 % ab — nicht ein zweites Mal (36 %, hell ~2,5:1). */
    .empty-hint { font-size: 0.85rem; color: inherit; }
    .list { display: flex; flex-direction: column; gap: 8px; }
    .fav { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 12px; }
    .info { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .src { flex-shrink: 0; opacity: 0.7; }
    .meta { display: flex; flex-direction: column; min-width: 0; gap: 2px; }
    .line1 { display: flex; gap: 8px; align-items: baseline; flex-wrap: wrap; }
    .title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 40ch; }
    .rating { font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .themes { display: flex; gap: 4px; flex-wrap: wrap; }
    .chip { font-size: 0.72rem; padding: 1px 7px; border-radius: 10px; text-transform: capitalize; background: color-mix(in srgb, currentColor 10%, transparent); }
    .date { font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .actions { display: flex; flex-shrink: 0; }
    @media (max-width: 599.98px) {
      .fav { flex-direction: column; align-items: stretch; }
      .actions { justify-content: flex-end; }
    }
  `]
})
export class FavoritesComponent implements OnInit {
  favorites: FavoritePuzzle[] = [];
  loading = true;
  /** Laden gescheitert — sonst saehe die Seite aus wie „noch keine Favoriten". */
  loadError = false;
  private destroyRef = inject(DestroyRef);
  private confirm = inject(ConfirmService);

  constructor(
    private service: FavoritesService,
    private router: Router,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.loadError = false;
    this.service.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.favorites = list; this.loading = false; },
      error: () => { this.loading = false; this.loadError = true; },
    });
  }

  themeList(themes: string): string[] {
    return themes.split(/[\s,]+/).filter(t => t).slice(0, 6);
  }

  /** Puzzle erneut lösen (quellen-passender Deep-Link zum Solver). */
  replay(f: FavoritePuzzle): void {
    if (f.source === 'Book') {
      this.router.navigate(['/puzzles/book', f.puzzleId], { queryParams: { single: 1 } });
    } else {
      this.router.navigate(['/puzzles', f.puzzleId]);
    }
  }

  /** Stellung + Zugfolge direkt im Analysemodus öffnen. */
  analyze(f: FavoritePuzzle): void {
    const moves = (f.moves || '').split(' ').filter(m => m).join(',');
    this.router.navigate(['/analysis'], {
      queryParams: { fen: f.fen, moves, orientation: this.orientationFor(f), from: '/favorites' },
    });
  }

  /** Aus den Favoriten nehmen — erst nach Rückfrage (F5-016): ohne Rückgängig müsste man das Puzzle
   *  erst wiederfinden, um es erneut zu markieren. */
  remove(f: FavoritePuzzle): void {
    this.confirm.ask('favorites.removeConfirm').subscribe(ok => {
      if (!ok) return;
      const source = f.source === 'Book' ? 'book' : 'standard';
      this.service.remove(source, f.puzzleId).subscribe({
        next: () => { this.favorites = this.favorites.filter(x => x.id !== f.id); },
        error: () => this.snackbar.warn(this.translate.instant('favorites.removeError')),
      });
    });
  }

  /** Brett-Ausrichtung aus FEN + Quelle: Standard-/Lichess-Puzzles starten mit dem Gegnerzug
   *  (Spieler ist die andere Seite); Buch-Stellungen sind meist schon die Trainingsstellung. */
  private orientationFor(f: FavoritePuzzle): 'white' | 'black' {
    const turn = whiteToMove(f.fen) ? 'white' : 'black';
    if (f.source === 'Book') return turn;
    return turn === 'white' ? 'black' : 'white';
  }
}
