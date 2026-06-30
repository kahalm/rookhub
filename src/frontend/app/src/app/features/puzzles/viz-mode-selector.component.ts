import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Inline-Umschalter für die fünf Visualisierungsmodi (0–4), der in der Hinweiszeile unter dem
 * Brett sitzt (ersetzt den früheren „Aufdecken"-Knopf). Reine Darstellung: der aktuelle Modus
 * kommt als Input, ein Klick reicht den gewünschten Modus als Event hoch — der Eltern-Solver
 * ruft damit sein vorhandenes `setVisualizationLevel(...)` (setzt Modus + persistiert + startet
 * das Puzzle neu). Die fünf SVG-Icons sind global via `MatIconRegistry` registriert
 * (`viz-0`…`viz-4`, siehe app.component). Modus 0 = Normal deckt wieder auf.
 */
@Component({
  selector: 'app-viz-mode-selector',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule, TranslateModule],
  template: `
    <div class="viz-mode-selector" role="group" [attr.aria-label]="'puzzles.viz.modeSelectAria' | translate">
      @for (m of modes; track m) {
        <button type="button" class="vms-btn" [class.active]="mode === m"
                [attr.aria-pressed]="mode === m"
                [matTooltip]="('puzzles.viz.level' + m + 'Name') | translate"
                [attr.aria-label]="('puzzles.viz.level' + m + 'Name') | translate"
                (click)="modeChange.emit(m)">
          <mat-icon [svgIcon]="'viz-' + m"></mat-icon>
        </button>
      }
    </div>
  `,
  styles: [`
    .viz-mode-selector { display: inline-flex; gap: 4px; }
    .vms-btn {
      width: 32px; height: 32px; display: grid; place-items: center; padding: 0; cursor: pointer;
      border: 1px solid color-mix(in srgb, currentColor 20%, transparent); border-radius: 6px;
      background: var(--mat-sys-surface-container, #fff); color: inherit;
      user-select: none; touch-action: manipulation;
      transition: background .12s, border-color .12s, color .12s;
    }
    .vms-btn:hover { border-color: color-mix(in srgb, currentColor 38%, transparent); }
    .vms-btn:active { transform: translateY(1px); }
    .vms-btn:focus-visible { outline: 2px solid var(--mat-sys-primary, #3f51b5); outline-offset: 2px; }
    .vms-btn.active {
      border-color: var(--mat-sys-primary, #3f51b5);
      background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 16%, transparent);
      color: var(--mat-sys-primary, #3f51b5);
    }
    .vms-btn mat-icon { width: 22px; height: 22px; font-size: 22px; }
    .vms-btn ::ng-deep svg { width: 22px; height: 22px; display: block; }
  `],
})
export class VizModeSelectorComponent {
  readonly modes = [0, 1, 2, 3, 4];
  @Input() mode = 0;
  @Output() modeChange = new EventEmitter<number>();
}
