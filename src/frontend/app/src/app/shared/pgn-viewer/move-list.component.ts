import {
  Component, Input, Output, EventEmitter, OnChanges,
  SimpleChanges, ElementRef, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Move } from 'chess.js';

@Component({
  selector: 'app-move-list',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="move-list" #moveListEl>
      @for (pair of movePairs; track $index) {
        <div class="move-row" [class.row-active]="isRowActive(pair)">
          <span class="move-number">{{ pair.number }}.</span>
          @if (pair.white !== undefined) {
            <span class="move" [class.active]="pair.whiteIndex === currentMoveIndex"
                  (click)="moveClicked.emit(pair.whiteIndex)">{{ pair.white }}</span>
          } @else {
            <span class="move-empty"></span>
          }
          @if (pair.black) {
            <span class="move" [class.active]="pair.blackIndex === currentMoveIndex"
                  (click)="moveClicked.emit(pair.blackIndex!)">{{ pair.black }}</span>
          } @else {
            <span class="move-empty"></span>
          }
        </div>
        @if ((pair.whiteIndex >= 0 && comments[pair.whiteIndex]) ||
             (pair.blackIndex !== undefined && comments[pair.blackIndex!])) {
          <div class="comment-row">
            {{ comments[pair.whiteIndex] || comments[pair.blackIndex!] }}
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .move-list {
      font-family: 'Roboto Mono', monospace;
      font-size: 13px;
      overflow-y: auto;
      height: 100%;
      padding: 4px 0;
    }
    .move-row {
      display: grid;
      grid-template-columns: 32px 1fr 1fr;
      align-items: center;
      padding: 1px 6px;
      border-radius: 3px;
    }
    .move-row.row-active {
      background: color-mix(in srgb, currentColor 6%, transparent);
    }
    .move-number {
      color: color-mix(in srgb, currentColor 45%, transparent);
      font-size: 11px;
      user-select: none;
    }
    .move {
      cursor: pointer;
      padding: 3px 6px;
      border-radius: 3px;
      line-height: 1.6;
    }
    .move:hover { background: color-mix(in srgb, currentColor 12%, transparent); }
    .move.active { background: #1976d2; color: white; }
    .move-empty { display: block; }
    .comment-row {
      padding: 2px 6px 6px 38px;
      color: color-mix(in srgb, currentColor 60%, transparent);
      font-style: italic;
      font-size: 12px;
      font-family: 'Roboto', sans-serif;
      line-height: 1.5;
    }
  `]
})
export class MoveListComponent implements OnChanges {
  @Input() moves: Move[] = [];
  @Input() currentMoveIndex = -1;
  @Input() comments: { [moveIndex: number]: string } = {};
  @Output() moveClicked = new EventEmitter<number>();

  @ViewChild('moveListEl') moveListEl!: ElementRef<HTMLElement>;

  movePairs: { number: number; white?: string; whiteIndex: number; black?: string; blackIndex?: number }[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['moves']) {
      this.buildPairs();
    }
    if (changes['currentMoveIndex']) {
      this.scrollToActive();
    }
  }

  isRowActive(pair: { whiteIndex: number; blackIndex?: number }): boolean {
    return pair.whiteIndex === this.currentMoveIndex ||
      (pair.blackIndex !== undefined && pair.blackIndex === this.currentMoveIndex);
  }

  private buildPairs(): void {
    this.movePairs = [];
    let i = 0;
    while (i < this.moves.length) {
      const m = this.moves[i];
      const num = this.fullMoveNumber(m, i);
      if (m.color === 'b') {
        this.movePairs.push({ number: num, white: undefined, whiteIndex: -1, black: m.san, blackIndex: i });
        i += 1;
      } else {
        const black = this.moves[i + 1];
        const hasBlack = !!black && black.color === 'b';
        this.movePairs.push({
          number: num,
          white: m.san,
          whiteIndex: i,
          black: hasBlack ? black!.san : undefined,
          blackIndex: hasBlack ? i + 1 : undefined,
        });
        i += hasBlack ? 2 : 1;
      }
    }
  }

  private fullMoveNumber(m: Move, fallbackIndex: number): number {
    const before = (m as unknown as { before?: string }).before;
    const n = before ? parseInt(before.split(' ')[5], 10) : NaN;
    return Number.isFinite(n) ? n : Math.floor(fallbackIndex / 2) + 1;
  }

  private scrollToActive(): void {
    setTimeout(() => {
      const el = this.moveListEl?.nativeElement;
      if (!el) return;
      const active = el.querySelector('.move.active') as HTMLElement;
      if (active) {
        active.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
    });
  }
}
