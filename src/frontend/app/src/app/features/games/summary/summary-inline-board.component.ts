import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../../shared/pgn-viewer/chess-board.component';
import { SanPipe } from '../../../shared/chess/san.pipe';
import { PreferencesService } from '../../../core/preferences.service';
import type { LiveMove } from '../live-engine-session';

/** Stand eines Bretts im Text: Zugfolge ab Partieanfang und wie weit sie gezeigt wird (0 = Anfangsstellung). */
export interface InlineBoardJump { line: readonly LiveMove[]; at: number; }

/**
 * Brett im Fließtext der Zusammenfassung (Wunsch 2026-11-28: „wenn die summary so schmal ist dass das brett nicht da ist …
 * im fließtext immer wieder ein diagramm einblenden … mit <> tasten zum durchklicken“). Unter 900 px steht das Seitenbrett
 * erst hinter dem ganzen Text; die Seite setzt dann hinter einen Absatz mit Partiezügen dieses kleine Brett
 * (`ClubGameSummaryComponent.inlineBoards`). Es zeigt die Stellung nach dem gewählten Zug; ◀ ▶ blättern in derselben
 * Zugfolge — der ganzen Partie bzw. der Variante, die ein Klick auf einen Variantenzug im Absatz gezeigt hat. Der Knopf
 * rechts öffnet das große Brett (Blatt mit Zugliste und Engine) an dieser Stellung.
 */
@Component({
  selector: 'app-summary-inline-board',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, MatButtonModule, MatIconModule, TranslatePipe, SanPipe],
  template: `
    <figure class="inline-board">
      <div class="ib-board">
        <app-chess-board [fen]="fen()" [lastMove]="lastMove()" [flipped]="flipped()" fullscreenMode="none"
                         [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
      </div>
      <figcaption class="ib-nav" role="group" [attr.aria-label]="'playerCard.movesGroup' | translate">
        <button mat-icon-button type="button" class="ib-back" [disabled]="at() === 0" (click)="step(-1)"
                [attr.aria-label]="'playerCard.stepBack' | translate"><mat-icon>chevron_left</mat-icon></button>
        <span class="ib-move" aria-live="polite">
          @if (variation()) { <span class="ib-var">{{ 'games.summary.variation' | translate }}</span> }
          @if (at() > 0) { {{ number() }}{{ san() | san }} } @else { {{ 'gameAnalysis.startPosition' | translate }} }
        </span>
        <button mat-icon-button type="button" class="ib-forward" [disabled]="at() >= jump().line.length" (click)="step(1)"
                [attr.aria-label]="'playerCard.stepForward' | translate"><mat-icon>chevron_right</mat-icon></button>
        <button mat-icon-button type="button" class="ib-open" (click)="open.emit({ line: jump().line, at: at() })"
                [attr.aria-label]="'games.summary.showOnBoard' | translate" [attr.title]="'games.summary.showOnBoard' | translate">
          <mat-icon>open_in_full</mat-icon></button>
      </figcaption>
    </figure>
  `,
  styles: [`
    :host { display: block; }
    .inline-board { margin: 8px auto 16px; max-width: 280px; }
    .ib-board { width: 100%; }
    .ib-nav { display: flex; align-items: center; justify-content: center; gap: 4px; margin-top: 2px; }
    .ib-move { flex: 1 1 auto; min-width: 0; text-align: center; font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap;
      overflow: hidden; text-overflow: ellipsis; }
    .ib-var { font-weight: 400; font-style: italic; color: var(--mat-sys-on-surface-variant); margin-right: 4px; }
    .ib-open { margin-left: -4px; }
  `],
})
export class SummaryInlineBoardComponent {
  readonly preferences = inject(PreferencesService);
  /** Anfangsstellung der Partie (vor dem ersten Zug der Folge). */
  readonly rootFen = input.required<string>();
  readonly jump = input.required<InlineBoardJump>();
  readonly flipped = input(false);
  /** Zugfolge der Partie — daran erkennt das Brett, ob es gerade eine Variante zeigt. */
  readonly game = input<readonly LiveMove[]>([]);
  /** Großes Brett an dieser Stellung öffnen. */
  readonly open = output<InlineBoardJump>();

  /** Gezeigter Halbzug der Folge; ein neuer Sprung (Klick auf einen Zug im Absatz) setzt ihn zurück. */
  readonly at = linkedSignal(() => Math.max(0, Math.min(this.jump().at, this.jump().line.length)));

  readonly fen = computed(() => this.at() === 0 ? this.rootFen() : this.jump().line[this.at() - 1].fen);
  readonly lastMove = computed<[string, string] | undefined>(() => {
    const m = this.at() > 0 ? this.jump().line[this.at() - 1] : undefined;
    return m ? [m.from, m.to] : undefined;
  });
  readonly san = computed(() => this.at() > 0 ? this.jump().line[this.at() - 1].san : '');
  /** „25.“ bzw. „25…“ aus der Stellung vor dem Zug — stimmt auch für Partien ab einer FEN-Stellung. */
  readonly number = computed(() => {
    const before = this.at() <= 1 ? this.rootFen() : this.jump().line[this.at() - 2].fen;
    const [, turn, , , , full] = before.split(' ');
    return turn === 'b' ? `${full}…` : `${full}.`;
  });
  /** Weicht die Folge bis zum gezeigten Zug von der Partie ab? */
  readonly variation = computed(() => {
    const line = this.jump().line, game = this.game();
    for (let i = 0; i < this.at(); i++) if (game[i]?.uci !== line[i].uci) return true;
    return false;
  });

  step(delta: number): void {
    this.at.update(a => Math.max(0, Math.min(a + delta, this.jump().line.length)));
  }
}
