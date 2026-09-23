import { Component, OnInit, HostListener, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { PgnViewerService } from '../../shared/pgn-viewer/pgn-viewer.service';
import { PreferencesService } from '../../core/preferences.service';
import { GamesService, SharedGame } from './games.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';

/**
 * Öffentliche Nachspiel-Seite einer geteilten Partie (Route <c>/g/:token</c>, kein Login nötig).
 * Reused den PgnViewerService + chess-board/move-list aus dem PGN-Viewer, aber inline statt im Dialog.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-shared-game',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule,
    TranslatePipe, ChessBoardComponent, MoveListComponent, PositionRepertoiresComponent,
  ],
  providers: [PgnViewerService],
  template: `
    <div class="shared-page">
      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound) {
        <mat-card class="empty">
          <mat-icon>link_off</mat-icon>
          <p>{{ 'games.shared.notFound' | translate }}</p>
        </mat-card>
      } @else if (game) {
        <mat-card class="viewer">
          <div class="header">
            <div class="header-main">
              <span class="players">
                <strong>{{ game.white || '?' }}</strong>@if (game.whiteElo) { <span class="elo">({{ game.whiteElo }})</span> }
                –
                <strong>{{ game.black || '?' }}</strong>@if (game.blackElo) { <span class="elo">({{ game.blackElo }})</span> }
              </span>
              <span class="meta">
                @if (game.result && game.result !== '*') { <span class="result">{{ game.result }}</span> }
                <span>{{ game.source }}</span>
                <span class="date">{{ (game.playedAt || game.createdAt) | date:'mediumDate' }}</span>
              </span>
            </div>
            <!-- Am PC in die Kopfzeile: als eigene Zeile unter Brett und Zugliste war der Knopf so breit wie
                 die Karte und stand mitten im Leeren. Auf dem Handy fällt die Kopfzeile in eine Spalte. -->
            @if (game.sourceUrl) {
              <a mat-stroked-button [href]="game.sourceUrl" target="_blank" rel="noopener" class="original">
                <mat-icon>open_in_new</mat-icon> {{ 'games.openOriginal' | translate }}
              </a>
            }
          </div>
          <div class="body">
            <div class="board-section">
              <div class="board-wrap">
                <app-chess-board [fen]="service.currentFen" [lastMove]="service.lastMove" [flipped]="flipped"
                                 [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                <div class="board-tap board-tap-prev" (click)="service.goBack()"></div>
                <div class="board-tap board-tap-next" (click)="service.goForward()"></div>
              </div>
              <div class="nav">
                <button mat-icon-button (click)="service.goToStart()" [disabled]="service.currentMoveIndex < 0"><mat-icon>skip_previous</mat-icon></button>
                <button mat-icon-button (click)="service.goBack()" [disabled]="service.currentMoveIndex < 0"><mat-icon>navigate_before</mat-icon></button>
                <button mat-icon-button (click)="service.goForward()" [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"><mat-icon>navigate_next</mat-icon></button>
                <button mat-icon-button (click)="service.goToEnd()" [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"><mat-icon>skip_next</mat-icon></button>
                <button mat-icon-button (click)="flipped = !flipped"><mat-icon>swap_vert</mat-icon></button>
              </div>
              <app-position-repertoires class="pr-slot" [fen]="service.currentFen" />
            </div>
            <div class="moves-section">
              @if (service.currentGame; as g) {
                <app-move-list [moves]="g.moves" [currentMoveIndex]="service.currentMoveIndex" [comments]="g.comments" (moveClicked)="service.goToMove($event)" />
              }
            </div>
          </div>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .shared-page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    /* Die Karte umschließt ihren Inhalt (Brett + Zugliste) und steht mittig, statt sich auf die Seitenbreite zu
       dehnen und rechts von der Zugliste leer zu bleiben. Das Brett wächst mit dem Fenster: so hoch, dass
       Kopfzeile und Steuerleiste noch Platz haben, und so breit, dass die Zugliste daneben passt — aber nie
       über 640 px (darüber wird es ein Poster) und nie unter 360 px (gemeldet 2026-09-23: 400 px auf einem
       2250 px breiten Bildschirm, zwei Drittel der Seite leer). */
    .viewer {
      --board-size: clamp(360px, min(calc(100vh - 300px), calc(100vw - 440px)), 640px);
      width: fit-content; max-width: 100%; margin: 0 auto; padding: 16px 20px 20px; box-sizing: border-box;
    }
    .header { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px 16px; flex-wrap: wrap; margin-bottom: 12px; }
    .header-main { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .players { font-size: 1.05rem; }
    .players .elo { font-weight: 400; font-size: 0.85em; color: color-mix(in srgb, currentColor 60%, transparent); }
    .meta { display: flex; gap: 10px; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .result { color: #1976d2; font-weight: 600; }
    .original { flex: 0 0 auto; white-space: nowrap; }
    .body { display: flex; gap: 20px; align-items: flex-start; }
    .board-section { width: var(--board-size); display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: var(--board-size); }
    .board-wrap app-chess-board { display: block; width: var(--board-size); }
    .board-tap {
      display: none;
      position: absolute;
      top: 0; bottom: 0;
      width: 40%;
      z-index: 10;
      cursor: pointer;
    }
    .board-tap-prev { left: 0; }
    .board-tap-next { right: 0; }
    .nav { display: flex; gap: 4px; }
    .pr-slot { display: block; width: 100%; }
    /* Die Zugliste ist so hoch wie das Brett und scrollt in sich; eine feste Breite, damit die zwei Zugspalten
       nebeneinander stehen statt — bei einer Spalte, die den Rest der Karte füllt — mit einer Handbreit Luft
       dazwischen. */
    .moves-section {
      width: 300px; height: var(--board-size); flex-shrink: 0; box-sizing: border-box;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; overflow: auto;
    }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { width: auto; padding: 0; border-radius: 0; }
      .header { flex-direction: column; align-items: stretch; padding: 12px 16px; }
      .original { align-self: stretch; }
      .body { flex-direction: column; align-items: stretch; }
      .board-section { width: 100%; max-width: 100%; align-items: center; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      .board-tap { display: block; }
      .nav { justify-content: center; padding: 4px 0; }
      .moves-section {
        width: 100%; height: auto; max-height: 40vh;
        border-left: none; border-right: none; border-radius: 0; border-bottom: none;
      }
    }
  `]
})
export class SharedGameComponent implements OnInit {
  game: SharedGame | null = null;
  loading = true;
  notFound = false;
  flipped = false;

  constructor(
    public service: PgnViewerService,
    private route: ActivatedRoute,
    private games: GamesService,
    public preferences: PreferencesService,
  ) {}

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.games.getShared(token).subscribe({
      next: g => {
        this.game = g;
        // Aus der Sicht des Teilenden: spielte er Schwarz, startet das Brett gedreht (Flip-Knopf bleibt).
        this.flipped = g.ownerSide === 'black';
        this.service.loadPgn(g.pgn);
        this.loading = false;
      },
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.service.goBack(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.service.goForward(); }
  }
}
