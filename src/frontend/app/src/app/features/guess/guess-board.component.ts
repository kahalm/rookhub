import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessResult, GuessReviewMove, GuessService, GuessSession } from './guess.service';

/**
 * Punktepartie (`/guess/:id`): Zug für Zug raten, sofort eine Rückmeldung samt Punkten, dann rückt
 * die Partie zwei Halbzüge weiter (eigener Zug + Antwort des Gegners).
 *
 * <p>Der Partiezug kommt erst mit der ANTWORT des Servers — vorher kennt der Client ihn nicht
 * (siehe `GuessSessionService`). Deshalb setzt das Brett den geratenen Zug auch nicht selbst um:
 * gezeigt wird, was der Server zurückmeldet.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-guess-board',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent],
  template: `
    <div class="gb-container">
      @if (loading) {
        <app-loading-spinner />
      } @else if (!session) {
        <mat-card><mat-card-content>
          <p>{{ 'guess.notFound' | translate }}</p>
          <a mat-stroked-button routerLink="/guess">{{ 'guess.back' | translate }}</a>
        </mat-card-content></mat-card>
      } @else {
        <div class="header">
          <div>
            <h1>{{ session.title || ('guess.untitled' | translate) }}</h1>
            <p class="muted small">
              {{ (session.guessWhite ? 'guess.playingWhite' : 'guess.playingBlack') | translate }}
              · {{ 'guess.score' | translate:{ points: session.points, max: session.maxPoints } }}
              @if (session.movesPlayed > 0) {
                · {{ 'guess.hits' | translate:{ hits: session.gameMoveHits, moves: session.movesPlayed } }}
              }
            </p>
          </div>
          <a mat-stroked-button routerLink="/guess"><mat-icon>arrow_back</mat-icon> {{ 'guess.back' | translate }}</a>
        </div>

        <div class="body">
          <div class="board-col">
            <app-chess-board [fen]="boardFen" [lastMove]="lastMove" [flipped]="!session.guessWhite"
                             [boardTheme]="boardTheme" [pieceSet]="pieceSet"
                             [playable]="canGuess" (userMove)="onMove($event)" />
            @if (session.status === 'running') {
              <div class="actions">
                <button mat-stroked-button (click)="skip()" [disabled]="busy">
                  <mat-icon>skip_next</mat-icon> {{ 'guess.skip' | translate }}
                </button>
                <span class="muted small">{{ 'guess.yourTurn' | translate:{ move: moveLabel } }}</span>
              </div>
            }
          </div>

          <div class="side-col">
            @if (last) {
              <mat-card class="feedback" [class]="'g-' + (last.grade || 'skipped')">
                <mat-card-content>
                  <div class="fb-head">
                    <span class="pts">{{ last.points > 0 ? '+' : '' }}{{ last.points }}</span>
                    <span class="grade">{{ ('guess.grade.' + (last.grade || 'skipped')) | translate }}</span>
                  </div>
                  <p class="fb-line">
                    @if (last.playedSan) {
                      <span>{{ 'guess.youPlayed' | translate:{ move: last.playedSan } }}</span> ·
                    }
                    <span>{{ 'guess.gamePlayed' | translate:{ move: last.gameMoveSan } }}</span>
                    @if (last.replySan) { · <span>{{ 'guess.reply' | translate:{ move: last.replySan } }}</span> }
                  </p>
                  @if (last.evalText) {
                    <p class="muted small">{{ 'guess.evalAfter' | translate:{ eval: last.evalText } }}</p>
                  }
                </mat-card-content>
              </mat-card>
            }

            @if (session.status === 'done') {
              <mat-card class="done">
                <mat-card-content>
                  <h2>{{ 'guess.finished' | translate }}</h2>
                  <p class="final">{{ 'guess.score' | translate:{ points: session.points, max: session.maxPoints } }}</p>
                  <p class="muted small">{{ 'guess.hits' | translate:{ hits: session.gameMoveHits, moves: session.movesPlayed } }}</p>
                </mat-card-content>
              </mat-card>

              @if (review.length) {
                <mat-card>
                  <mat-card-content>
                    <h3>{{ 'guess.review' | translate }}</h3>
                    <div class="review">
                      @for (r of review; track r.ply) {
                        <div class="rev-row" [class]="'g-' + (r.grade || 'skipped')">
                          <span class="no">{{ r.moveNumber }}{{ r.white ? '.' : '…' }}</span>
                          <span class="game">{{ r.gameSan }}</span>
                          <span class="yours">{{ r.playedSan || '–' }}</span>
                          <span class="pts">{{ r.points > 0 ? '+' : '' }}{{ r.points }}</span>
                        </div>
                      }
                    </div>
                  </mat-card-content>
                </mat-card>
              }
            } @else {
              <mat-progress-bar mode="determinate" [value]="progress"></mat-progress-bar>
              <p class="muted small">{{ 'guess.progress' | translate:{ done: session.movesPlayed, total: session.totalGuesses } }}</p>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .gb-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.4rem; }
    h2 { margin: 0 0 4px; font-size: 1.1rem; }
    h3 { margin: 0 0 8px; font-size: 1rem; }
    .body { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; margin-top: 12px; }
    .board-col { flex: 1 1 320px; max-width: 520px; }
    .side-col { flex: 1 1 280px; max-width: 440px; display: flex; flex-direction: column; gap: 10px; }
    .actions { display: flex; align-items: center; gap: 10px; margin-top: 8px; flex-wrap: wrap; }
    .feedback .fb-head { display: flex; align-items: baseline; gap: 10px; }
    .feedback .pts { font-size: 1.6rem; font-weight: 700; font-variant-numeric: tabular-nums; }
    .feedback .grade { font-weight: 600; }
    .fb-line { margin: 6px 0 0; }
    .final { font-size: 1.4rem; font-weight: 700; margin: 0; }
    /* Farbe nach Güte — dieselbe Ordnung wie die Punkte. */
    .g-clearlyBetter .pts, .g-better .pts, .g-onlyMove .pts, .g-gameMove .pts { color: #2e7d32; }
    .g-similar .pts { color: #558b2f; }
    .g-worse .pts, .g-skipped .pts { color: color-mix(in srgb, currentColor 60%, transparent); }
    .g-muchWorse .pts { color: #c62828; }
    /* Der Rückblick kann lang sein — er scrollt IN SICH, nicht die Seite. */
    .review { display: flex; flex-direction: column; gap: 2px; max-height: 50vh; overflow-y: auto; }
    .rev-row { display: grid; grid-template-columns: 44px 1fr 1fr 44px; gap: 8px; align-items: baseline;
               padding: 2px 4px; border-radius: 3px; }
    .rev-row .no { color: color-mix(in srgb, currentColor 55%, transparent); font-size: .8rem; }
    .rev-row .game { font-weight: 600; }
    .rev-row .yours { color: color-mix(in srgb, currentColor 75%, transparent); }
    .rev-row .pts { text-align: right; font-variant-numeric: tabular-nums; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GuessBoardComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private service = inject(GuessService);
  private prefs = inject(PreferencesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  session: GuessSession | null = null;
  last: GuessResult | null = null;
  review: GuessReviewMove[] = [];
  loading = true;
  busy = false;

  /** Was das Brett zeigt: die zu ratende Stellung bzw. nach einem Zug die Stellung danach. */
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  lastMove?: [string, string];

  private since = Date.now();

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }
  get canGuess(): boolean { return !!this.session?.position && !this.busy && this.session.status === 'running'; }

  get moveLabel(): string {
    const p = this.session?.position;
    return p ? `${p.moveNumber}${p.whiteToMove ? '.' : '…'}` : '';
  }

  get progress(): number {
    const s = this.session;
    return s && s.totalGuesses > 0 ? Math.round((100 * s.movesPlayed) / s.totalGuesses) : 0;
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.get(id).subscribe({
      next: s => { this.apply(s); this.loading = false; this.cdr.markForCheck(); },
      error: () => { this.loading = false; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { /* nichts zu lösen — die Zeit wird je Zug gemeldet */ }

  onMove(m: UserBoardMove): void {
    if (!this.canGuess) return;
    this.send(m.from + m.to);
  }

  skip(): void {
    if (!this.canGuess) return;
    this.send(null);
  }

  private send(uci: string | null): void {
    const id = this.session?.id;
    if (!id) return;
    this.busy = true;
    const seconds = Math.min(3600, Math.max(0, Math.round((Date.now() - this.since) / 1000)));

    this.service.guess(id, uci, seconds).subscribe({
      next: res => {
        this.busy = false;
        this.last = res;
        this.apply(res.session);
        if (res.session.status === 'done') this.loadReview(id);
        this.cdr.markForCheck();
      },
      error: err => {
        this.busy = false;
        // Der Nutzer hat gerade gezogen — ein stiller Fehlschlag wäre hier das Schlimmste.
        this.snackbar.warn(err?.error?.message || this.translate.instant('guess.moveFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  private apply(s: GuessSession): void {
    this.session = s;
    this.since = Date.now();
    if (s.position) {
      this.boardFen = s.position.fen;
      this.lastMove = s.position.lastMoveUci
        ? [s.position.lastMoveUci.slice(0, 2), s.position.lastMoveUci.slice(2, 4)]
        : undefined;
    }
  }

  private loadReview(id: number): void {
    this.service.review(id).subscribe({
      next: rows => { this.review = rows; this.cdr.markForCheck(); },
      error: () => { /* der Rückblick ist Zugabe — die Sitzung ist bereits gewertet */ },
    });
  }
}
