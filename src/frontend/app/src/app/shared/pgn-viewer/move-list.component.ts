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
      @for (pair of movePairs; track pair.number) {
        <span class="move-number">{{ pair.number }}.</span>
        <span class="move" [class.active]="pair.whiteIndex === currentMoveIndex"
              (click)="moveClicked.emit(pair.whiteIndex)">{{ pair.white }}</span>
        @if (pair.black) {
          <span class="move" [class.active]="pair.blackIndex === currentMoveIndex"
                (click)="moveClicked.emit(pair.blackIndex!)">{{ pair.black }}</span>
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
    .move-number { color: #888; min-width: 28px; }
    .move {
      cursor: pointer;
      padding: 1px 4px;
      border-radius: 3px;
    }
    .move:hover { background: #e0e0e0; }
    .move.active { background: #1976d2; color: white; }
  `]
})
export class MoveListComponent implements OnChanges {
  @Input() moves: Move[] = [];
  @Input() currentMoveIndex = -1;
  @Output() moveClicked = new EventEmitter<number>();

  @ViewChild('moveListEl') moveListEl!: ElementRef<HTMLElement>;

  movePairs: { number: number; white: string; whiteIndex: number; black?: string; blackIndex?: number }[] = [];

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
    for (let i = 0; i < this.moves.length; i += 2) {
      this.movePairs.push({
        number: Math.floor(i / 2) + 1,
        white: this.moves[i].san,
        whiteIndex: i,
        black: this.moves[i + 1]?.san,
        blackIndex: i + 1 < this.moves.length ? i + 1 : undefined,
      });
    }
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
