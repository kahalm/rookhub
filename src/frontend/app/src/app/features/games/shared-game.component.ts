import { Component, OnInit, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { PgnViewerService } from '../../shared/pgn-viewer/pgn-viewer.service';
import { GamesService, SharedGame } from './games.service';

/**
 * Öffentliche Nachspiel-Seite einer geteilten Partie (Route <c>/g/:token</c>, kein Login nötig).
 * Reused den PgnViewerService + chess-board/move-list aus dem PGN-Viewer, aber inline statt im Dialog.
 */
@Component({
  selector: 'app-shared-game',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule,
    TranslateModule, ChessBoardComponent, MoveListComponent,
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
            <span class="players"><strong>{{ game.white || '?' }}</strong> – <strong>{{ game.black || '?' }}</strong></span>
            <span class="meta">
              @if (game.result && game.result !== '*') { <span class="result">{{ game.result }}</span> }
              <span>{{ game.source }}</span>
              <span class="date">{{ (game.playedAt || game.createdAt) | date:'mediumDate' }}</span>
            </span>
          </div>
          <div class="body">
            <div class="board-section">
              <div class="board-wrap">
                <app-chess-board [fen]="service.currentFen" [lastMove]="service.lastMove" [flipped]="flipped" />
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
            </div>
            <div class="moves-section">
              @if (service.currentGame; as g) {
                <app-move-list [moves]="g.moves" [currentMoveIndex]="service.currentMoveIndex" [comments]="g.comments" (moveClicked)="service.goToMove($event)" />
              }
            </div>
          </div>
          @if (game.sourceUrl) {
            <a mat-stroked-button [href]="game.sourceUrl" target="_blank" rel="noopener" class="original">
              <mat-icon>open_in_new</mat-icon> {{ 'games.openOriginal' | translate }}
            </a>
          }
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .shared-page { max-width: 900px; margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .viewer { padding: 16px; }
    .header { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
    .meta { display: flex; gap: 10px; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .result { color: #1976d2; font-weight: 600; }
    .body { display: flex; gap: 16px; align-items: flex-start; }
    /* Board-Maße wie der Repertoire-Linien-Look (fixe 400px-Spalte). */
    .board-section { width: 400px; display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: 400px; }
    .board-wrap app-chess-board { display: block; width: 400px; }
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
    .moves-section { flex: 1; border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; min-width: 180px; overflow: auto; max-height: 60vh; }
    .original { margin-top: 12px; }
    @media (max-width: 768px) {
      .body { flex-direction: column; align-items: center; }
      .board-section { width: 100%; max-width: 400px; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      .board-tap { display: block; }
      .moves-section { width: 100%; max-height: 40vh; }
    }
  `]
})
export class SharedGameComponent implements OnInit {
  game: SharedGame | null = null;
  loading = true;
  notFound = false;
  flipped = false;

  constructor(public service: PgnViewerService, private route: ActivatedRoute, private games: GamesService) {}

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.games.getShared(token).subscribe({
      next: g => { this.game = g; this.service.loadPgn(g.pgn); this.loading = false; },
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.service.goBack(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.service.goForward(); }
  }
}
