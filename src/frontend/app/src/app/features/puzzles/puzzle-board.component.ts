import {
  Component, ElementRef, EventEmitter, Input, OnChanges,
  OnDestroy, AfterViewInit, Output, SimpleChanges, ViewChild
} from '@angular/core';
import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { Color, Key } from 'chessground/types';

@Component({
  selector: 'app-puzzle-board',
  standalone: true,
  template: `<div #boardEl></div>`,
  styles: [`
    :host { display: block; width: 100%; }
  `]
})
export class PuzzleBoardComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  @Input() orientation: Color = 'white';
  @Input() turnColor: Color = 'white';
  @Input() dests: Map<Key, Key[]> = new Map();
  @Input() lastMove?: [Key, Key];
  @Input() viewOnly = false;
  @Input() check = false;

  @Output() moveMade = new EventEmitter<{ orig: Key; dest: Key }>();

  @ViewChild('boardEl') boardEl!: ElementRef<HTMLElement>;

  private ground?: Api;
  private resizeObserver?: ResizeObserver;
  private initAttempts = 0;

  ngAfterViewInit(): void {
    this.initBoard();
  }

  private initBoard(): void {
    const el = this.boardEl.nativeElement;
    const hostWidth = (el.parentElement as HTMLElement)?.clientWidth || el.clientWidth;

    if (hostWidth === 0 && this.initAttempts < 10) {
      this.initAttempts++;
      requestAnimationFrame(() => this.initBoard());
      return;
    }

    const size = hostWidth || 400;
    el.style.width = `${size}px`;
    el.style.height = `${size}px`;

    this.ground = Chessground(el, {
      fen: this.fen,
      orientation: this.orientation,
      turnColor: this.turnColor,
      viewOnly: this.viewOnly,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 200 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
      movable: {
        free: false,
        color: this.viewOnly ? undefined : this.turnColor,
        dests: this.dests,
        showDests: true
      },
      events: {
        move: (orig: Key, dest: Key) => {
          this.moveMade.emit({ orig, dest });
        }
      }
    });

    this.resizeObserver = new ResizeObserver(() => {
      const hostEl = el.parentElement as HTMLElement;
      const w = hostEl?.clientWidth || el.clientWidth;
      if (w > 0 && w !== el.clientWidth) {
        el.style.width = `${w}px`;
        el.style.height = `${w}px`;
      }
      this.ground?.redrawAll();
    });
    this.resizeObserver.observe(el.parentElement || el);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.ground) return;
    this.ground.set({
      fen: this.fen,
      orientation: this.orientation,
      turnColor: this.turnColor,
      viewOnly: this.viewOnly,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      movable: {
        free: false,
        color: this.viewOnly ? undefined : this.turnColor,
        dests: this.dests,
        showDests: true
      }
    });
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.ground?.destroy();
  }
}
