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
  template: `<div #boardEl [class]="'cg-square board-theme-' + boardTheme + ' piece-set-' + pieceSet"></div>`,
  styles: [`
    :host { display: block; width: 100%; }
    /* Verschließ-Sicher: der Boden-Div bleibt immer quadratisch, egal welche width
       chess-board.component per JS setzt oder ob die JS-Größenberechnung verpasst wird.
       Ohne aspect-ratio zieht Chessground die Squares horizontal, das Brett wirkt
       gequetscht (siehe Games-Dialog-Bug 0.244.x). */
    .cg-square { aspect-ratio: 1 / 1; }
  `]
})
export class ChessBoardComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  @Input() lastMove?: [string, string];
  @Input() flipped = false;
  /** Brett-Theme aus den User-Preferences (styles.scss `.board-theme-*` cg-board Regeln). */
  @Input() boardTheme = 'brown';
  /** Figurenset aus den User-Preferences (styles.scss `.piece-set-*` cg-board piece Regeln). */
  @Input() pieceSet = 'cburnett';

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

    // WICHTIG: NICHT mit viewOnly:true initialisieren — Chessground bindet die
    // Maus-/Touch-Listener (inkl. Rechtsklick-Zeichnen) nur beim Init und
    // überspringt sie bei viewOnly=true (bindBoard: `if (s.viewOnly) return;`).
    // Damit man auf diesem reinen Anzeige-Brett trotzdem Pfeile/Kreise per
    // Rechtsklick ziehen kann, initialisieren wir interaktiv, schalten aber
    // jegliche Figuren-Interaktion (Ziehen/Auswählen/Zug) aus.
    this.ground = Chessground(el, {
      fen: this.fen,
      viewOnly: false,
      orientation: this.flipped ? 'black' : 'white',
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 200 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
      movable: { free: false, color: undefined },
      draggable: { enabled: false },
      selectable: { enabled: false },
      // Pfeile/Kreise per Rechtsklick-Ziehen (wie im Analyse-/Puzzle-Brett).
      drawable: { enabled: true, visible: true },
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
    if (changes['fen'] || changes['lastMove'] || changes['flipped']) {
      this.ground.set({
        fen: this.fen,
        orientation: this.flipped ? 'black' : 'white',
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
