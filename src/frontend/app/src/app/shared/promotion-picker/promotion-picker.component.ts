import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { Color, Key } from 'chessground/types';

export type PromotionPiece = 'q' | 'r' | 'b' | 'n';

/**
 * Wiederverwendbarer Bauernumwandlungs-Dialog (über einem chessground-Brett).
 *
 * Muss in einem `position: relative`-Container liegen, der das Brett exakt überdeckt
 * (der Host legt sich per `position: absolute; inset: 0` darüber). Die Auswahl-Spalte
 * positioniert sich über `dest`/`orientation` genau auf der Umwandlungs-Datei.
 *
 * Mobile-Schutz: Der Dialog erscheint direkt unter dem Finger (auf dem Zielfeld) —
 * der gerade ausgelöste Zug-Tap würde sonst auf die oberste Auswahl (Dame) durchfallen
 * und ungewollt umwandeln. Ein kurzes Guard-Fenster verwirft diesen Ghost-Tap.
 */
@Component({
  selector: 'app-promotion-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="promotion-backdrop" (click)="onDismiss()"></div>
    <div class="promotion-choices" [style.left.%]="filePercent" [class.from-bottom]="fromBottom">
      @for (piece of pieces; track piece) {
        <div class="promotion-piece" (click)="onChoose(piece)">
          <div class="piece-icon" [style.backgroundImage]="image(piece)"></div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { position: absolute; inset: 0; z-index: 100; display: block; }
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
  `]
})
export class PromotionPickerComponent implements OnInit {
  /** Farbe des umwandelnden Bauern (für die Figurengrafik). */
  @Input({ required: true }) color!: 'w' | 'b';
  /** Zielfeld der Umwandlung (z.B. 'a8') — bestimmt Datei + Richtung des Dialogs. */
  @Input({ required: true }) dest!: Key;
  @Input() orientation: Color = 'white';
  @Input() pieceSet = 'cburnett';

  @Output() choose = new EventEmitter<PromotionPiece>();
  @Output() dismiss = new EventEmitter<void>();

  pieces: PromotionPiece[] = ['q', 'r', 'b', 'n'];
  filePercent = 0;
  fromBottom = false;

  private guardUntil = 0;
  private static readonly GUARD_MS = 400;
  private static readonly NAMES: Record<'w' | 'b', Record<PromotionPiece, string>> = {
    w: { q: 'wQ', r: 'wR', b: 'wB', n: 'wN' },
    b: { q: 'bQ', r: 'bR', b: 'bB', n: 'bN' }
  };

  ngOnInit(): void {
    const fileIndex = this.dest.charCodeAt(0) - 'a'.charCodeAt(0);
    this.filePercent = (this.orientation === 'white' ? fileIndex : 7 - fileIndex) * 12.5;
    const rank = this.dest[1];
    this.fromBottom = (this.orientation === 'white' && rank === '1') ||
                      (this.orientation === 'black' && rank === '8');
    this.guardUntil = Date.now() + PromotionPickerComponent.GUARD_MS;
  }

  onChoose(piece: PromotionPiece): void {
    if (Date.now() < this.guardUntil) return;   // Ghost-Tap des Zugs verwerfen
    this.choose.emit(piece);
  }

  onDismiss(): void {
    if (Date.now() < this.guardUntil) return;   // Ghost-Tap nicht als Abbruch werten
    this.dismiss.emit();
  }

  image(piece: PromotionPiece): string {
    const set = this.pieceSet === '_crazy' ? 'cburnett' : (this.pieceSet || 'cburnett');
    return `url('/piece/${set}/${PromotionPickerComponent.NAMES[this.color][piece]}.svg')`;
  }
}
