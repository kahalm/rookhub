import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { PctPipe } from '../../shared/format/pct';
import { WoodpeckerSchedule } from './woodpecker-stats.util';

/**
 * Zeitplan des Woodpecker-Zyklus auf einen Blick (Wunsch 2026-11-18: „neben x pro Tag für fertig auch noch ein
 * Checkmark, wenn ich % der Gesamtpuzzles passend zum aktuellen Tag hab (und in Statistik oben ausgewiesen: noch x
 * Puzzle bis z. B. 55 % erfüllt)“). Geschwister des Tagesziels (`app-woodpecker-daily-goal`, DESIGN 5.12), gerechnet in
 * `woodpeckerSchedule`; steht an denselben drei Stellen direkt dahinter bzw. darunter.
 *
 * Kurz (Dashboard-Kachel, Löser-Kopf):
 * - im Zeitplan: grüner Haken `check_circle` + „Im Zeitplan“ in der Erfolgsfarbe, der Soll-Anteil klein dahinter,
 * - dahinter: leise „Zeitplan: noch 12 bis 55 %“ — neutral, keine Warnfarbe (ein Rückstand am Vormittag ist normal).
 *
 * Ausführlich (`full`, Übersicht im Statistik-Fenster): „Zeitplan (Tag 8 von 14): noch 12 Aufgaben bis 55 %“ bzw.
 * Haken + „55 % erreicht“ und klein der Stand „(aktuell 61 %)“. Nach der Frist ohne Tagesangabe (Soll = 100 %).
 *
 * Symbol UND Klartext, nie nur Farbe (DESIGN 7, E-Ink 3.6). Reine Darstellung; ohne Zeitplan (`null`) rendert der
 * Aufrufer den Baustein gar nicht (in einer `.rh-dots`-Zeile bekäme ein leeres Element sonst einen Trennpunkt).
 */
@Component({
  selector: 'app-woodpecker-schedule',
  imports: [MatIconModule, TranslatePipe, PctPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (schedule(); as s) {
      <span class="wsc" [class.wsc-done]="s.reached" [class.wsc-full]="full()">
        @if (full()) {
          <span class="wsc-label">{{ (s.overdue ? 'woodpecker.schedule.label' : 'woodpecker.schedule.labelDay') | translate: { k: s.day, d: s.days } }}:</span>
          @if (s.reached) {
            <span class="wsc-ok">
              <mat-icon class="wsc-check" aria-hidden="true">check_circle</mat-icon>
              <span class="wsc-reached">{{ 'woodpecker.schedule.reachedPct' | translate: { pct: (s.targetPct | pct) } }}</span>
            </span>
            <span class="wsc-current">({{ 'woodpecker.schedule.current' | translate: { pct: (s.currentPct | pct) } }})</span>
          } @else {
            <span class="wsc-left">{{ (s.left === 1 ? 'woodpecker.schedule.leftLongOne' : 'woodpecker.schedule.leftLong') | translate: { n: s.left, pct: (s.targetPct | pct) } }}</span>
          }
        } @else if (s.reached) {
          <span class="wsc-ok">
            <mat-icon class="wsc-check" aria-hidden="true">check_circle</mat-icon>
            <span class="wsc-reached">{{ 'woodpecker.schedule.onTrack' | translate }}</span>
          </span>
          <span class="wsc-pct">{{ s.targetPct | pct }}</span>
        } @else {
          <span class="wsc-label">{{ 'woodpecker.schedule.label' | translate }}:</span>
          <span class="wsc-left">{{ 'woodpecker.schedule.left' | translate: { n: s.left, pct: (s.targetPct | pct) } }}</span>
        }
      </span>
    }
  `,
  styles: [`
    :host { display: inline-flex; min-width: 0; }
    /* Wie das Tagesziel: eine Zeile aus Teilen mit Lücke; bricht notfalls zwischen den Teilen um, nie mitten in einem. */
    .wsc { display: inline-flex; flex-wrap: wrap; align-items: center; column-gap: 0.3em; row-gap: 2px;
      font-variant-numeric: tabular-nums; }
    .wsc > * { white-space: nowrap; }
    /* Haken und sein Text sind EIN Teil: hinter der Beschriftung der ausführlichen Zeile bliebe der Haken sonst allein
       am Zeilenende stehen (360 px). */
    .wsc-ok { display: inline-flex; align-items: center; column-gap: 0.3em; }
    .wsc-check { flex: none; font-size: 1.3em; width: 1em; height: 1em; color: var(--rh-success); }
    .wsc-reached { color: var(--rh-success); font-weight: 600; }
    .wsc-pct { font-size: 0.9em; }
    /* Aktueller Stand: klein und leise in Klammern, wie die Herleitung hinter dem Tagesziel im Fenster. */
    .wsc-current { font-size: 0.85em; color: var(--mat-sys-on-surface-variant); }
  `],
})
export class WoodpeckerScheduleComponent {
  readonly schedule = input<WoodpeckerSchedule | null>(null);
  /** Ausführliche Zeile fürs Statistik-Fenster (mit Tag k von D, Anzahl in Worten, aktuellem Stand). */
  readonly full = input(false);
}
