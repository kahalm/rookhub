import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { SetupBoard } from '../core/position-setup';

/** Ab so vielen Pixeln Bewegung wird aus einem Tipp ein Ziehen (darunter bleibt es ein Klick). */
export const DRAG_THRESHOLD_PX = 6;
/** So lange ohne Bewegung gedrückt = „langer Druck" (Touch-Ersatz für den Rechtsklick). */
export const LONG_PRESS_MS = 400;

/** Laufende Geste (ein Zeiger): Ziehen vom Brett (`from`) oder aus der Palette (`from` = null). */
interface Gesture {
  pointerId: number;
  pointerType: string;
  startX: number;
  startY: number;
  piece: string;
  from: number | null;
  dragging: boolean;
  /** Langer Druck hat schon ausgelöst — die Geste tut nichts mehr. */
  consumed: boolean;
  timer: ReturnType<typeof setTimeout> | null;
}

/** Was die Geisterfigur gerade zeigt. */
export interface SetupDrag {
  piece: string;
  from: number | null;
  over: number | null;
  x: number;
  y: number;
  size: number;
}

/**
 * Kleines Brett zum freien Aufstellen (Zug-Editor, Modus „Stellung"): ein 8×8-Raster mit denselben Figurenbildern wie das
 * Spielbrett (`/piece/cburnett/*.svg`). Bewusst nicht `app-chess-board` — das kennt nur legale Züge, keine freie
 * Aufstellung. Die Komponente ändert die Stellung nicht selbst, sie meldet nur, was passiert ist; die reinen Regeln
 * (`placePiece`/`movePiece`/`removePiece`) stehen in `core/position-setup.ts`.
 *
 * * Klick/Tipp auf ein Feld → `squareClick` (der Editor setzt die gewählte Figur oder leert das Feld).
 * * Rechtsklick (Maus) bzw. langer Druck (Touch, {@link LONG_PRESS_MS} ohne Bewegung, nur mit `altPlace`) → `squareAltClick`
 *   (der Editor setzt die gewählte Figur in Schwarz). Das Kontextmenü des Browsers bleibt auf dem Brett aus.
 * * Ziehen (Pointer Events, Maus UND Finger, ab {@link DRAG_THRESHOLD_PX}): Figur auf ein anderes Feld → `pieceMoved`,
 *   außerhalb des Bretts loslassen → `pieceRemoved`, aufs Ausgangsfeld oder Escape → Abbruch. Aus der Palette
 *   (`paletteDown`, vom Editor an seinen Palette-Knöpfen aufgerufen) aufs Brett → `pieceDropped`.
 *   Kein HTML5-Drag-and-drop — das gibt es auf Touch nicht. Nach einem Ziehen wird der eine Klick, den der Browser
 *   hinterherschickt, verschluckt, damit Klick und Ziehen sich nicht stören.
 */
@Component({
  selector: 'lh-setup-board',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #root class="sb" [class.dragging]="!!drag()" role="grid" aria-label="Brett zum Aufstellen" (contextmenu)="onContextMenu($event)">
      @for (cell of cells(); track cell.index) {
        <button type="button" class="sb-sq" [class.dark]="cell.dark" [class.has]="!!cell.piece" role="gridcell"
                [class.from]="drag()?.from === cell.index" [class.over]="drag()?.over === cell.index"
                [attr.data-i]="cell.index"
                [attr.aria-label]="cell.name + (cell.piece ? ': ' + pieceName(cell.piece) : ': leer')"
                (pointerdown)="onSquareDown($event, cell.index)" (click)="squareClick.emit(cell.index)">
          @if (cell.piece) { <img class="sb-pc" [src]="pieceSrc(cell.piece)" alt="" draggable="false" /> }
        </button>
      }
    </div>
    @if (drag(); as d) {
      <img class="sb-ghost" [src]="pieceSrc(d.piece)" alt="" draggable="false"
           [style.width.px]="d.size" [style.height.px]="d.size" [style.left.px]="d.x - d.size / 2" [style.top.px]="d.y - d.size / 2" />
    }
  `,
  styles: [`
    .sb { display: grid; grid-template-columns: repeat(8, 1fr); width: 100%; aspect-ratio: 1; border: 1px solid var(--line);
      user-select: none; -webkit-user-select: none; -webkit-touch-callout: none; }
    .sb.dragging { touch-action: none; cursor: grabbing; }
    .sb-sq { position: relative; padding: 0; margin: 0; border: 0; background: #f0d9b5; cursor: pointer; aspect-ratio: 1;
      -webkit-tap-highlight-color: transparent; touch-action: manipulation; -webkit-touch-callout: none; }
    /* Auf einer Figur scrollt ein Wisch die Seite NICHT — er zieht die Figur. Leere Felder lassen das Scrollen zu. */
    .sb-sq.has { touch-action: none; cursor: grab; }
    .sb-sq.dark { background: #b58863; }
    .sb-sq:focus-visible { outline: 3px solid var(--red); outline-offset: -3px; z-index: 1; }
    .sb-sq.from .sb-pc { opacity: .25; }
    .sb-sq.from { box-shadow: inset 0 0 0 100px rgba(0, 0, 0, .18); }
    .sb-sq.over { box-shadow: inset 0 0 0 3px var(--red, #c0392b), inset 0 0 0 100px rgba(255, 255, 255, .25); }
    .sb-pc { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
    .sb-ghost { position: fixed; z-index: 2000; pointer-events: none; filter: drop-shadow(0 4px 6px rgba(0, 0, 0, .35)); }
  `],
})
export class SetupBoardComponent {
  readonly board = input.required<SetupBoard>();
  readonly flipped = input(false);
  /** Langer Druck (Touch) meldet `squareAltClick` — nur, wenn der Editor etwas damit anfangen kann (Figur gewählt). */
  readonly altPlace = input(false);
  readonly squareClick = output<number>();
  /** Rechtsklick bzw. langer Druck auf ein Feld. */
  readonly squareAltClick = output<number>();
  readonly pieceMoved = output<{ from: number; to: number }>();
  readonly pieceDropped = output<{ piece: string; to: number }>();
  readonly pieceRemoved = output<{ from: number }>();

  readonly drag = signal<SetupDrag | null>(null);
  private readonly root = viewChild.required<ElementRef<HTMLElement>>('root');
  private gesture: Gesture | null = null;
  private lastPointerType = 'mouse';

  constructor() {
    inject(DestroyRef).onDestroy(() => this.end());
  }

  /** Felder in Anzeigereihenfolge (von oben links), je mit Index ins Brett-Feld. */
  readonly cells = computed(() => {
    const b = this.board();
    const out: { index: number; piece: string; dark: boolean; name: string }[] = [];
    for (let i = 0; i < 64; i++) {
      const index = this.flipped() ? 63 - i : i;
      const r = Math.floor(index / 8), f = index % 8;
      out.push({ index, piece: b[index] ?? '', dark: (r + f) % 2 === 1, name: `${'abcdefgh'[f]}${8 - r}` });
    }
    return out;
  });

  pieceSrc(p: string): string {
    return pieceSrc(p);
  }

  pieceName(p: string): string {
    return pieceName(p);
  }

  /** Brett-Feld (0 = a8 … 63 = h1) unter einer Zeigerposition, `null` außerhalb des Bretts. Beachtet die Drehung. */
  indexAt(x: number, y: number): number | null {
    const r = this.root().nativeElement.getBoundingClientRect();
    if (r.width <= 0 || x < r.left || x >= r.right || y < r.top || y >= r.bottom) return null;
    const col = Math.min(7, Math.floor((x - r.left) / (r.width / 8)));
    const row = Math.min(7, Math.floor((y - r.top) / (r.height / 8)));
    const shown = row * 8 + col;
    return this.flipped() ? 63 - shown : shown;
  }

  onSquareDown(ev: PointerEvent, index: number): void {
    this.lastPointerType = ev.pointerType || 'mouse';
    if (ev.button !== 0 || this.gesture) return;
    const piece = this.board()[index] ?? '';
    const longPress = this.altPlace() && this.lastPointerType !== 'mouse';
    if (!piece && !longPress) return;
    this.begin(ev, piece, piece ? index : null);
    if (longPress) {
      this.gesture!.timer = setTimeout(() => {
        const g = this.gesture;
        if (!g || g.dragging) return;
        g.timer = null;
        g.consumed = true;
        this.squareAltClick.emit(index);
      }, LONG_PRESS_MS);
    }
  }

  /** Vom Editor an einem Palette-Knopf gerufen: Figur aus der Palette aufs Brett ziehen. Ohne Bewegung bleibt es der Klick. */
  paletteDown(piece: string, ev: PointerEvent): void {
    this.lastPointerType = ev.pointerType || 'mouse';
    if (ev.button !== 0 || this.gesture || piece === 'x') return;
    this.begin(ev, piece, null);
  }

  /** Kontextmenü auf dem Brett aus; der Rechtsklick der MAUS setzt in Schwarz (Touch erledigt das über den langen Druck). */
  onContextMenu(ev: MouseEvent): void {
    ev.preventDefault();
    if (this.lastPointerType !== 'mouse' || this.gesture?.dragging) return;
    const cell = (ev.target as HTMLElement | null)?.closest?.('[data-i]');
    const index = cell ? Number(cell.getAttribute('data-i')) : this.indexAt(ev.clientX, ev.clientY);
    if (index !== null && !Number.isNaN(index)) this.squareAltClick.emit(index);
  }

  private begin(ev: PointerEvent, piece: string, from: number | null): void {
    this.gesture = { pointerId: ev.pointerId, pointerType: this.lastPointerType, startX: ev.clientX, startY: ev.clientY,
      piece, from, dragging: false, consumed: false, timer: null };
    window.addEventListener('pointermove', this.onMove);
    window.addEventListener('pointerup', this.onUp);
    window.addEventListener('pointercancel', this.onCancel);
    window.addEventListener('keydown', this.onKey, true);
    window.addEventListener('touchmove', this.onTouchMove, { passive: false });
  }

  private readonly onMove = (ev: PointerEvent): void => {
    const g = this.gesture;
    if (!g || ev.pointerId !== g.pointerId || g.consumed) return;
    if (!g.dragging) {
      if (Math.hypot(ev.clientX - g.startX, ev.clientY - g.startY) < DRAG_THRESHOLD_PX) return;
      this.clearTimer();
      if (!g.piece) { this.end(); return; }   // leeres Feld, nur auf den langen Druck gewartet
      g.dragging = true;
      try { this.root().nativeElement.setPointerCapture(ev.pointerId); } catch { /* synthetischer Zeiger (Tests) */ }
    }
    ev.preventDefault();
    const size = this.root().nativeElement.getBoundingClientRect().width / 8 || 40;
    this.drag.set({ piece: g.piece, from: g.from, over: this.indexAt(ev.clientX, ev.clientY), x: ev.clientX, y: ev.clientY, size });
  };

  private readonly onUp = (ev: PointerEvent): void => {
    const g = this.gesture;
    if (!g || ev.pointerId !== g.pointerId) return;
    const swallow = g.dragging || g.consumed;
    if (g.dragging) {
      const to = this.indexAt(ev.clientX, ev.clientY);
      if (g.from === null) { if (to !== null) this.pieceDropped.emit({ piece: g.piece, to }); }
      else if (to === null) this.pieceRemoved.emit({ from: g.from });
      else if (to !== g.from) this.pieceMoved.emit({ from: g.from, to });
    }
    this.end();
    if (swallow) swallowNextClick();
  };

  private readonly onCancel = (ev: PointerEvent): void => {
    if (this.gesture && ev.pointerId === this.gesture.pointerId) this.cancel();
  };

  private readonly onKey = (ev: KeyboardEvent): void => {
    if (ev.key !== 'Escape' || !this.gesture?.dragging) return;
    // Escape bricht nur das Ziehen ab, nicht den ganzen Dialog.
    ev.preventDefault();
    ev.stopImmediatePropagation();
    this.cancel();
  };

  private readonly onTouchMove = (ev: TouchEvent): void => {
    if (this.gesture?.dragging && ev.cancelable) ev.preventDefault();
  };

  private cancel(): void {
    const swallow = !!this.gesture && (this.gesture.dragging || this.gesture.consumed);
    this.end();
    if (swallow) swallowNextClick();
  }

  private clearTimer(): void {
    if (this.gesture?.timer) { clearTimeout(this.gesture.timer); this.gesture.timer = null; }
  }

  private end(): void {
    this.clearTimer();
    this.gesture = null;
    this.drag.set(null);
    window.removeEventListener('pointermove', this.onMove);
    window.removeEventListener('pointerup', this.onUp);
    window.removeEventListener('pointercancel', this.onCancel);
    window.removeEventListener('keydown', this.onKey, true);
    window.removeEventListener('touchmove', this.onTouchMove);
  }
}

/** Den einen Klick verschlucken, den der Browser nach einem Ziehen/langen Druck noch schickt (höchstens 500 ms lang). */
function swallowNextClick(): void {
  const kill = (e: Event): void => { e.stopPropagation(); e.preventDefault(); done(); };
  const done = (): void => { document.removeEventListener('click', kill, true); clearTimeout(t); };
  const t = setTimeout(done, 500);
  document.addEventListener('click', kill, true);
}

/** Bild einer Figur („K" = weißer König, „n" = schwarzer Springer). */
export function pieceSrc(p: string): string {
  return `/piece/cburnett/${p === p.toUpperCase() ? 'w' : 'b'}${p.toUpperCase()}.svg`;
}

const NAMES: Record<string, string> = { K: 'König', Q: 'Dame', R: 'Turm', B: 'Läufer', N: 'Springer', P: 'Bauer' };

export function pieceName(p: string): string {
  const white = p === p.toUpperCase();
  const female = p.toUpperCase() === 'Q';
  return `${white ? (female ? 'weiße' : 'weißer') : (female ? 'schwarze' : 'schwarzer')} ${NAMES[p.toUpperCase()] ?? p}`;
}
