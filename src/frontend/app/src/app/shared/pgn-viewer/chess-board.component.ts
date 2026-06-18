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
  template: `<div #boardEl></div>`,
  styles: [`
    :host { display: block; width: 100%; }
  `]
})
export class ChessBoardComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  @Input() lastMove?: [string, string];

  @ViewChild('boardEl') boardEl!: ElementRef<HTMLElement>;

  private ground?: Api;
  private resizeObserver?: ResizeObserver;
  private initAttempts = 0;
  private rafId?: number;
  private destroyed = false;

  ngAfterViewInit(): void {
    this.initBoard();
  }

  private initBoard(): void {
    // Während der Breite-0-Retries zerstört? Dann kein Chessground/ResizeObserver mehr
    // auf einem abgekoppelten Element aufbauen (würde sonst nie disconnected).
    if (this.destroyed) return;
    const el = this.boardEl.nativeElement;
    const hostWidth = (this.boardEl.nativeElement.parentElement as HTMLElement)?.clientWidth
      || el.clientWidth;

    if (hostWidth === 0 && this.initAttempts < 10) {
      this.initAttempts++;
      this.rafId = requestAnimationFrame(() => this.initBoard());
      return;
    }

    const size = hostWidth || 400;
    el.style.width = `${size}px`;
    el.style.height = `${size}px`;

    this.ground = Chessground(el, {
      fen: this.fen,
      viewOnly: true,
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 200 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
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
    if (changes['fen'] || changes['lastMove']) {
      this.ground.set({
        fen: this.fen,
        lastMove: this.lastMove as Key[] | undefined,
      });
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.rafId !== undefined) cancelAnimationFrame(this.rafId);
    this.resizeObserver?.disconnect();
    this.ground?.destroy();
  }
}
