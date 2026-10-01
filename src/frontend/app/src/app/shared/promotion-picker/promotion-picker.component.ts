import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, HostListener, Input, OnDestroy, OnInit,
  Output, QueryList, ViewChildren,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
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
 *
 * Tastatur/Screenreader (Codereview F8-018): die Figuren sind benannte Knöpfe, beim Öffnen steht der
 * Fokus auf der Dame, Esc bricht ab (auch im Guard-Fenster — Esc ist kein Ghost-Tap) und hält die
 * Taste von einem umgebenden Dialog fern; beim Schließen geht der Fokus dorthin zurück, wo er vorher war.
 */
@Component({
  selector: 'app-promotion-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    <div class="promotion-backdrop" aria-hidden="true" (click)="onDismiss()"></div>
    <div class="promotion-choices" role="group" [attr.aria-label]="'promotion.group' | translate"
         [style.left.%]="filePercent" [class.from-bottom]="fromBottom">
      @for (piece of pieces; track piece) {
        <button #choice type="button" class="promotion-piece" (click)="onChoose(piece)"
                [attr.aria-label]="'promotion.' + piece | translate">
          <span class="piece-icon" aria-hidden="true" [style.backgroundImage]="image(piece)"></span>
        </button>
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
      border: 0; padding: 0; margin: 0; font: inherit;
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
    .promotion-piece:focus-visible { outline: 3px solid #1976d2; outline-offset: -3px; }
    .piece-icon {
      display: block;
      width: 85%; height: 85%;
      background-size: contain;
      background-repeat: no-repeat;
      background-position: center;
    }
  `]
})
export class PromotionPickerComponent implements OnInit, AfterViewInit, OnDestroy {
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

  @ViewChildren('choice') private choices?: QueryList<ElementRef<HTMLButtonElement>>;
  /** Wo der Fokus vor dem Öffnen stand (z. B. das Zug-Eingabefeld) — dorthin kehrt er beim Schließen zurück. */
  private returnFocusTo: HTMLElement | null = null;

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
    const active = typeof document !== 'undefined' ? document.activeElement : null;
    this.returnFocusTo = active instanceof HTMLElement && active !== document.body ? active : null;
  }

  ngAfterViewInit(): void {
    // Fokus auf die Dame, ohne die Seite zu verschieben (das Brett darf sich nie bewegen).
    this.choices?.first?.nativeElement.focus({ preventScroll: true });
  }

  ngOnDestroy(): void {
    const back = this.returnFocusTo;
    this.returnFocusTo = null;
    if (!back || !back.isConnected) return;
    // Nur zurückholen, wenn der Fokus noch bei uns (oder nirgends) liegt — hat der Nutzer inzwischen
    // woanders hingeklickt, bleibt er dort.
    const active = document.activeElement;
    const ours = !active || active === document.body
      || (this.choices?.some(c => c.nativeElement === active) ?? false);
    if (ours) back.focus({ preventScroll: true });
  }

  /** Esc bricht ab — bewusst OHNE Guard (Esc kann kein Ghost-Tap sein) und ohne Weitergabe: ein Brett in
   *  einem Dialog soll mit Esc die Umwandlung abbrechen, nicht den Dialog schließen. */
  @HostListener('keydown.escape', ['$event'])
  onEscape(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    this.dismiss.emit();
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
