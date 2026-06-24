import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin, of, timer } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/auth.service';
import { Subscription } from '../../core/models';
import { DashboardService } from '../../core/dashboard.service';
import { ChessableService, ChessableAdminImport } from '../chessable/chessable.service';
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatListModule, TranslateModule],
  template: `
    <div class="dashboard">
      <h1>{{ 'dashboard.welcome' | translate:{ username: auth.currentUser?.username } }}</h1>
      <div class="dashboard-grid">
        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>extension</mat-icon>
            <mat-card-title>{{ 'dashboard.puzzles.title' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'dashboard.puzzles.subtitle' | translate:{ elo: puzzleElo, solved: puzzleSolved, accuracy: puzzleAccuracy } }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/puzzles">{{ 'dashboard.puzzles.solve' | translate }}</button>
            <button mat-button routerLink="/puzzles/daily/today">{{ 'dashboard.puzzles.daily' | translate }}</button>
            <button mat-button routerLink="/puzzles/endless">{{ 'dashboard.puzzles.endless' | translate }}</button>
          </mat-card-actions>
        </mat-card>

        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>emoji_events</mat-icon>
            <mat-card-title>{{ 'dashboard.subscriptions.title' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'dashboard.subscriptions.count' | translate:{ count: subscriptionCount } }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/tournaments">{{ 'dashboard.subscriptions.browse' | translate }}</button>
          </mat-card-actions>
        </mat-card>

        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>people</mat-icon>
            <mat-card-title>{{ 'dashboard.friends.title' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'dashboard.friends.count' | translate:{ count: friendCount } }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/friends">{{ 'dashboard.friends.manage' | translate }}</button>
          </mat-card-actions>
        </mat-card>

        @if (auth.isAdmin) {
          <mat-card>
            <mat-card-header>
              <mat-icon mat-card-avatar>library_books</mat-icon>
              <mat-card-title>{{ 'dashboard.repertoires.title' | translate }}</mat-card-title>
              <mat-card-subtitle>{{ 'dashboard.repertoires.count' | translate:{ count: repertoireCount } }}</mat-card-subtitle>
            </mat-card-header>
            <mat-card-actions>
              <button mat-button routerLink="/repertoires">{{ 'dashboard.repertoires.viewAll' | translate }}</button>
            </mat-card-actions>
          </mat-card>
        }

        @if (auth.isAdmin && chessableActive.length > 0) {
          <mat-card class="chessable-queue-card">
            <mat-card-header>
              <mat-icon mat-card-avatar>cloud_download</mat-icon>
              <mat-card-title>{{ 'dashboard.chessableQueue.title' | translate }}</mat-card-title>
              <mat-card-subtitle>{{ 'dashboard.chessableQueue.count' | translate:{ count: chessableActive.length } }}</mat-card-subtitle>
            </mat-card-header>
            <mat-card-actions>
              <button mat-button routerLink="/chessable">{{ 'dashboard.chessableQueue.view' | translate }}</button>
            </mat-card-actions>
          </mat-card>
        }
      </div>

      @if (auth.isAdmin && chessableActive.length > 0) {
        <h2>{{ 'dashboard.chessableQueue.heading' | translate }}</h2>
        <mat-list>
          @for (imp of chessableActive; track imp.id) {
            <mat-list-item>
              <mat-icon matListItemIcon>cloud_download</mat-icon>
              <span matListItemTitle>{{ imp.courseName || imp.bid }} — {{ imp.username }}</span>
              <span matListItemLine>{{ imp.statusLabel }}</span>
            </mat-list-item>
          }
        </mat-list>
      }

      @if (subscriptions.length > 0) {
        <h2>{{ 'dashboard.subscribedTournaments' | translate }}</h2>
        <mat-list>
          @for (sub of subscriptions; track sub.id) {
            <a mat-list-item [routerLink]="['/tournaments', sub.crawlerTournamentId]" class="tournament-link">
              <mat-icon matListItemIcon>emoji_events</mat-icon>
              <span matListItemTitle>{{ sub.tournamentName }}</span>
              <span matListItemLine>{{ 'dashboard.subscribedAt' | translate:{ date: (sub.subscribedAt | date) } }}</span>
            </a>
          }
        </mat-list>
      }
    </div>
  `,
  styles: [`
    .dashboard { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .dashboard-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(300px, 100%), 1fr)); gap: 1rem; margin: 1rem 0; }
    mat-icon[mat-card-avatar] { font-size: 40px; width: 40px; height: 40px; }
    .tournament-link { cursor: pointer; text-decoration: none; color: inherit; }
    .tournament-link:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    @media (max-width: 768px) {
      .dashboard { padding: 0.75rem; }
      h1 { font-size: 1.4rem; }
    }
  `]
})
export class DashboardComponent implements OnInit {
  private destroyRef = inject(DestroyRef);

  repertoireCount = 0;
  subscriptionCount = 0;
  friendCount = 0;
  puzzleSolved = 0;
  puzzleAccuracy = 0;
  puzzleElo = 1500;
  subscriptions: Subscription[] = [];

  /** Admin: aktive Chessable-Importe aller User (laufend/pausiert), live gepollt. */
  // Status-Label wird beim Polling-Update EINMAL berechnet und gecacht (statt je CD-Zyklus
  // ein translate.instant pro Eintrag auszuführen).
  chessableActive: (ChessableAdminImport & { statusLabel: string })[] = [];

  constructor(
    public auth: AuthService,
    private dashboardService: DashboardService,
    private chessable: ChessableService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    // Admin: aktive Chessable-Queue laufend anzeigen (sofort + alle 10 s).
    if (this.auth.isAdmin) {
      timer(0, 10000).pipe(
        switchMap(() => this.chessable.getActiveImportsAdmin().pipe(catchError(() => of([] as ChessableAdminImport[])))),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(list => this.chessableActive = list.map(imp => ({ ...imp, statusLabel: this.chessableStatus(imp) })));
    }

    forkJoin({
      repertoires: this.dashboardService.getRepertoires().pipe(catchError(() => of([]))),
      subscriptions: this.dashboardService.getSubscriptions().pipe(catchError(() => of([]))),
      friends: this.dashboardService.getFriends().pipe(catchError(() => of([]))),
      puzzleStats: this.dashboardService.getPuzzleStats().pipe(
        catchError(() => of({ totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0, puzzleElo: 1500 }))
      )
    }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ repertoires, subscriptions, friends, puzzleStats }) => {
      this.repertoireCount = repertoires.length;
      this.subscriptions = subscriptions;
      this.subscriptionCount = subscriptions.length;
      this.friendCount = friends.length;
      this.puzzleSolved = puzzleStats.solved || 0;
      this.puzzleAccuracy = puzzleStats.accuracy || 0;
      this.puzzleElo = puzzleStats.puzzleElo || 1500;
    });
  }

  /** Kurz-Status eines aktiven Imports: pausiert / Warteschlangen-Position / Hol-Fortschritt. */
  chessableStatus(imp: ChessableAdminImport): string {
    if (imp.status === 'paused') return this.translate.instant('chessable.statusPaused');
    if (imp.phase === 'queued') return this.translate.instant('chessable.queuePos', { pos: imp.queuedAhead + 1 });
    let s = this.translate.instant('chessable.phase_' + (imp.phase || 'queued'));
    if (imp.phase === 'fetching' && imp.chaptersTotal > 0) {
      s += ' ' + this.translate.instant('chessable.fetchProgress',
        { ch: imp.chaptersDone, total: imp.chaptersTotal, lines: imp.linesDone });
    }
    return s;
  }
}
