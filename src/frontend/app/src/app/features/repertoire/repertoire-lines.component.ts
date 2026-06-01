import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { TranslateModule } from '@ngx-translate/core';
import { Move } from 'chess.js';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { RepertoireLine } from './repertoire-viewer.service';

@Component({
  selector: 'app-repertoire-lines',
  standalone: true,
  imports: [CommonModule, MatListModule, MatIconModule, MatButtonModule, TranslateModule, MoveListComponent],
  template: `
    @if (selectedIndex >= 0) {
      <div class="move-view">
        <button mat-button (click)="lineDeselected.emit()" class="back-btn">
          <mat-icon>arrow_back</mat-icon> {{ 'repertoire.lines.allLines' | translate }}
        </button>
        <div class="line-header">
          <span class="players">{{ lines[selectedIndex].white }} vs {{ lines[selectedIndex].black }}</span>
          <span class="result">{{ lines[selectedIndex].result }}</span>
        </div>
        <div class="move-list-wrap">
          <app-move-list
            [moves]="moves"
            [currentMoveIndex]="currentMoveIndex"
            [comments]="comments"
            (moveClicked)="moveClicked.emit($event)" />
        </div>
      </div>
    } @else {
      <div class="lines-list">
        @for (line of lines; track line.gameIndex; let i = $index) {
          <div class="line-item" (click)="lineSelected.emit(i)">
            <div class="line-players">
              <span>{{ line.white }} vs {{ line.black }}</span>
              <span class="line-result">{{ line.result }}</span>
            </div>
            @if (line.opening) {
              <div class="line-opening">{{ line.opening }}</div>
            }
            <div class="line-summary">{{ line.summary }}</div>
          </div>
        } @empty {
          <div class="empty">{{ 'repertoire.lines.empty' | translate }}</div>
        }
      </div>
    }
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .lines-list { overflow-y: auto; height: 100%; }
    .line-item {
      padding: 12px 16px;
      border-bottom: 1px solid #e0e0e0;
      cursor: pointer;
      transition: background 0.15s;
    }
    .line-item:hover { background: #f5f5f5; }
    .line-players {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-weight: 500;
      font-size: 14px;
    }
    .line-result { color: #1976d2; font-weight: 600; font-size: 13px; }
    .line-opening { color: #666; font-size: 12px; margin-top: 2px; }
    .line-summary {
      font-family: 'Roboto Mono', monospace;
      font-size: 12px;
      color: #888;
      margin-top: 4px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .move-view { display: flex; flex-direction: column; height: 100%; }
    .back-btn { align-self: flex-start; margin: 4px; }
    .line-header {
      display: flex;
      justify-content: space-between;
      padding: 4px 16px 8px;
      font-size: 14px;
      border-bottom: 1px solid #e0e0e0;
    }
    .players { font-weight: 500; }
    .result { color: #1976d2; font-weight: 600; }
    .move-list-wrap { flex: 1; overflow: hidden; }
    .empty { padding: 2rem; text-align: center; color: #888; }
  `]
})
export class RepertoireLinesComponent {
  @Input() lines: RepertoireLine[] = [];
  @Input() selectedIndex = -1;
  @Input() moves: Move[] = [];
  @Input() currentMoveIndex = -1;
  @Input() comments: { [moveIndex: number]: string } = {};

  @Output() lineSelected = new EventEmitter<number>();
  @Output() lineDeselected = new EventEmitter<void>();
  @Output() moveClicked = new EventEmitter<number>();
}
