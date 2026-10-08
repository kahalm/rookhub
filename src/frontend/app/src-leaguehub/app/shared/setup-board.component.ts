import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { SetupBoard } from '../core/position-setup';

/**
 * Kleines Brett zum freien Aufstellen (Zug-Editor, Modus „Stellung"): ein 8×8-Raster mit denselben Figurenbildern wie das
 * Spielbrett (`/piece/cburnett/*.svg`). Bewusst nicht `app-chess-board` — das kennt nur legale Züge, keine freie
 * Aufstellung. Meldet nur den Klick auf ein Feld; was dort passiert (setzen, löschen), entscheidet der Editor.
 */
@Component({
  selector: 'lh-setup-board',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sb" role="grid" aria-label="Brett zum Aufstellen">
      @for (cell of cells(); track cell.index) {
        <button type="button" class="sb-sq" [class.dark]="cell.dark" role="gridcell"
                [attr.aria-label]="cell.name + (cell.piece ? ': ' + pieceName(cell.piece) : ': leer')"
                (click)="squareClick.emit(cell.index)">
          @if (cell.piece) { <img class="sb-pc" [src]="pieceSrc(cell.piece)" alt="" draggable="false" /> }
        </button>
      }
    </div>
  `,
  styles: [`
    .sb { display: grid; grid-template-columns: repeat(8, 1fr); width: 100%; aspect-ratio: 1; border: 1px solid var(--line);
      user-select: none; touch-action: manipulation; }
    .sb-sq { position: relative; padding: 0; margin: 0; border: 0; background: #f0d9b5; cursor: pointer; aspect-ratio: 1;
      -webkit-tap-highlight-color: transparent; }
    .sb-sq.dark { background: #b58863; }
    .sb-sq:focus-visible { outline: 3px solid var(--red); outline-offset: -3px; z-index: 1; }
    .sb-pc { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
  `],
})
export class SetupBoardComponent {
  readonly board = input.required<SetupBoard>();
  readonly flipped = input(false);
  readonly squareClick = output<number>();

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
