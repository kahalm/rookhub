import {
  Component, ElementRef, Input, OnChanges, OnDestroy,
  AfterViewInit, SimpleChanges, ViewChild
} from '@angular/core';
import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { Key } from 'chessground/types';

@Component({
  selector: 'app-chess-board',
  standalone: true,
  template: `<div #boardEl class="board-wrap"></div>`,
  styles: [`
    :host { display: block; width: 100%; max-width: 560px; }
    .board-wrap { aspect-ratio: 1; width: 100%; }
    .board-wrap ::ng-deep .cg-wrap { width: 100%; height: 100%; }
  `]
})
export class ChessBoardComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  @Input() lastMove?: [string, string];

  @ViewChild('boardEl') boardEl!: ElementRef<HTMLElement>;

  private ground?: Api;
  private resizeObserver?: ResizeObserver;

  ngAfterViewInit(): void {
    this.ground = Chessground(this.boardEl.nativeElement, {
      fen: this.fen,
      viewOnly: true,
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 200 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
    });

    this.resizeObserver = new ResizeObserver(() => {
      this.ground?.redrawAll();
    });
    this.resizeObserver.observe(this.boardEl.nativeElement);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.ground) return;
    if (changes['fen'] || changes['lastMove']) {
      this.ground.set({
        fen: this.fen,
        lastMove: this.lastMove as Key[] | undefined,
      });
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.ground?.destroy();
  }
}
