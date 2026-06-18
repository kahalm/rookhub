import { Component, HostListener, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { TranslateModule } from '@ngx-translate/core';
import { ChessBoardComponent } from './chess-board.component';
import { MoveListComponent } from './move-list.component';
import { PgnViewerService } from './pgn-viewer.service';

export interface PgnViewerData {
  pgn: string;
  fileName?: string;
}

@Component({
  selector: 'app-pgn-viewer',
  standalone: true,
  imports: [
    CommonModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatFormFieldModule, TranslateModule, ChessBoardComponent, MoveListComponent,
  ],
  providers: [PgnViewerService],
  template: `
    <div class="viewer-container">
      <div class="viewer-header">
        <div class="header-info">
          @if (service.games.length > 1) {
            <mat-form-field appearance="outline" class="game-select">
              <mat-label>{{ 'pgnViewer.gameLabel' | translate }}</mat-label>
              <mat-select [value]="service.currentGameIndex" (selectionChange)="service.selectGame($event.value)"
                          [attr.aria-label]="'pgnViewer.gameLabel' | translate">
                @for (game of service.games; track $index) {
                  <mat-option [value]="$index">
                    {{ game.headers['White'] || '?' }} vs {{ game.headers['Black'] || '?' }}
                    @if (game.headers['Result']) { ({{ game.headers['Result'] }}) }
                  </mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
          @if (service.currentGame; as game) {
            <div class="game-info">
              <span class="players">
                <strong>{{ game.headers['White'] || '?' }}</strong>
                vs
                <strong>{{ game.headers['Black'] || '?' }}</strong>
              </span>
              @if (game.headers['Result']) {
                <span class="result">{{ game.headers['Result'] }}</span>
              }
              @if (game.headers['Event']) {
                <span class="event">{{ game.headers['Event'] }}</span>
              }
              @if (game.headers['Date']) {
                <span class="date">{{ game.headers['Date'] }}</span>
              }
            </div>
          }
        </div>
        <button mat-icon-button (click)="dialogRef.close()"
                [attr.aria-label]="'common.close' | translate">
          <mat-icon>close</mat-icon>
        </button>
      </div>

      <div class="viewer-body">
        <div class="board-section">
          <app-chess-board [fen]="service.currentFen" [lastMove]="service.lastMove" />
          <div class="nav-buttons">
            <button mat-icon-button (click)="service.goToStart()" [disabled]="service.currentMoveIndex < 0"
                    [attr.aria-label]="'pgnViewer.nav.first' | translate"
                    [attr.title]="'pgnViewer.nav.first' | translate">
              <mat-icon>skip_previous</mat-icon>
            </button>
            <button mat-icon-button (click)="service.goBack()" [disabled]="service.currentMoveIndex < 0"
                    [attr.aria-label]="'pgnViewer.nav.previous' | translate"
                    [attr.title]="'pgnViewer.nav.previous' | translate">
              <mat-icon>navigate_before</mat-icon>
            </button>
            <button mat-icon-button (click)="service.goForward()"
                    [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"
                    [attr.aria-label]="'pgnViewer.nav.next' | translate"
                    [attr.title]="'pgnViewer.nav.next' | translate">
              <mat-icon>navigate_next</mat-icon>
            </button>
            <button mat-icon-button (click)="service.goToEnd()"
                    [disabled]="!service.currentGame || service.currentMoveIndex >= service.currentGame.moves.length - 1"
                    [attr.aria-label]="'pgnViewer.nav.last' | translate"
                    [attr.title]="'pgnViewer.nav.last' | translate">
              <mat-icon>skip_next</mat-icon>
            </button>
          </div>
        </div>
        <div class="moves-section">
          @if (service.currentGame; as game) {
            <app-move-list
              [moves]="game.moves"
              [currentMoveIndex]="service.currentMoveIndex"
              [comments]="game.comments"
              (moveClicked)="service.goToMove($event)" />
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    .viewer-container {
      display: flex;
      flex-direction: column;
      height: 90vh;
      max-height: 90vh;
      overflow: hidden;
    }
    .viewer-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 16px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent);
      flex-shrink: 0;
    }
    .header-info { display: flex; flex-direction: column; gap: 4px; flex: 1; min-width: 0; }
    .game-select { width: 100%; max-width: 400px; }
    .game-select ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .game-info {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
      font-size: 14px;
    }
    .result { color: #1976d2; font-weight: 500; }
    .event, .date { color: color-mix(in srgb, currentColor 60%, transparent); }

    .viewer-body {
      display: flex;
      flex: 1;
      overflow: hidden;
      padding: 16px;
      gap: 16px;
    }
    /* Board-Maße wie der Repertoire-Linien-Look (repertoire-detail): fixe
       400px-Spalte, sonst kollabiert sie in der Flex-Zeile auf Nav-Breite. */
    .board-section {
      width: 400px;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      flex-shrink: 0;
    }
    .board-section app-chess-board { display: block; width: 400px; }
    .nav-buttons { display: flex; gap: 4px; }
    .moves-section {
      flex: 1;
      overflow: hidden;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent);
      border-radius: 4px;
      min-width: 180px;
    }

    @media (max-width: 768px) {
      .viewer-body {
        flex-direction: column;
        overflow-y: auto;
        align-items: center;
      }
      .board-section { width: 100%; max-width: 400px; }
      .board-section app-chess-board { width: 100%; }
      .moves-section { min-height: 200px; flex-shrink: 0; width: 100%; }
    }
  `]
})
export class PgnViewerComponent implements OnInit {
  constructor(
    public service: PgnViewerService,
    public dialogRef: MatDialogRef<PgnViewerComponent>,
    @Inject(MAT_DIALOG_DATA) private data: PgnViewerData,
  ) {}

  ngOnInit(): void {
    this.service.loadPgn(this.data.pgn);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault();
        this.service.goBack();
        break;
      case 'ArrowRight':
        event.preventDefault();
        this.service.goForward();
        break;
      case 'Home':
        event.preventDefault();
        this.service.goToStart();
        break;
      case 'End':
        event.preventDefault();
        this.service.goToEnd();
        break;
    }
  }
}
