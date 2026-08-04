import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

/**
 * Kleines Hilfe-Icon (?) statt Fließtext-Absätzen: der Erklärtext liegt in einem Tooltip —
 * Desktop hovert, Touch/Maus können ihn zusätzlich per Klick auf-/zuklappen (matTooltip zeigt
 * auf Touch sonst nur bei Long-Press).
 *
 * UI-Welle 2 (TODO.md „Überladung"): pro Karte höchstens ein Satz sichtbarer Fließtext,
 * alles Erklärende wandert hinter dieses Icon. Mehrzeilige Texte per \n\n trennen
 * (white-space: pre-line in der globalen .hh-tooltip-Klasse).
 */
@Component({
  selector: 'app-help-hint',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule],
  template: `
    <button type="button" class="hh-btn" #tt="matTooltip"
            [matTooltip]="text" matTooltipClass="hh-tooltip"
            (click)="tt.toggle()"
            [attr.aria-label]="text">
      <mat-icon inline>help_outline</mat-icon>
    </button>
  `,
  styles: [`
    :host { display: inline-flex; vertical-align: middle; }
    .hh-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 22px; height: 22px;
      padding: 0;
      border: 0;
      border-radius: 50%;
      background: transparent;
      color: currentColor;
      opacity: 0.55;
      cursor: pointer;
      font-size: 18px;
    }
    .hh-btn:hover, .hh-btn:focus-visible { opacity: 1; }
  `],
})
export class HelpHintComponent {
  /** Bereits übersetzter Hilfetext (mehrere Sätze mit \n\n trennen). */
  @Input() text = '';
}
