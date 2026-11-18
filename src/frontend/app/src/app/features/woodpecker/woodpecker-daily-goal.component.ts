import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { WoodpeckerDailyGoal } from './woodpecker-stats.util';

/**
 * Tagesziel des Woodpecker-Zyklus auf einen Blick (Wunsch 2026-11-18: „auch ohne Statistik visualisieren, wenn ich
 * heute das notwendige Minimum erledigt habe … grüne Checkmark oder so“). EIN Baustein für Dashboard-Kachel,
 * Löser-Kopf und Statistik-Fenster (DESIGN 2.9), gerechnet in `woodpeckerDailyGoal`:
 *
 * - offen: leise „Heute: 23 von 50“ (kein Haken),
 * - erreicht: grüner Haken `check_circle` + „Tagesziel erreicht“ in der Erfolgsfarbe, die Zahl klein dahinter —
 *   Symbol UND Klartext, nie nur Farbe (DESIGN 7, E-Ink 3.6),
 * - Zyklus 1 (kein Soll): nur „Heute: 23“.
 *
 * Reine Darstellung; ohne Ziel (`null`) rendert der Aufrufer den Baustein gar nicht (in einer `.rh-dots`-Zeile bekäme
 * ein leeres Element sonst einen Trennpunkt).
 */
@Component({
  selector: 'app-woodpecker-daily-goal',
  imports: [MatIconModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (goal(); as g) {
      <span class="wdg" [class.wdg-done]="g.reached">
        @if (g.reached) {
          <mat-icon class="wdg-check" aria-hidden="true">check_circle</mat-icon>
          <span class="wdg-reached">{{ 'woodpecker.daily.reached' | translate }}</span>
          <span class="wdg-count">{{ 'woodpecker.daily.count' | translate: { today: g.today, target: g.target } }}</span>
        } @else {
          <span class="wdg-label">{{ 'woodpecker.overview.todayLabel' | translate }}:</span>
          @if (g.target !== null) {
            <span class="wdg-count">{{ 'woodpecker.daily.count' | translate: { today: g.today, target: g.target } }}</span>
          } @else {
            <span class="wdg-count">{{ g.today }}</span>
          }
        }
      </span>
    }
  `,
  styles: [`
    :host { display: inline-flex; min-width: 0; }
    /* Eine Zeile aus Teilen mit Lücke (Angular entfernt Leerraum zwischen Elementen); bricht notfalls zwischen den
       Teilen um, nie mitten in einem. */
    .wdg { display: inline-flex; flex-wrap: wrap; align-items: center; column-gap: 0.3em; row-gap: 2px;
      font-variant-numeric: tabular-nums; }
    .wdg > * { white-space: nowrap; }
    .wdg-check { flex: none; font-size: 1.3em; width: 1em; height: 1em; color: var(--rh-success); }
    .wdg-reached { color: var(--rh-success); font-weight: 600; }
    .wdg-done .wdg-count { font-size: 0.9em; }
  `],
})
export class WoodpeckerDailyGoalComponent {
  readonly goal = input<WoodpeckerDailyGoal | null>(null);
}
