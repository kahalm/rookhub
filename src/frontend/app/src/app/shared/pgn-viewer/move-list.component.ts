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
        <span class="move-number">{{ pair.number }}.{{ pair.white === undefined ? '..' : '' }}</span>
        @if (pair.white !== undefined) {
          <span class="move" [class.active]="pair.whiteIndex === currentMoveIndex"
                (click)="moveClicked.emit(pair.whiteIndex)">{{ pair.white }}</span>
          @if (comments[pair.whiteIndex]) {
            <span class="comment">{{ comments[pair.whiteIndex] }}</span>
          }
        }
        @if (pair.black) {
          <span class="move" [class.active]="pair.blackIndex === currentMoveIndex"
                (click)="moveClicked.emit(pair.blackIndex!)">{{ pair.black }}</span>
        }
        @if (pair.blackIndex !== undefined && comments[pair.blackIndex!]) {
          <span class="comment">{{ comments[pair.blackIndex!] }}</span>
        }
      }
    </div>
  `,
  styles: [`
    .move-list {
      font-family: 'Roboto Mono', monospace;
      font-size: 14px;
      line-height: 1.8;
      padding: 8px;
      overflow-y: auto;
      height: 100%;
      display: flex;
      flex-wrap: wrap;
      align-content: flex-start;
      gap: 2px 4px;
    }
    .move-number { color: color-mix(in srgb, currentColor 47%, transparent); min-width: 28px; }
    .move {
      cursor: pointer;
      padding: 1px 4px;
      border-radius: 3px;
    }
    .move:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .move.active { background: #1976d2; color: white; }
    .comment {
      width: 100%;
      color: color-mix(in srgb, currentColor 60%, transparent);
      font-style: italic;
      font-size: 12px;
      font-family: 'Roboto', sans-serif;
      line-height: 1.5;
      padding: 2px 4px 6px;
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

  private buildPairs(): void {
    this.movePairs = [];
    let i = 0;
    while (i < this.moves.length) {
      const m = this.moves[i];
      const num = this.fullMoveNumber(m, i);
      if (m.color === 'b') {
        // Segment beginnt mit Schwarz am Zug (FEN mit "b ...") -> "N... <schwarz>"
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

  /** Vollzugnummer aus der FEN vor dem Zug (chess.js verbose Move hat `before`). */
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
