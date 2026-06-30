import {
  Component, ElementRef, EventEmitter, HostListener, Input, OnChanges,
  OnDestroy, AfterViewInit, Output, SimpleChanges, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { Color, Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { Chess, Square } from 'chess.js';

type PromotionPiece = 'q' | 'r' | 'b' | 'n';

@Component({
  selector: 'app-puzzle-board',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="board-wrapper" [class]="'board-theme-' + boardTheme + ' piece-set-' + pieceSet">
      <div #boardEl class="cg-wrap"></div>
      @if (vizSelectedSquare) {
        <!-- DOM-Overlay als verlässliche Viz-Auswahlmarkierung (unabhängig von chessground-SVG). -->
        <div class="viz-select-overlay"
             [style.left.%]="vizSelectOverlayLeft"
             [style.top.%]="vizSelectOverlayTop"></div>
      }
      @if (showPromotionOverlay) {
        <div class="promotion-backdrop" (click)="cancelPromotion()"></div>
        <div class="promotion-choices" [style.left.%]="promotionFilePercent"
             [class.from-bottom]="promotionFromBottom">
          @for (piece of promotionPieces; track piece) {
            <div class="promotion-piece" (click)="selectPromotion(piece)">
              <div class="piece-icon" [style.backgroundImage]="getPieceImage(piece)"></div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .board-wrapper { position: relative; }
    .cg-wrap {
      position: relative;
      display: block;
      overflow: hidden;
    }
    .promotion-backdrop {
      position: absolute; inset: 0; z-index: 100;
      background: rgba(0,0,0,0.35);
    }
    .promotion-choices {
      position: absolute; z-index: 101;
      top: 0; width: 12.5%;
      display: flex; flex-direction: column;
    }
    .promotion-choices.from-bottom {
      top: auto; bottom: 0;
      flex-direction: column-reverse;
    }
    .promotion-piece {
      width: 100%; aspect-ratio: 1;
      background: rgba(255,255,255,0.9);
      cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      transition: background 0.1s;
    }
    .promotion-piece:first-child { border-radius: 4px 4px 0 0; }
    .promotion-piece:last-child { border-radius: 0 0 4px 4px; }
    .promotion-choices.from-bottom .promotion-piece:first-child { border-radius: 0 0 4px 4px; }
    .promotion-choices.from-bottom .promotion-piece:last-child { border-radius: 4px 4px 0 0; }
    .promotion-piece:hover { background: rgba(200,220,255,0.95); }
    .piece-icon {
      width: 85%; height: 85%;
      background-size: contain;
      background-repeat: no-repeat;
      background-position: center;
    }
    .viz-select-overlay {
      position: absolute;
      width: 12.5%; height: 12.5%;
      box-sizing: border-box;
      border: 0.6vmin solid #15781b;
      /* Untere Grenze, falls vmin sehr klein wird (Mobile Hochkant) — mindestens 4px. */
      border-width: max(0.6vmin, 4px);
      border-radius: 50%;
      pointer-events: none;
      z-index: 50;
      box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.4) inset;
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
  @Input() boardTheme = 'brown';
  @Input() pieceSet = 'cburnett';
  /** Visualisierungs-Level (0 = aus, >=1 = aktiv): Brett bleibt eingefroren, Klicks (Von→Nach)
   *  werden als Koordinaten erfasst und als moveMade emittiert (kein figurenbasiertes Ziehen). */
  @Input() visualization = 0;
  /** Tatsaechliche chess.js-FEN (im Viz-Modus weicht `fen` als frozen-Brett davon ab).
   *  Wird im Viz-Modus für Promotion-Erkennung und Legalitäts-Check beim 2. Klick genutzt. */
  @Input() actualFen?: string;
  /** Letzter Gegnerzug als Pfeil (nur im Viz-Modus 1-4 gesetzt; via setAutoShapes gerendert). */
  @Input() vizOpponentArrow?: [Key, Key];

  @Output() moveMade = new EventEmitter<{ orig: Key; dest: Key; promotion?: string }>();

  @ViewChild('boardEl') boardEl!: ElementRef<HTMLElement>;

  private ground?: Api;
  private resizeObserver?: ResizeObserver;
  private premoveTimer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private initAttempts = 0;
  private pendingPremove?: { orig: Key; dest: Key };
  private vizFrom?: Key;
  /** Aktuell im Viz-Modus angetapptes Feld — rendert den grünen Overlay-Ring oben drüber. */
  vizSelectedSquare: Key | null = null;
  // Geste-Tracking im Viz-Modus (Tap vs. Ziehen).
  private vizPointerStartKey?: Key;
  private vizPointerStartX = 0;
  private vizPointerStartY = 0;
  /** Mindest-Fingerweg (px), ab dem eine Geste als Ziehen statt Tap gilt. */
  private static readonly VIZ_DRAG_THRESHOLD_PX = 10;

  // Promotion overlay state
  showPromotionOverlay = false;
  private pendingPromotion: { orig: Key; dest: Key } | null = null;
  private promotionColor: 'w' | 'b' = 'w';
  promotionFilePercent = 0;
  promotionFromBottom = false;
  promotionPieces: PromotionPiece[] = ['q', 'r', 'b', 'n'];
  /** Zeitpunkt (ms), bis zu dem Klicks/Taps auf den Promotion-Dialog ignoriert werden.
   *  Der Dialog erscheint genau unter dem Finger (auf dem Zielfeld) — auf Touch-Geräten
   *  fällt der gerade ausgelöste Zug-Tap sonst direkt auf die oberste Auswahl (Dame)
   *  durch und wandelt ungewollt in die Dame um. Ein kurzes Fenster schluckt diesen Ghost-Tap. */
  private promotionGuardUntil = 0;
  private static readonly PROMOTION_GUARD_MS = 400;

  private static readonly PIECE_NAMES: Record<string, Record<PromotionPiece, string>> = {
    'w': { q: 'wQ', r: 'wR', b: 'wB', n: 'wN' },
    'b': { q: 'bQ', r: 'bR', b: 'bB', n: 'bN' }
  };

  ngAfterViewInit(): void {
    this.ensureChessgroundCss();
    this.initBoard();
    // Eigene Capture-Listener für den Visualisierungs-Modus: erfassen jede Geste als
    // Brett-Koordinate (unabhängig davon, welche Figur auf dem eingefrorenen Brett steht).
    // pointerdown merkt sich das Startfeld, pointerup entscheidet Tap vs. Ziehen.
    this.boardEl.nativeElement.addEventListener('pointerdown', this.onVizPointerDown, true);
    this.boardEl.nativeElement.addEventListener('pointerup', this.onVizPointerUp, true);
  }

  /** Rechnet einen Pointer-Event in ein Brettfeld um (oder null, wenn außerhalb/nicht vermessbar). */
  private keyFromPointer(ev: PointerEvent): Key | null {
    const rect = this.boardEl.nativeElement.getBoundingClientRect();
    if (rect.width === 0) return null;
    const x = ev.clientX - rect.left;
    const y = ev.clientY - rect.top;
    if (x < 0 || y < 0 || x > rect.width || y > rect.height) return null;
    let col = Math.floor(x / (rect.width / 8));   // 0..7 von links
    let row = Math.floor(y / (rect.height / 8));  // 0..7 von oben
    col = Math.max(0, Math.min(7, col));
    row = Math.max(0, Math.min(7, row));
    const fileIdx = this.orientation === 'white' ? col : 7 - col;
    const rankIdx = this.orientation === 'white' ? 7 - row : row;
    return (String.fromCharCode(97 + fileIdx) + (rankIdx + 1)) as Key;
  }

  private onVizPointerDown = (ev: PointerEvent): void => {
    if (!this.visualization || !this.ground) return;
    ev.preventDefault();
    ev.stopPropagation();              // verhindert, dass Chessground den Klick (Figur wählen) verarbeitet
    const key = this.keyFromPointer(ev);
    this.vizPointerStartKey = key ?? undefined;
    this.vizPointerStartX = ev.clientX;
    this.vizPointerStartY = ev.clientY;
    // Pointer einfangen, damit das pointerup auch dann ankommt, wenn der Finger das Brett
    // beim Ziehen kurz verlässt.
    if (key) { try { this.boardEl.nativeElement.setPointerCapture(ev.pointerId); } catch { /* nicht unterstützt */ } }
  };

  private onVizPointerUp = (ev: PointerEvent): void => {
    if (!this.visualization || !this.ground) return;
    const startKey = this.vizPointerStartKey;
    this.vizPointerStartKey = undefined;
    if (!startKey) return;
    ev.preventDefault();
    ev.stopPropagation();
    const endKey = this.keyFromPointer(ev) ?? startKey;
    const movedFar = Math.hypot(ev.clientX - this.vizPointerStartX, ev.clientY - this.vizPointerStartY)
                     >= PuzzleBoardComponent.VIZ_DRAG_THRESHOLD_PX;
    // Ziehgeste (Finger merklich bewegt UND auf einem anderen Feld losgelassen) → Start→Ziel
    // direkt als Zug werten, damit „Figur ziehen" im Viz-Modus genauso geht wie Antippen.
    if (movedFar && endKey !== startKey) {
      this.handleVizDrag(startKey, endKey);
    } else {
      this.handleVizTap(startKey);
    }
  };

  /** Zwei-Tap-Auswahl: 1. Tap wählt das Ausgangsfeld, 2. Tap das Zielfeld. */
  private handleVizTap(key: Key): void {
    if (!this.vizFrom) {
      this.vizFrom = key;
      this.markVizSelection(key);
      return;
    }
    if (key === this.vizFrom) {                    // gleiches Feld → Auswahl aufheben
      this.vizFrom = undefined;
      this.clearVizSelection();
      return;
    }
    const orig = this.vizFrom;
    // Promotion-Erkennung über die tatsächliche chess.js-Stellung (das eingefrorene Brett
    // weiß nichts vom Bauern auf der 7. Reihe, der durch bisherige Viz-Züge dort hingekommen ist).
    const promo = this.detectVizPromotion(orig, key);
    // Legalitäts-Check: ein illegaler 2. Klick wird zum neuen Ausgangsfeld
    // (sonst verschwindet die Auswahl wirkungslos und der Spieler verliert die Orientierung).
    if (!this.isVizLegalMove(orig, key, promo)) {
      this.vizFrom = key;
      this.markVizSelection(key);
      return;
    }
    this.vizFrom = undefined;
    this.clearVizSelection();
    if (promo) {
      this.showVizPromotionDialog(orig, key);
      return;
    }
    this.moveMade.emit({ orig, dest: key });
  }

  /** Ziehgeste Start→Ziel: gleiche Legalitäts-/Promotion-Prüfung wie der 2. Tap. */
  private handleVizDrag(orig: Key, dest: Key): void {
    const promo = this.detectVizPromotion(orig, dest);
    if (!this.isVizLegalMove(orig, dest, promo)) {
      // Illegale Ziehgeste → wie ein Tap das Startfeld auswählen (Orientierung bleibt erhalten).
      this.vizFrom = orig;
      this.markVizSelection(orig);
      return;
    }
    this.vizFrom = undefined;
    this.clearVizSelection();
    if (promo) {
      this.showVizPromotionDialog(orig, dest);
      return;
    }
    this.moveMade.emit({ orig, dest });
  }

  /**
   * Markiert die Viz-Auswahl visuell. Der primäre Marker ist ein DOM-Overlay-`<div>`
   * (über `vizSelectedSquare` ans Template gebunden) — komplett unabhängig von
   * chessgrounds SVG-Drawable-Layer, damit auf jedem Gerät verlässlich ein dicker
   * grüner Ring um das angetappte Feld erscheint. Zusätzlich noch chessgrounds
   * `selectSquare`/`setShapes` als Bonus, falls die Theme/Brushes greifen.
   */
  private markVizSelection(key: Key): void {
    this.vizSelectedSquare = key;
    if (!this.ground) return;
    this.ground.setShapes([{ orig: key, brush: 'green', modifiers: { lineWidth: 22 } }]);
    try { this.ground.selectSquare(key, true); } catch { /* alte chessground-Version */ }
  }

  private clearVizSelection(): void {
    this.vizSelectedSquare = null;
    if (!this.ground) return;
    this.ground.setShapes([]);
    try { this.ground.selectSquare(null); } catch { /* alte chessground-Version */ }
  }

  private applyVizOpponentArrow(): void {
    if (!this.ground) return;
    if (this.visualization > 0 && this.vizOpponentArrow) {
      const shape: DrawShape = { orig: this.vizOpponentArrow[0], dest: this.vizOpponentArrow[1], brush: 'red' };
      this.ground.setAutoShapes([shape]);
    } else {
      this.ground.setAutoShapes([]);
    }
  }

  /** Linker Offset (in %) des Overlay-Rings — abhängig von Orientation und Feldkoordinate. */
  get vizSelectOverlayLeft(): number {
    if (!this.vizSelectedSquare) return 0;
    const fileIdx = this.vizSelectedSquare.charCodeAt(0) - 97;  // 'a' → 0 ... 'h' → 7
    return (this.orientation === 'white' ? fileIdx : 7 - fileIdx) * 12.5;
  }

  /** Top-Offset (in %) des Overlay-Rings — abhängig von Orientation und Feldkoordinate. */
  get vizSelectOverlayTop(): number {
    if (!this.vizSelectedSquare) return 0;
    const rankIdx = parseInt(this.vizSelectedSquare[1], 10) - 1;  // '1' → 0 ... '8' → 7
    return (this.orientation === 'white' ? 7 - rankIdx : rankIdx) * 12.5;
  }

  /** Erkennt einen Bauern-Promotion-Zug im Viz-Modus (anhand actualFen, nicht des frozen Bretts). */
  private detectVizPromotion(orig: Key, dest: Key): boolean {
    if (!this.actualFen) return false;
    const destRank = dest[1];
    if (destRank !== '1' && destRank !== '8') return false;
    try {
      const c = new Chess(this.actualFen);
      const piece = c.get(orig as Square);
      return piece?.type === 'p';
    } catch {
      return false;
    }
  }

  /** Prüft ob (orig→dest) ein legaler Zug in der aktuellen chess.js-Stellung ist. */
  private isVizLegalMove(orig: Key, dest: Key, promotion: boolean): boolean {
    if (!this.actualFen) return true;          // ohne FEN-Info nicht filtern (Fallback)
    try {
      const c = new Chess(this.actualFen);
      const mv = c.move({ from: orig as Square, to: dest as Square, promotion: promotion ? 'q' : undefined });
      return mv !== null;
    } catch {
      return false;
    }
  }

  /** Promotion-Dialog im Viz-Modus — Farbe/Orientierung kommen aus actualFen statt aus dem frozen Brett. */
  private showVizPromotionDialog(orig: Key, dest: Key): void {
    this.pendingPromotion = { orig, dest };
    let color: 'w' | 'b' = this.orientation === 'white' ? 'w' : 'b';
    if (this.actualFen) {
      try {
        const piece = new Chess(this.actualFen).get(orig as Square);
        if (piece) color = piece.color;
      } catch { /* fallback to orientation */ }
    }
    this.promotionColor = color;
    const fileIndex = dest.charCodeAt(0) - 'a'.charCodeAt(0);
    const adjustedFile = this.orientation === 'white' ? fileIndex : 7 - fileIndex;
    this.promotionFilePercent = adjustedFile * 12.5;
    const destRank = dest[1];
    this.promotionFromBottom = (this.orientation === 'white' && destRank === '1') ||
                                (this.orientation === 'black' && destRank === '8');
    this.promotionGuardUntil = Date.now() + PuzzleBoardComponent.PROMOTION_GUARD_MS;
    this.showPromotionOverlay = true;
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showPromotionOverlay) {
      this.cancelPromotion();
    }
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
        // Im Viz-Modus chessgrounds eigene Interaktion abschalten — unser
        // pointerdown-Capture übernimmt die Klicks. Sonst frisst chessground
        // den Tap auf dem Handy und unsere Selection wird wieder geleert.
        color: (this.viewOnly || this.visualization > 0) ? undefined : this.orientation,
        dests: (this.viewOnly || this.visualization > 0) ? undefined : this.dests,
        showDests: true
      },
      premovable: {
        enabled: this.premovable && this.visualization === 0,
        showDests: true
      },
      // Pfeile/Kreise per Rechtsklick-Ziehen (wie im Analysemodus), auch im viewOnly-Zustand.
      drawable: { enabled: true, visible: true },
      events: {
        move: (orig: Key, dest: Key) => {
          if (this.isPromotion(orig, dest)) {
            this.showPromotionDialog(orig, dest);
          } else {
            this.moveMade.emit({ orig, dest });
          }
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

    // Im Viz-Modus uebernimmt unser eigener `onVizPointer`-Handler die Klick-Erfassung.
    // Wenn chessground gleichzeitig auch noch movable ist, kann ein Tap auf eine Figur
    // chessgrounds Auswahl-Logik triggern und unsere Shapes/Selection sofort wieder
    // ueberschreiben — am Handy hat das die Sichtbarkeit des Klicks zerstoert.
    const vizActive = this.visualization > 0;
    const interactionDisabled = this.viewOnly || vizActive;

    this.ground.set({
      fen: this.fen,
      orientation: this.orientation,
      turnColor: this.turnColor,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      movable: {
        free: false,
        color: interactionDisabled ? undefined : this.orientation,
        dests: interactionDisabled ? undefined : this.dests,
        showDests: true
      },
      premovable: {
        enabled: this.premovable && !vizActive
      }
    });

    if ('vizOpponentArrow' in changes || 'visualization' in changes) {
      this.applyVizOpponentArrow();
    }

    // Execute pending premove when transitioning from THINKING to PLAYING
    if (wasPremovable && isNowInteractive && hasPremove) {
      const [orig, dest] = hasPremove;
      // Cancel the premove visual, then emit the move
      this.ground.cancelPremove();
      if (this.premoveTimer) clearTimeout(this.premoveTimer);
      this.premoveTimer = setTimeout(() => {
        this.premoveTimer = undefined;
        // Guard: Component zwischenzeitlich zerstoert / Board weg -> nicht emittieren.
        if (this.destroyed || !this.ground) return;
        if (this.isPromotion(orig, dest)) {
          this.showPromotionDialog(orig, dest);
        } else {
          this.moveMade.emit({ orig, dest });
        }
      }, 50);
    }
  }

  // --- Promotion logic ---

  private isPromotion(orig: Key, dest: Key): boolean {
    if (!this.ground) return false;
    // Check piece at the origin square (before the move was applied by chessground)
    // After chessground applies the move, the piece is at dest
    const piece = this.ground.state.pieces.get(dest);
    if (!piece || piece.role !== 'pawn') return false;
    const destRank = dest[1];
    return destRank === '1' || destRank === '8';
  }

  private showPromotionDialog(orig: Key, dest: Key): void {
    this.pendingPromotion = { orig, dest };
    const piece = this.ground?.state.pieces.get(dest);
    this.promotionColor = piece?.color === 'white' ? 'w' : 'b';

    // Calculate file position (0-7 mapped to percentage)
    const fileChar = dest[0];
    const fileIndex = fileChar.charCodeAt(0) - 'a'.charCodeAt(0);
    // If board is flipped, reverse the file
    const adjustedFile = this.orientation === 'white' ? fileIndex : 7 - fileIndex;
    this.promotionFilePercent = adjustedFile * 12.5;

    // Show from top if promoting to rank 8 (white's side when white orientation)
    // Show from bottom if promoting to rank 1
    const destRank = dest[1];
    this.promotionFromBottom = (this.orientation === 'white' && destRank === '1') ||
                                (this.orientation === 'black' && destRank === '8');

    this.promotionGuardUntil = Date.now() + PuzzleBoardComponent.PROMOTION_GUARD_MS;
    this.showPromotionOverlay = true;
  }

  selectPromotion(piece: PromotionPiece): void {
    if (!this.pendingPromotion) return;
    // Ghost-Tap des Zugs (fällt direkt auf den frisch gerenderten Dialog durch) verwerfen.
    if (Date.now() < this.promotionGuardUntil) return;
    const { orig, dest } = this.pendingPromotion;
    this.showPromotionOverlay = false;
    this.pendingPromotion = null;
    this.moveMade.emit({ orig, dest, promotion: piece });
  }

  cancelPromotion(): void {
    if (!this.pendingPromotion) return;
    // Innerhalb des Guard-Fensters keine versehentliche Abwahl durch den durchfallenden Tap.
    if (Date.now() < this.promotionGuardUntil) return;
    // Reset the board to undo the visual move chessground already made
    this.showPromotionOverlay = false;
    this.pendingPromotion = null;
    // Restore previous position
    if (this.ground) {
      this.ground.set({
        fen: this.fen,
        lastMove: this.lastMove as Key[] | undefined,
        turnColor: this.turnColor,
        check: this.check,
        movable: {
          free: false,
          color: this.viewOnly ? undefined : this.orientation,
          dests: this.viewOnly ? undefined : this.dests,
          showDests: true
        }
      });
    }
  }

  getPieceImage(piece: PromotionPiece): string {
    const name = PuzzleBoardComponent.PIECE_NAMES[this.promotionColor][piece];
    const set = this.pieceSet === '_crazy' ? 'cburnett' : (this.pieceSet || 'cburnett');
    return `url('/piece/${set}/${name}.svg')`;
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.premoveTimer) clearTimeout(this.premoveTimer);
    this.boardEl?.nativeElement?.removeEventListener('pointerdown', this.onVizPointerDown, true);
    this.boardEl?.nativeElement?.removeEventListener('pointerup', this.onVizPointerUp, true);
    this.resizeObserver?.disconnect();
    this.ground?.destroy();
  }
}
