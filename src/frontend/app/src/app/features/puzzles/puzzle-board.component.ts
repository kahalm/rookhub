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
  template: `<div #boardEl class="cg-wrap"></div>`,
  styles: [`
    :host { display: block; width: 100%; }
    .cg-wrap {
      position: relative;
      display: block;
      overflow: hidden;
    }
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
  @Input() premovable = false;

  @Output() moveMade = new EventEmitter<{ orig: Key; dest: Key }>();

  @ViewChild('boardEl') boardEl!: ElementRef<HTMLElement>;

  private ground?: Api;
  private resizeObserver?: ResizeObserver;
  private initAttempts = 0;
  private pendingPremove?: { orig: Key; dest: Key };

  ngAfterViewInit(): void {
    this.ensureChessgroundCss();
    this.initBoard();
  }

  /**
   * Inject critical chessground CSS rules into <head> as a <style> element.
   * Angular loads the global stylesheet with media="print" (deferred), so
   * chessground base CSS (display:block, position:absolute on custom elements)
   * is NOT available when Chessground first reads getBoundingClientRect().
   * This ensures the rules are immediately available.
   */
  private ensureChessgroundCss(): void {
    if (document.getElementById('cg-critical-css')) return;
    const style = document.createElement('style');
    style.id = 'cg-critical-css';
    style.textContent = `
      .cg-wrap { position: relative; display: block; }
      cg-container { position: absolute; width: 100%; height: 100%; display: block; top: 0; }
      cg-board { position: absolute; top: 0; left: 0; width: 100%; height: 100%; display: block; }
      cg-board square { position: absolute; top: 0; left: 0; width: 12.5%; height: 12.5%; }
      cg-board piece { position: absolute; top: 0; left: 0; width: 12.5%; height: 12.5%; z-index: 2; will-change: transform; }
    `;
    document.head.appendChild(style);
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

    // IMPORTANT: Never init with viewOnly:true - Chessground only binds
    // mouse/touch event listeners during init, and skips them when viewOnly=true.
    // Later toggling viewOnly via .set() does NOT re-bind the listeners.
    // Instead, control interactivity via movable.color/dests.
    this.ground = Chessground(el, {
      fen: this.fen,
      orientation: this.orientation,
      turnColor: this.turnColor,
      viewOnly: false,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 200 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
      movable: {
        free: false,
        color: this.viewOnly ? undefined : (this.premovable ? this.orientation : this.turnColor),
        dests: this.viewOnly ? undefined : this.dests,
        showDests: true
      },
      premovable: {
        enabled: this.premovable,
        showDests: true
      },
      events: {
        move: (orig: Key, dest: Key) => {
          this.moveMade.emit({ orig, dest });
        }
      }
    });

    // After init, force a redraw to ensure correct dimensions
    requestAnimationFrame(() => this.ground?.redrawAll());

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

    // Check for pending premove before updating the board
    // When transitioning from premovable to interactive (user's turn), execute premove
    const wasPremovable = changes['premovable']?.previousValue;
    const isNowInteractive = !this.viewOnly && !this.premovable;
    const hasPremove = this.ground.state.premovable.current;

    this.ground.set({
      fen: this.fen,
      orientation: this.orientation,
      turnColor: this.turnColor,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      movable: {
        free: false,
        color: this.viewOnly ? undefined : (this.premovable ? this.orientation : this.turnColor),
        dests: this.viewOnly ? undefined : this.dests,
        showDests: true
      },
      premovable: {
        enabled: this.premovable
      }
    });

    // Execute pending premove when transitioning from THINKING to PLAYING
    if (wasPremovable && isNowInteractive && hasPremove) {
      const [orig, dest] = hasPremove;
      // Cancel the premove visual, then emit the move
      this.ground.cancelPremove();
      setTimeout(() => {
        this.moveMade.emit({ orig, dest });
      }, 50);
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.ground?.destroy();
  }
}
