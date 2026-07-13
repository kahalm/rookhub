import { Component, OnInit, HostListener, ChangeDetectionStrategy } from '@angular/core';
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
import { RepertoireService, SharedLine } from '../../core/repertoire.service';

/**
 * Öffentliche Nur-Ansehen-Seite einer geteilten Repertoire-Linie (Route <c>/l/:token</c>, kein
 * Login nötig). Spiegelt die geteilte-Partie-Seite (<c>/g/:token</c>): reused den PgnViewerService
 * + chess-board/move-list.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-shared-line',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatCardModule, MatProgressSpinnerModule,
    TranslatePipe, ChessBoardComponent, MoveListComponent,
  ],
  providers: [PgnViewerService],
  template: `
    <div class="shared-page">
      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound) {
        <mat-card class="empty">
          <mat-icon>link_off</mat-icon>
          <p>{{ 'repertoire.shareLine.notFound' | translate }}</p>
        </mat-card>
      } @else if (line) {
        <mat-card class="viewer">
          <div class="header">
            <span class="title">{{ line.title || ('repertoire.shareLine.defaultTitle' | translate) }}</span>
            <span class="meta">
              @if (line.repertoireName) { <span class="rep">{{ line.repertoireName }}</span> }
              <span class="date">{{ line.createdAt | date:'mediumDate' }}</span>
            </span>
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
    .shared-page { max-width: 900px; margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .viewer { padding: 16px; }
    .header { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
    .title { font-weight: 600; font-size: 1.1rem; }
    .meta { display: flex; gap: 10px; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .rep { font-weight: 500; }
    .body { display: flex; gap: 16px; align-items: flex-start; }
    .board-section { width: 400px; display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { position: relative; width: 400px; }
    .board-wrap app-chess-board { display: block; width: 400px; }
    .board-tap { display: none; position: absolute; top: 0; bottom: 0; width: 40%; z-index: 10; cursor: pointer; }
    .board-tap-prev { left: 0; }
    .board-tap-next { right: 0; }
    .nav { display: flex; gap: 4px; }
    .moves-section { flex: 1; border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; min-width: 180px; overflow: auto; max-height: 60vh; }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { padding: 0; border-radius: 0; }
      .header { padding: 12px 16px; }
      .body { flex-direction: column; align-items: stretch; }
      .board-section { width: 100%; max-width: 100%; align-items: center; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      .board-tap { display: block; }
      .nav { justify-content: center; padding: 4px 0; }
      .moves-section { width: 100%; max-height: 40vh; border-left: none; border-right: none; border-radius: 0; border-bottom: none; }
    }
  `]
})
export class SharedLineComponent implements OnInit {
  line: SharedLine | null = null;
  loading = true;
  notFound = false;
  flipped = false;

  constructor(
    public service: PgnViewerService,
    private route: ActivatedRoute,
    private repertoires: RepertoireService,
    public preferences: PreferencesService,
  ) {}

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.repertoires.getSharedLine(token).subscribe({
      next: l => { this.line = l; this.service.loadPgn(l.pgn); this.loading = false; },
      error: () => { this.notFound = true; this.loading = false; },
    });
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.service.goBack(); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.service.goForward(); }
  }
}
