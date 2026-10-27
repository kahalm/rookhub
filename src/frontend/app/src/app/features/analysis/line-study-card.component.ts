import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { LineComparison } from './line-study.util';
import { MOVE_CLASS_COLORS } from '../games/game-review.util';

/**
 * Leiste „Ohne Engine" am Analysebrett (0.855.0, seit 0.855.1 eine Zeile): solange die Engine aus ist, der Fortschritt der
 * Hintergrundrechnung und „Engine einschalten"; danach die Zähler des Vergleichs — das Urteil je Zug (?!, ?, ??) samt Bewertung
 * steht seit 0.860.1 in der Zugliste, nicht mehr als Liste hier. Seit 0.859.0 (Wunsch 2026-10-27: „es soll einen Knopf geben, wo ich die Engine wieder
 * wegschalte") schaltet „Engine ausschalten" sie nach dem Aufdecken wieder weg (nur auf dieser Seite, `hidden`), „Engine
 * einschalten" holt sie zurück.
 */
@Component({
  selector: 'app-line-study-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="line-study">
      <div class="bar">
        <mat-icon class="icon" aria-hidden="true">{{ on() ? 'insights' : 'visibility_off' }}</mat-icon>
        <span class="label" [matTooltip]="on() ? '' : ('analysis.lineStudy.hint' | translate)">
          {{ (on() ? 'analysis.lineStudy.titleRevealed' : 'analysis.lineStudy.title') | translate }}
        </span>
        <span class="progress">{{ 'analysis.lineStudy.progressShort' | translate:{ done: analyzed(), total: total() } }}</span>
        @if (comparison(); as c) {
          @if (on() && c.rated > 0) {
            <!-- Nur die Zähler; das Urteil (?!, ?, ??) und die Bewertung stehen am Zug in der Zugliste (0.860.1, Wunsch
                 2026-10-27: „die Auflistung oben bringt nichts — besser hinter jeden Zug die Eval"). -->
            <span class="counts">
              <span class="cnt best">{{ 'analysis.lineStudy.bestShort' | translate:{ best: c.best, rated: c.rated } }}</span>
              @if (c.inaccuracies) { <span class="cnt" [style.color]="color('inaccuracy')">?! {{ c.inaccuracies }}</span> }
              @if (c.mistakes) { <span class="cnt" [style.color]="color('mistake')">? {{ c.mistakes }}</span> }
              @if (c.blunders) { <span class="cnt" [style.color]="color('blunder')">?? {{ c.blunders }}</span> }
            </span>
          }
        }
        @if (!on()) {
          <button mat-stroked-button type="button" class="reveal" (click)="reveal.emit()">
            <mat-icon>visibility</mat-icon> {{ 'analysis.lineStudy.reveal' | translate }}
          </button>
        } @else {
          <button mat-stroked-button type="button" class="reveal hide" (click)="hide.emit()">
            <mat-icon>visibility_off</mat-icon> {{ 'analysis.lineStudy.hide' | translate }}
          </button>
        }
      </div>
    </div>
  `,
  styles: [`
    /* Schmale Leiste (0.855.1, Wunsch 2026-10-27: „der große Block rechts oben stört") — eine Zeile, Erklärung im Tooltip. */
    .line-study { border: 1px solid color-mix(in srgb, currentColor 18%, transparent); border-radius: 10px; padding: 4px 8px; }
    .bar { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; min-height: 36px; }
    .icon { font-size: 20px; width: 20px; height: 20px; opacity: 0.8; }
    .label { font-weight: 600; }
    .progress { color: var(--mat-sys-on-surface-variant); font-size: 13px; font-variant-numeric: tabular-nums; }
    .reveal { margin-left: auto; height: 32px; }
    .counts { display: inline-flex; flex-wrap: wrap; gap: 8px; margin-left: auto; }
    .cnt { font-weight: 700; font-size: 13px; }
    .cnt.best { font-weight: 500; }
  `],
})
export class LineStudyCardComponent {
  readonly revealed = input(false);
  /** Nach dem Aufdecken wieder weggeschaltet (0.859.0). */
  readonly hidden = input(false);
  /** Engine sichtbar: aufgedeckt und nicht weggeschaltet. */
  readonly on = computed(() => this.revealed() && !this.hidden());
  readonly analyzed = input(0);
  readonly total = input(0);
  readonly comparison = input<LineComparison | null>(null);
  readonly reveal = output<void>();
  readonly hide = output<void>();

  color(cls: string): string { return MOVE_CLASS_COLORS[cls as keyof typeof MOVE_CLASS_COLORS]; }
}
