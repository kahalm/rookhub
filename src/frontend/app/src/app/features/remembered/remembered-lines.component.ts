import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { RememberedService, RememberedPosition } from '../../core/remembered.service';

/**
 * Zeigt die über die RepCheck-Extension („Remember line" auf chessable.com) gemerkten Stellungen
 * des Users: je Eintrag ein Brett-Vorschau (FEN), Kursname/-Link, Datum + Aktionen
 * (In Analyse öffnen · FEN kopieren · Löschen).
 */
@Component({
  selector: 'app-remembered-lines',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, TranslatePipe, ChessBoardComponent,
  ],
  template: `
    <div class="remembered-page">
      <div class="head">
        <h1>{{ 'remembered.title' | translate }}</h1>
        <p class="hint">{{ 'remembered.hint' | translate }}</p>
      </div>

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (items.length === 0) {
        <mat-card class="empty">
          <mat-icon>bookmark_border</mat-icon>
          <p>{{ 'remembered.empty' | translate }}</p>
        </mat-card>
      } @else {
        <div class="grid">
          @for (p of items; track p.id) {
            <mat-card class="item">
              <div class="board">
                <app-chess-board [fen]="p.fen" [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
              </div>
              <div class="meta">
                <div class="course">
                  @if (p.sourceUrl) {
                    <a [href]="p.sourceUrl" target="_blank" rel="noopener">{{ p.courseName || p.courseId || ('remembered.unknownCourse' | translate) }}<mat-icon class="ext">open_in_new</mat-icon></a>
                  } @else {
                    <span>{{ p.courseName || p.courseId || ('remembered.unknownCourse' | translate) }}</span>
                  }
                </div>
                <div class="date">{{ p.createdAt | date:'medium' }}</div>
                <div class="fen" [matTooltip]="p.fen">{{ p.fen }}</div>
              </div>
              <div class="actions">
                <a mat-stroked-button routerLink="/analysis" [queryParams]="{ fen: p.fen }">
                  <mat-icon>science</mat-icon> {{ 'remembered.analyze' | translate }}
                </a>
                <button mat-icon-button (click)="copyFen(p)" [matTooltip]="'remembered.copyFen' | translate">
                  <mat-icon>content_copy</mat-icon>
                </button>
                <button mat-icon-button color="warn" (click)="remove(p)" [matTooltip]="'remembered.delete' | translate">
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
    .remembered-page { max-width: 1100px; margin: 0 auto; padding: 16px; }
    .head h1 { margin: 0 0 4px; }
    .head .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 16px; }
    .item { padding: 10px; display: flex; flex-direction: column; gap: 8px; }
    .board { width: 100%; }
    .board app-chess-board { display: block; width: 100%; }
    .meta { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
    .course { font-weight: 500; }
    .course a { display: inline-flex; align-items: center; gap: 3px; color: #1976d2; text-decoration: none; }
    .course a:hover { text-decoration: underline; }
    .course .ext { font-size: 14px; width: 14px; height: 14px; }
    .date { font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .fen { font-family: monospace; font-size: 0.72rem; color: color-mix(in srgb, currentColor 55%, transparent);
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .actions { display: flex; align-items: center; gap: 4px; margin-top: auto; }
    .actions a { flex: 1; }
  `]
})
export class RememberedLinesComponent implements OnInit {
  items: RememberedPosition[] = [];
  loading = true;
  private destroyRef = inject(DestroyRef);

  constructor(
    private remembered: RememberedService,
    public preferences: PreferencesService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading = true;
    this.remembered.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: items => { this.items = items; this.loading = false; },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('remembered.errors.load')); },
    });
  }

  async copyFen(p: RememberedPosition): Promise<void> {
    try {
      await navigator.clipboard.writeText(p.fen);
      this.snackbar.copy(this.translate.instant('remembered.copied'));
    } catch {
      this.snackbar.info(this.translate.instant('remembered.copyFailed'));
    }
  }

  remove(p: RememberedPosition): void {
    if (!confirm(this.translate.instant('remembered.deleteConfirm'))) return;
    this.remembered.remove(p.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.items = this.items.filter(x => x.id !== p.id); },
      error: () => this.snackbar.info(this.translate.instant('remembered.errors.delete')),
    });
  }
}
