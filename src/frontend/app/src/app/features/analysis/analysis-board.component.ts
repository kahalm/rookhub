import {
  Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy, OnInit,
  Output, SimpleChanges, ViewChild
} from '@angular/core';
import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { Color, Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { PromotionPickerComponent, PromotionPiece } from '../../shared/promotion-picker/promotion-picker.component';

/**
 * Interaktives Analyse-Brett (chessground): beide Seiten spielbar (movable.color = turnColor),
 * benutzergezeichnete Pfeile/Kreise (drawable), Engine-Pfeile via autoShapes.
 * Bauernumwandlung über den gemeinsamen `PromotionPickerComponent` (Auswahl statt Auto-Dame).
 */
@Component({
  selector: 'app-analysis-board',
  standalone: true,
  imports: [PromotionPickerComponent],
  template: `
    <div class="ab-wrap board-theme-brown piece-set-cburnett">
      <div #board class="ab-board"></div>
      @if (promo) {
        <app-promotion-picker
          [color]="promo.color" [dest]="promo.dest" [orientation]="orientation"
          (choose)="confirmPromotion($event)" (dismiss)="cancelPromotion()" />
      }
    </div>
  `,
  styles: [`
    .ab-wrap { width: 100%; position: relative; }
    .ab-board { width: 100%; aspect-ratio: 1 / 1; }
  `]
})
export class AnalysisBoardComponent implements OnInit, OnChanges, OnDestroy {
  @Input() fen = '';
  @Input() orientation: Color = 'white';
  @Input() turnColor: Color = 'white';
  @Input() dests: Map<Key, Key[]> = new Map();
  @Input() lastMove?: [Key, Key];
  @Input() check = false;
  /** Engine-Pfeile (autoShapes) — koexistieren mit vom User gezeichneten Pfeilen. */
  @Input() shapes: DrawShape[] = [];
  @Output() moveMade = new EventEmitter<{ orig: Key; dest: Key; promotion?: string }>();

  @ViewChild('board', { static: true }) boardEl!: ElementRef<HTMLDivElement>;
  private ground?: Api;

  /** Offener Umwandlungs-Dialog (orig/dest des Zugs + Farbe des Bauern). */
  promo: { orig: Key; dest: Key; color: 'w' | 'b' } | null = null;

  ngOnInit(): void {
    this.ground = Chessground(this.boardEl.nativeElement, {
      fen: this.fen,
      orientation: this.orientation,
      turnColor: this.turnColor,
      viewOnly: false,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 180 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
      movable: { free: false, color: this.turnColor, dests: this.dests, showDests: true },
      drawable: { enabled: true, visible: true },
      events: {
        move: (orig: Key, dest: Key) => {
          if (this.isPromotion(dest)) {
            this.openPromotion(orig, dest);
          } else {
            this.moveMade.emit({ orig, dest });
          }
        }
      },
    });
    this.applyShapes();
    requestAnimationFrame(() => this.ground?.redrawAll());
  }

  /** Ein Bauer steht nach dem (von chessground bereits angewandten) Zug auf Reihe 1/8. */
  private isPromotion(dest: Key): boolean {
    const piece = this.ground?.state.pieces.get(dest);
    if (!piece || piece.role !== 'pawn') return false;
    return dest[1] === '1' || dest[1] === '8';
  }

  private openPromotion(orig: Key, dest: Key): void {
    const piece = this.ground?.state.pieces.get(dest);
    this.promo = { orig, dest, color: piece?.color === 'black' ? 'b' : 'w' };
  }

  confirmPromotion(piece: PromotionPiece): void {
    if (!this.promo) return;
    const { orig, dest } = this.promo;
    this.promo = null;
    this.moveMade.emit({ orig, dest, promotion: piece });
  }

  /** Abbruch: den von chessground visuell vorweggenommenen Zug zurücksetzen. */
  cancelPromotion(): void {
    this.promo = null;
    this.ground?.set({
      fen: this.fen,
      turnColor: this.turnColor,
      check: this.check,
      lastMove: this.lastMove as Key[] | undefined,
      movable: { free: false, color: this.turnColor, dests: this.dests, showDests: true },
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.ground) return;
    // Brett-State nur aktualisieren, wenn er sich wirklich ändert. WICHTIG: Engine-Updates
    // ändern nur `shapes` (häufig, ~10×/s) — würde man dabei jedes Mal set() aufrufen, würde
    // das laufende Rechtsklick-Zeichnen ständig unterbrochen/verworfen (= „Pfeile gehen nicht").
    if (changes['fen'] || changes['orientation'] || changes['turnColor'] ||
        changes['check'] || changes['lastMove'] || changes['dests']) {
      this.ground.set({
        fen: this.fen,
        orientation: this.orientation,
        turnColor: this.turnColor,
        check: this.check,
        lastMove: this.lastMove as Key[] | undefined,
        movable: { free: false, color: this.turnColor, dests: this.dests, showDests: true },
      });
    }
    if (changes['shapes']) this.applyShapes();
  }

  private applyShapes(): void {
    this.ground?.setAutoShapes(this.shapes ?? []);
  }

  ngOnDestroy(): void {
    this.ground?.destroy();
  }
}
