import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { LiveEngineSession } from './live-engine-session';

/**
 * Leiste der Live-Engine unter dem Brett der Partieseite: welche Engine rechnet und wie tief, die eigene
 * Nebenvariante (mit „Zug zurück" und „Zurück zur Partie") und die Linien der Stellung auf dem Brett. Den
 * Zustand hält die {@link LiveEngineSession}; hier wird nur gezeigt und weitergereicht.
 */
@Component({
  selector: 'app-live-engine-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    @let s = session();
    <section class="live">
      <div class="head">
        <mat-icon class="engine-icon">memory</mat-icon>
        <span class="engine">{{ s.engineName() ?? ('games.live.browser' | translate) }}</span>
        @if (s.depth() > 0) { <span class="depth">{{ 'games.live.depth' | translate: { depth: s.depth() } }}</span> }
        <span class="spacer"></span>
        <button mat-icon-button type="button" class="close" (click)="closed.emit()"
                [matTooltip]="'common.close' | translate" [attr.aria-label]="'common.close' | translate">
          <mat-icon>close</mat-icon>
        </button>
      </div>
      @if (s.variation().length || s.canRedo()) {
        <div class="variation">
          <span class="variation-san">{{ s.variationSan() }}</span>
          <button mat-stroked-button type="button" class="undo" (click)="s.undo(gameFen())">
            <mat-icon>undo</mat-icon> {{ 'games.live.undo' | translate }}
          </button>
          @if (s.canRedo()) {
            <button mat-stroked-button type="button" class="redo" (click)="s.redo(gameFen())">
              <mat-icon>redo</mat-icon> {{ 'games.live.redo' | translate }}
            </button>
          }
          <button mat-stroked-button type="button" class="back" (click)="s.reset(gameFen())">
            <mat-icon>replay</mat-icon> {{ 'games.live.backToGame' | translate }}
          </button>
        </div>
      } @else {
        <div class="hint">{{ 'games.live.hint' | translate }}</div>
      }
      <ol class="lines">
        @for (l of s.lines(); track $index) {
          <li>
            <span class="line-eval" [class.white]="l.positive">{{ l.evalText }}</span>
            <span class="line-san">{{ l.san }}</span>
          </li>
        }
      </ol>
    </section>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .live {
      display: flex; flex-direction: column; gap: 4px; padding: 6px 8px; border-radius: 6px;
      border: 1px solid color-mix(in srgb, #42a5f5 45%, transparent);
    }
    .head { display: flex; align-items: center; gap: 6px; font-size: 0.85rem; }
    .engine-icon { font-size: 18px; width: 18px; height: 18px; color: #42a5f5; }
    .engine { font-weight: 600; }
    .depth { color: color-mix(in srgb, currentColor 65%, transparent); font-variant-numeric: tabular-nums; }
    .spacer { flex: 1 1 auto; }
    .close { --mat-icon-button-state-layer-size: 30px; width: 30px; height: 30px; padding: 3px; }
    .close mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .variation { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; font-size: 0.85rem; }
    .variation-san { font-weight: 500; min-width: 0; overflow-wrap: anywhere; }
    .hint { font-size: 0.8rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .lines { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; font-size: 0.82rem; }
    .lines li { display: flex; gap: 8px; align-items: baseline; min-width: 0; padding: 1px 4px; }
    .line-eval {
      flex: 0 0 auto; min-width: 3.4em; text-align: center; padding: 0 4px; border-radius: 3px;
      font-weight: 600; font-variant-numeric: tabular-nums; background: #403e3b; color: #fff;
    }
    .line-eval.white { background: #fff; color: #262421; box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.2); }
    .line-san { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  `],
})
export class LiveEnginePanelComponent {
  session = input.required<LiveEngineSession>();
  /** Stellung der Partie am aktuellen Zug — dorthin führen „Zug zurück" (am Anfang) und „Zurück zur Partie". */
  gameFen = input.required<string>();
  closed = output<void>();
}
