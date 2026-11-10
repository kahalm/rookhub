import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { daysLeft, WoodpeckerPlan } from '../woodpecker/woodpecker.service';

/**
 * Inhalt der Dashboard-Kachel „Woodpecker“ (Kopf mit Titel/Untertitel baut das Dashboard wie bei jeder Kachel):
 * das laufende Training auf einen Blick — Name, Fortschritt des Zyklus, Frist — und genau EINE Aktion je Zustand
 * (DESIGN 2.1/8.1): `solving` → „Weiter lösen“ (Kurs-Löser `/woodpecker/:id/solve`, wie die Übersicht), `ready` → zur
 * Übersicht (dort startet der Zyklus), `break` → Pause-Satz + Textlink zur Übersicht. Ohne laufendes Training (nur im
 * Anpassen-Modus sichtbar) ein Satz und „Neues Training“.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-woodpecker-tile',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  template: `
    @if (plan(); as p) {
      @let c = cycle()!;
      <mat-card-content class="wp-content">
        <a class="wp-name" routerLink="/woodpecker" [title]="p.name">{{ p.name }}</a>
        @switch (p.status) {
          @case ('solving') {
            @if (c.number === 1) {
              <p class="wp-line">{{ (p.setSize === 1 ? 'woodpecker.setGrowingOne' : 'woodpecker.setGrowing') | translate: { n: p.setSize, total: p.availableCount } }}</p>
            } @else {
              @let progress = 'woodpecker.cycleProgress' | translate: { done: c.attempted, total: p.setSize };
              <p class="wp-line">{{ progress }}</p>
              <mat-progress-bar class="wp-bar" mode="determinate" [value]="donePercent()" [attr.aria-label]="progress" />
            }
            <p class="wp-deadline" [class.over]="overdue()">
              {{ (overdue() ? 'woodpecker.overdue' : (left() === 1 ? 'woodpecker.daysLeftOne' : 'woodpecker.daysLeft')) | translate: { days: left() } }}
            </p>
          }
          @case ('break') {
            <p class="wp-line">{{ 'woodpecker.breakUntil' | translate: { n: c.number + 1, at: (p.nextCycleAt | date: 'short') } }}</p>
          }
          @case ('ready') {
            <p class="wp-line">{{ (nextDays() === 1 ? 'woodpecker.readyOne' : 'woodpecker.ready') | translate: { n: c.number + 1, days: nextDays(), size: p.setSize } }}</p>
          }
        }
      </mat-card-content>
      <mat-card-actions>
        @switch (p.status) {
          @case ('solving') {
            <a mat-flat-button color="primary" class="wp-primary" [routerLink]="['/woodpecker', p.id, 'solve']">
              <mat-icon>play_arrow</mat-icon>{{ 'woodpecker.continue' | translate }}
            </a>
          }
          @case ('ready') {
            <a mat-flat-button color="primary" class="wp-primary" routerLink="/woodpecker">
              <mat-icon>replay</mat-icon>{{ 'woodpecker.toOverview' | translate }}
            </a>
          }
          @default {
            <a mat-button class="wp-overview" routerLink="/woodpecker">{{ 'woodpecker.toOverview' | translate }}</a>
          }
        }
      </mat-card-actions>
    } @else {
      <mat-card-content class="wp-content">
        <p class="wp-empty">{{ 'dashboard.woodpecker.none' | translate }}</p>
      </mat-card-content>
      <mat-card-actions>
        <a mat-button class="wp-create" routerLink="/woodpecker">{{ 'woodpecker.new' | translate }}</a>
      </mat-card-actions>
    }
  `,
  styles: [`
    /* Füllt die Kachel (Flex-Spalte des Dashboards), damit die Aktionszeile wie bei den Nachbarkacheln unten sitzt. */
    :host { display: flex; flex-direction: column; flex: 1 1 auto; }
    .wp-content { display: flex; flex-direction: column; gap: 0.35rem; padding-top: 0.25rem; }
    /* Kursname = Name des Trainings: bis zwei Zeilen, dann „…“ (wie die Übersicht, d6). */
    .wp-name { font-weight: 600; font-size: 0.92rem; line-height: 1.3; color: inherit; text-decoration: none;
      display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; line-clamp: 2; overflow: hidden;
      overflow-wrap: anywhere; }
    .wp-name:hover, .wp-name:focus-visible { text-decoration: underline; }
    .wp-line { margin: 0; font-size: 0.9rem; }
    .wp-bar { border-radius: 999px; overflow: hidden; height: 6px; }
    .wp-deadline { margin: 0; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .wp-deadline.over { color: var(--rh-warn); }
    .wp-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; margin: 0; font-size: 0.9rem; }
    mat-card-actions { margin-top: auto; flex-wrap: wrap; }
    mat-card-actions > a { min-width: 0; white-space: nowrap; }
  `],
})
export class WoodpeckerTileComponent {
  /** Das laufende Training (`activeWoodpeckerPlan`), `null` = keins. */
  readonly plan = input<WoodpeckerPlan | null>(null);

  readonly cycle = computed(() => {
    const p = this.plan();
    return p ? p.cycles[p.cycles.length - 1] ?? null : null;
  });

  /** Erledigter Anteil des Satzes für den Balken (0–100). */
  readonly donePercent = computed(() => {
    const p = this.plan(), c = this.cycle();
    return p && c && p.setSize > 0 ? Math.min(100, 100 * c.attempted / p.setSize) : 0;
  });

  left(): number { const c = this.cycle(); return c ? daysLeft(c.deadline) : 0; }
  overdue(): boolean { const c = this.cycle(); return !!c && new Date(c.deadline).getTime() < Date.now(); }
  /** Zieltage des nächsten Zyklus: halbiert, aufgerundet (wie im Backend und in der Übersicht). */
  nextDays(): number { const c = this.cycle(); return c ? Math.ceil(c.targetDays / 2) : 0; }
}
