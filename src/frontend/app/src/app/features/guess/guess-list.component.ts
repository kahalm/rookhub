import { Component, OnInit, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessService, GuessSession } from './guess.service';
import { GameAnalysis, GameAnalysisService } from '../analysis/game-analysis.service';

/**
 * Punktepartie-Übersicht (`/guess`): welche analysierten Partien lassen sich spielen, und welche
 * eigenen Durchläufe gibt es schon. Gespielt werden kann nur, was die Engine (mindestens teilweise)
 * gerechnet hat — sonst gäbe es nichts zu werten.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-guess-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatTooltipModule, MatButtonToggleModule, TranslatePipe, LoadingSpinnerComponent],
  template: `
    <div class="gl-container">
      <div class="header">
        <h1>{{ 'guess.title' | translate }}</h1>
        <a mat-stroked-button routerLink="/analysis/games">
          <mat-icon>insights</mat-icon> {{ 'guess.toAnalyses' | translate }}
        </a>
      </div>
      <p class="muted intro">{{ 'guess.intro' | translate }}</p>

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <mat-card class="start-card">
          <mat-card-content>
            <h2>{{ 'guess.startNew' | translate }}</h2>
            @if (playable.length === 0) {
              <p class="muted">{{ 'guess.noGames' | translate }}</p>
              <a mat-stroked-button routerLink="/analysis/games">{{ 'guess.analyseFirst' | translate }}</a>
            } @else {
              <div class="side-pick">
                <span class="muted small">{{ 'guess.sideLabel' | translate }}</span>
                <mat-button-toggle-group [(ngModel)]="guessWhite" aria-label="side">
                  <mat-button-toggle [value]="true">{{ 'guess.white' | translate }}</mat-button-toggle>
                  <mat-button-toggle [value]="false">{{ 'guess.black' | translate }}</mat-button-toggle>
                </mat-button-toggle-group>
              </div>
              @for (g of playable; track g.id) {
                <div class="game-row">
                  <span class="g-title">{{ g.title || ('guess.untitled' | translate) }}</span>
                  <span class="muted small">{{ 'guess.analysed' | translate:{ done: g.analyzedPlies, total: g.plyCount } }}</span>
                  <span class="spacer"></span>
                  <button mat-flat-button color="primary" [disabled]="starting" (click)="start(g)">
                    <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
                  </button>
                </div>
              }
            }
          </mat-card-content>
        </mat-card>

        @if (sessions.length) {
          <h2 class="sec">{{ 'guess.yourRuns' | translate }}</h2>
          @for (s of sessions; track s.id) {
            <mat-card class="run">
              <mat-card-content>
                <div class="run-row">
                  <a class="g-title" [routerLink]="['/guess', s.id]">{{ s.title || ('guess.untitled' | translate) }}</a>
                  <span class="muted small">{{ (s.guessWhite ? 'guess.white' : 'guess.black') | translate }}</span>
                  <span class="pts">{{ 'guess.score' | translate:{ points: s.points, max: s.maxPoints } }}</span>
                  <span class="spacer"></span>
                  <span class="chip">{{ ('guess.status.' + s.status) | translate }}</span>
                  <button mat-icon-button [attr.aria-label]="'common.delete' | translate"
                          [matTooltip]="'common.delete' | translate" (click)="remove(s)">
                    <mat-icon>delete</mat-icon>
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          }
        }
      }
    </div>
  `,
  styles: [`
    .gl-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.5rem; }
    h2 { margin: 0 0 8px; font-size: 1.05rem; }
    h2.sec { margin: 18px 0 8px; }
    .intro { margin: 4px 0 14px; }
    .start-card { margin-bottom: 8px; }
    .side-pick { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    .game-row, .run-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 6px 0; }
    .game-row + .game-row { border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .g-title { font-weight: 600; text-decoration: none; color: inherit; }
    a.g-title:hover { text-decoration: underline; }
    .spacer { flex: 1 1 auto; }
    .pts { font-variant-numeric: tabular-nums; }
    .chip { font-size: .72rem; padding: 2px 8px; border-radius: 10px; border: 1px solid currentColor;
            color: color-mix(in srgb, currentColor 65%, transparent); }
    .run { margin-bottom: 8px; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GuessListComponent implements OnInit {
  private guess = inject(GuessService);
  private analyses = inject(GameAnalysisService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  sessions: GuessSession[] = [];
  playable: GameAnalysis[] = [];
  loading = true;
  starting = false;
  guessWhite = true;

  ngOnInit(): void {
    this.analyses.list().subscribe({
      next: list => {
        // Spielbar ist, was mindestens eine gerechnete Stellung hat — auf den Rest wartet man.
        this.playable = list.filter(a => a.analyzedPlies > 0);
        this.cdr.markForCheck();
      },
      error: () => { /* die Liste bleibt leer; der Hinweis „erst analysieren" greift */ },
    });
    this.guess.list().subscribe({
      next: rows => { this.sessions = rows; this.loading = false; this.cdr.markForCheck(); },
      error: () => { this.loading = false; this.cdr.markForCheck(); },
    });
  }

  start(game: GameAnalysis): void {
    if (this.starting) return;
    this.starting = true;
    this.guess.start(game.id, this.guessWhite).subscribe({
      next: s => { this.starting = false; this.router.navigate(['/guess', s.id]); },
      error: err => {
        this.starting = false;
        this.snackbar.warn(err?.error?.message || this.translate.instant('guess.startFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  remove(s: GuessSession): void {
    this.guess.delete(s.id).subscribe({
      next: () => { this.sessions = this.sessions.filter(x => x.id !== s.id); this.cdr.markForCheck(); },
      error: () => this.snackbar.warn(this.translate.instant('guess.deleteFailed')),
    });
  }
}
