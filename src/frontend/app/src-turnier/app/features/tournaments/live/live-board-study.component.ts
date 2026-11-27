import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { LiveEnginePanelComponent } from '@rh/features/games/live-engine-panel.component';
import { SanPipe } from '@rh/shared/chess/san.pipe';
import { MoveListComponent } from '@rh/shared/pgn-viewer/move-list.component';
import { LiveStudy, writeLiveFsPrefs } from './live-study';

/**
 * Seitenteil eines Live-Bretts im Vollbild (1.036.0): Blättern, „Zur Live-Stellung“, Engine an/aus + Linienzahl,
 * „Selbst ziehen“, die Zugliste der Partie (geteilte `app-move-list`) und die Linien der Browser-Engine (geteilte
 * `app-live-engine-panel`). Den Zustand hält {@link LiveStudy}; hier wird nur gezeigt und weitergereicht.
 *
 * Bewusst ohne Material-Tooltips/-Auswahllisten: die Turnierseite hängt CDK-Overlays nicht ins Vollbild-Element um,
 * dort blieben sie unsichtbar. Erklärt wird per `title`, die Linienzahl ist ein natives `<select>`.
 */
@Component({
  selector: 'app-live-board-study',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe, SanPipe, MoveListComponent, LiveEnginePanelComponent],
  template: `
    @let s = study();
    <div class="ls-bar">
      <button mat-icon-button type="button" class="ls-nav ls-first" (click)="s.first()" [disabled]="s.index() < 0 && !s.hasVariation()"
              [attr.aria-label]="'pgnViewer.nav.first' | translate" [title]="'pgnViewer.nav.first' | translate"><mat-icon>first_page</mat-icon></button>
      <button mat-icon-button type="button" class="ls-nav ls-prev" (click)="s.prev()" [disabled]="s.index() < 0 && !s.session.variation().length"
              [attr.aria-label]="'pgnViewer.nav.previous' | translate" [title]="'pgnViewer.nav.previous' | translate"><mat-icon>chevron_left</mat-icon></button>
      <button mat-icon-button type="button" class="ls-nav ls-next" (click)="s.next()" [disabled]="s.following() && !s.session.canRedo()"
              [attr.aria-label]="'pgnViewer.nav.next' | translate" [title]="'pgnViewer.nav.next' | translate"><mat-icon>chevron_right</mat-icon></button>
      <button mat-icon-button type="button" class="ls-nav ls-last" (click)="s.last()" [disabled]="s.atLive()"
              [attr.aria-label]="'pgnViewer.nav.last' | translate" [title]="'pgnViewer.nav.last' | translate"><mat-icon>last_page</mat-icon></button>
      <span class="ls-spacer"></span>
      @if (s.atLive()) {
        <!-- Man folgt der Partie: Zustand als Symbol + Text, kein Knopf -->
        <span class="ls-live" [title]="'tournaments.live.fs.atLiveTip' | translate"><mat-icon aria-hidden="true">sensors</mat-icon>{{ 'tournaments.live.fs.atLive' | translate }}</span>
      } @else {
        <button mat-flat-button color="primary" type="button" class="ls-tolive" (click)="s.backToLive()">
          <mat-icon>sensors</mat-icon>{{ 'tournaments.live.fs.toLive' | translate }}
        </button>
      }
    </div>
    <div class="ls-opts">
      <button mat-stroked-button type="button" class="ls-engine" [class.on]="s.engineOn()" [attr.aria-pressed]="s.engineOn()"
              (click)="setEngine(!s.engineOn())"><mat-icon>memory</mat-icon>{{ 'tournaments.live.fs.engine' | translate }}</button>
      @if (s.engineOn()) {
        <label class="ls-lines">{{ 'tournaments.live.fs.lines' | translate }}
          <select [value]="'' + s.session.lineSlots()" (change)="setLines($any($event.target).value)">
            @for (n of lineChoices; track n) { <option [value]="'' + n" [selected]="n === s.session.lineSlots()">{{ n }}</option> }
          </select>
        </label>
      }
      <button mat-stroked-button type="button" class="ls-own" [class.on]="s.ownMoves()" [attr.aria-pressed]="s.ownMoves()"
              (click)="s.setOwnMoves(!s.ownMoves())"><mat-icon>pan_tool_alt</mat-icon>{{ 'tournaments.live.fs.ownMoves' | translate }}</button>
    </div>
    @if (s.hasVariation()) {
      <div class="ls-var">
        <mat-icon class="ls-var-icon" aria-hidden="true">alt_route</mat-icon>
        <span class="ls-var-san">{{ s.session.variationSan() | san:'text' }}</span>
        <button mat-button type="button" class="ls-undo" (click)="s.prev()" [disabled]="!s.session.variation().length">
          <mat-icon>undo</mat-icon>{{ 'games.live.undo' | translate }}
        </button>
      </div>
    } @else if (s.ownMoves()) {
      <p class="ls-hint">{{ 'tournaments.live.fs.ownHint' | translate }}</p>
    }
    <div class="ls-moves">
      @if (s.moves().length) {
        <app-move-list [moves]="s.moves()" [currentMoveIndex]="s.index()" [clocks]="s.clocks()" (moveClicked)="s.goTo($event)" />
      } @else if (pgnState() === 'error') {
        <p class="ls-hint">{{ 'tournaments.live.fs.movesFailed' | translate }}
          <button mat-button type="button" class="ls-retry" (click)="retry.emit()">{{ 'common.retry' | translate }}</button></p>
      } @else if (pgnState() === 'loading') {
        <p class="ls-hint">{{ 'tournaments.live.fs.movesLoading' | translate }}</p>
      } @else {
        <p class="ls-hint">{{ 'tournaments.live.fs.noMoves' | translate }}</p>
      }
    </div>
    @if (s.engineOn()) {
      <app-live-engine-panel [session]="s.session" [gameFen]="s.baseFen()" [showVariation]="false" (closed)="setEngine(false)" />
    }
  `,
  styles: [`
    :host { display: flex; flex-direction: column; gap: 8px; min-height: 0; min-width: 0; }
    .ls-bar { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }
    .ls-spacer { flex: 1 1 auto; }
    .ls-live { display: inline-flex; align-items: center; gap: 4px; color: var(--rh-success); font-weight: 500; padding: 0 8px; }
    .ls-live mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .ls-opts { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    /* Schalterknöpfe: an = Akzentfarbe (wie der Engine-Knopf der Zusammenfassung), Zustand zusätzlich per aria-pressed */
    .ls-opts .on { color: var(--rh-accent); border-color: var(--rh-accent); background: color-mix(in srgb, var(--rh-accent) 10%, transparent); }
    :host-context(html.eink) .ls-opts .on { color: black; border-color: black; background: none; font-weight: 700; }
    .ls-lines { display: inline-flex; align-items: center; gap: 6px; font-size: 0.9rem; color: var(--mat-sys-on-surface-variant); }
    .ls-lines select { font: inherit; color: var(--mat-sys-on-surface); background: var(--mat-sys-surface); min-height: 36px;
      padding: 0 6px; border: 1px solid var(--mat-sys-outline); border-radius: 4px; }
    .ls-var { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; font-size: 0.9rem; }
    .ls-var-icon { color: var(--rh-accent); font-size: 18px; width: 18px; height: 18px; }
    .ls-var-san { font-weight: 500; min-width: 0; overflow-wrap: anywhere; }
    .ls-hint { margin: 0; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    /* Zugliste scrollt in ihrem Kasten (der aktive Zug scrollt nur ihn, nie die Seite) */
    .ls-moves { flex: 1 1 auto; min-height: 120px; max-height: 40dvh; overflow: hidden; display: flex; flex-direction: column;
      border: 1px solid color-mix(in srgb, currentColor 14%, transparent); border-radius: 8px; }
    .ls-moves app-move-list { display: block; flex: 1 1 auto; min-height: 0; }
    .ls-moves .ls-hint { padding: 8px; }
    /* Breit (Seitenteil neben dem Brett): die Zugliste nimmt die freie Höhe */
    :host-context(.lb-wide) .ls-moves { max-height: none; }
  `],
})
export class LiveBoardStudyComponent {
  readonly study = input.required<LiveStudy>();
  /** Stand des PGN-Abrufs (Zugliste). */
  readonly pgnState = input<'loading' | 'ok' | 'error'>('loading');
  readonly retry = output<void>();
  readonly lineChoices = [1, 2, 3, 4, 5];

  setEngine(on: boolean): void {
    this.study().setEngine(on);
    this.remember();
  }

  setLines(value: string): void {
    this.study().setLines(Number(value));
    this.remember();
  }

  private remember(): void {
    const s = this.study();
    writeLiveFsPrefs({ engine: s.engineOn(), lines: s.session.lineSlots() });
  }
}
