import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MOVE_CLASS_COLORS } from '@rh/features/games/game-review.util';

/**
 * Die Abzeichen einer Vereinspartie (0.852.0, Wunsch 2026-10-27: „finde es schaut noch nicht gut aus" → kleine runde Abzeichen
 * statt Text hinter dem Ergebnis): „+" auf Gold = interessante Stellungen (wie der „+"-Knopf in RookHub, mit dem man sie
 * markiert), „!!" auf Türkis = brillante Züge (Farbe des Rückblicks, wie bei chess.com). Die Zahl erst ab 2. Ein Klick
 * meldet `pick` — die Seite klappt das Nachspielen auf und springt zur ersten Stellung bzw. zum ersten „!!"-Zug.
 * `columns` (0.860.0, Wunsch 2026-10-27: „wirkt bissl unruhig, vielleicht besser in eine Flucht bringen?"): in Listen und
 * Tabellen hat jedes Abzeichen einen festen Platz gleicher Breite, ein fehlendes lässt seinen Platz leer — „+" und „!!" stehen
 * dann Zeile für Zeile untereinander.
 */
@Component({
  selector: 'lh-game-badges',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.cols]': 'columns()' },
  template: `
    @if (marks() > 0) {
      <button type="button" class="gb gb-mark" [style.background]="markColor" [style.color]="markText" (click)="pick.emit('mark')"
              [title]="(marks() === 1 ? '1 interessante Stellung' : marks() + ' interessante Stellungen') + ' — anspringen'"
              [attr.aria-label]="(marks() === 1 ? '1 interessante Stellung' : marks() + ' interessante Stellungen') + ', anspringen'">+{{ marks() > 1 ? marks() : '' }}</button>
    } @else if (columns()) { <span class="gb gb-slot" aria-hidden="true"></span> }
    @if (brilliant() > 0) {
      <button type="button" class="gb gb-brilliant" [style.background]="brilliantColor" [style.color]="brilliantText" (click)="pick.emit('brilliant')"
              [title]="(brilliant() === 1 ? '1 brillanter Zug' : brilliant() + ' brillante Züge') + ' — anspringen'"
              [attr.aria-label]="(brilliant() === 1 ? '1 brillanter Zug' : brilliant() + ' brillante Züge') + ', anspringen'">!!{{ brilliant() > 1 ? brilliant() : '' }}</button>
    } @else if (columns()) { <span class="gb gb-slot" aria-hidden="true"></span> }
  `,
  styles: [`
    :host { display: inline-flex; gap: 4px; align-items: center; vertical-align: middle; }
    :host:empty { display: none; }
    .gb { min-width: 22px; height: 20px; padding: 0 6px; border: 0; border-radius: 10px; cursor: pointer;
      font: 700 12px/20px var(--cond, inherit); letter-spacing: 0.02em; text-align: center; white-space: nowrap; }
    :host(.cols) .gb { width: 36px; min-width: 0; padding: 0; }
    .gb-slot { display: inline-block; visibility: hidden; }
    .gb:focus-visible { outline: 2px solid var(--ink); outline-offset: 1px; }
  `],
})
export class GameBadgesComponent {
  /** Zahl der interessanten Stellungen (0 = kein Abzeichen). */
  readonly marks = input(0);
  /** Zahl der brillanten Züge (0 = kein Abzeichen). */
  readonly brilliant = input(0);
  /** Feste Plätze gleicher Breite (Listen, Tabellen) — siehe oben. */
  readonly columns = input(false);
  readonly pick = output<'mark' | 'brilliant'>();
  /** Gold wie die Klasse „Ungenauigkeit" im Rückblick — ein eigenes Gelb gibt es im Farbsatz nicht. */
  readonly markColor = MOVE_CLASS_COLORS.inaccuracy;
  readonly brilliantColor = MOVE_CLASS_COLORS.brilliant;
  /** Schrift auf den festen Flächen — dunkel auf beiden (weiß auf Türkis hätte nur ~2,3:1), unabhängig vom Modus. */
  readonly markText = '#1b1b1b';
  readonly brilliantText = '#0b2e28';
}
