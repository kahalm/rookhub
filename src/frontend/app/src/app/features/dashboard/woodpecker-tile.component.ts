import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DatePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { activeWoodpeckerPlan, daysLeft, WoodpeckerPlan, WoodpeckerService } from '../woodpecker/woodpecker.service';
import { WoodpeckerDailyGoalComponent } from '../woodpecker/woodpecker-daily-goal.component';
import { WoodpeckerScheduleComponent } from '../woodpecker/woodpecker-schedule.component';
import { woodpeckerDailyGoal, woodpeckerSchedule } from '../woodpecker/woodpecker-stats.util';

/**
 * Inhalt der Dashboard-Kachel „Woodpecker“ (Kopf mit Titel/Untertitel baut das Dashboard wie bei jeder Kachel):
 * das laufende Training auf einen Blick — Name, Fortschritt des Zyklus, Frist — und genau EINE Aktion je Zustand
 * (DESIGN 2.1/8.1): `solving` → Tagesziel („Heute: 23 von 50“, erreicht mit grünem Haken; 0.993.0), darunter der
 * Zeitplan („Zeitplan: noch 12 bis 55 %“ bzw. Haken + „Im Zeitplan“; 0.994.0) und „Weiter
 * lösen“ (Kurs-Löser `/woodpecker/:id/solve`, wie die Übersicht), `ready` →
 * „Zyklus N starten“ startet den Zyklus DIREKT (`next-cycle`) und springt in den Löser (Nutzerentscheidung 10.11.,
 * 0.958.0), `break` → Pause-Satz + Textlink zur Übersicht. Ohne laufendes Training (nur im Anpassen-Modus sichtbar) ein
 * Satz und „Neues Training“.
 *
 * Ganze Kachel klickbar (DESIGN 5.2; die Kachel `rh-tile` ist die `mat-card` des Dashboards): die Fläche gehört
 * „Weiter lösen“ (`solving`) bzw. „Zur Übersicht“ (`break`). „Zyklus N starten“ ändert Daten ohne Rückfrage (der Zyklus
 * und seine Frist beginnen) — das löst nur der Knopf selbst aus; die Fläche führt dort wie der Name zur Übersicht.
 *
 * Den Aufruf macht die Kachel selbst (wie die Übersicht über `WoodpeckerService`); den neuen Stand meldet sie über
 * `planChange` ans Dashboard, das ihn als `plan` zurückgibt und zwischenspeichert. Scheitert der Start (z. B. die Pause
 * läuft doch noch), Snackbar mit dem Grund und Plan neu laden.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-woodpecker-tile',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, MatProgressSpinnerModule,
    TranslatePipe, WoodpeckerDailyGoalComponent, WoodpeckerScheduleComponent],
  template: `
    @if (plan(); as p) {
      @let c = cycle()!;
      <mat-card-content class="wp-content">
        <a class="wp-name" [class.rh-tile-target]="p.status === 'ready'" routerLink="/woodpecker" [title]="p.name">{{ p.name }}</a>
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
            <!-- Tagesziel (0.993.0): „Heute: 23 von 50“, erreicht mit grünem Haken — ohne die Statistik zu öffnen. -->
            @if (dailyGoal(); as g) {
              <p class="wp-daily"><app-woodpecker-daily-goal [goal]="g" /></p>
            }
            <!-- Zeitplan (0.994.0): Anteil des Satzes passend zum Tag des Zyklus — eigene Zeile direkt darunter, damit
                 zwei erreichte Ziele als zwei Haken untereinander stehen. -->
            @if (schedule(); as s) {
              <p class="wp-schedule"><app-woodpecker-schedule [schedule]="s" /></p>
            }
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
            <a mat-flat-button color="primary" class="wp-primary rh-tile-target" [routerLink]="['/woodpecker', p.id, 'solve']">
              <mat-icon>play_arrow</mat-icon>{{ 'woodpecker.continue' | translate }}
            </a>
          }
          @case ('ready') {
            <!-- DESIGN 5.10 „Knopf arbeitet“: gesperrt, Spinner statt Symbol, solange next-cycle läuft. -->
            <button mat-flat-button color="primary" class="wp-primary wp-start" [disabled]="starting()" (click)="startNext(p)">
              @if (starting()) {
                <mat-spinner diameter="16" class="wp-spin" />
              } @else {
                <mat-icon>replay</mat-icon>
              }
              {{ 'woodpecker.startCycle' | translate: { n: c.number + 1 } }}
            </button>
          }
          @default {
            <a mat-button class="wp-overview rh-tile-target" routerLink="/woodpecker">{{ 'woodpecker.toOverview' | translate }}</a>
          }
        }
      </mat-card-actions>
    } @else {
      <mat-card-content class="wp-content">
        <p class="wp-empty">{{ 'dashboard.woodpecker.none' | translate }}</p>
      </mat-card-content>
      <mat-card-actions>
        <a mat-button class="wp-create rh-tile-target" routerLink="/woodpecker">{{ 'woodpecker.new' | translate }}</a>
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
    /* Tagesziel: eigene Zeile, leise wie die Frist; der erreichte Zustand färbt sich im Baustein selbst. */
    .wp-daily, .wp-schedule { margin: 0; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .wp-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; margin: 0; font-size: 0.9rem; }
    mat-card-actions { margin-top: auto; flex-wrap: wrap; }
    mat-card-actions > a, mat-card-actions > button { min-width: 0; white-space: nowrap; }
    .wp-spin { display: inline-block; margin-right: 8px; vertical-align: middle; }
  `],
})
export class WoodpeckerTileComponent {
  /** Das laufende Training (`activeWoodpeckerPlan`), `null` = keins. */
  readonly plan = input<WoodpeckerPlan | null>(null);
  /** Neuer Stand nach einer Aktion (gestarteter Zyklus bzw. nach einem Fehler neu geladen; `null` = keins mehr aktiv). */
  readonly planChange = output<WoodpeckerPlan | null>();

  private service = inject(WoodpeckerService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  /** `next-cycle` läuft — Knopf gesperrt mit Spinner. */
  readonly starting = signal(false);

  readonly cycle = computed(() => {
    const p = this.plan();
    return p ? p.cycles[p.cycles.length - 1] ?? null : null;
  });

  /** Erledigter Anteil des Satzes für den Balken (0–100). */
  readonly donePercent = computed(() => {
    const p = this.plan(), c = this.cycle();
    return p && c && p.setSize > 0 ? Math.min(100, 100 * c.attempted / p.setSize) : 0;
  });

  /** Tagesziel des laufenden Zyklus (`woodpeckerDailyGoal`): heute bearbeitet gegen das Soll; `null` ohne laufenden
   *  Zyklus, nach der Frist oder wenn der Server `todayAttempted` (noch) nicht liefert. */
  readonly dailyGoal = computed(() => woodpeckerDailyGoal(this.plan()));

  /** Zeitplan des laufenden Zyklus (`woodpeckerSchedule`): bearbeiteter Anteil des Satzes gegen den Anteil, der zum
   *  heutigen Tag des Zyklus passt; `null` in Zyklus 1 oder ohne laufenden Zyklus. */
  readonly schedule = computed(() => woodpeckerSchedule(this.plan()));

  left(): number { const c = this.cycle(); return c ? daysLeft(c.deadline) : 0; }
  overdue(): boolean { const c = this.cycle(); return !!c && new Date(c.deadline).getTime() < Date.now(); }
  /** Zieltage des nächsten Zyklus: halbiert, aufgerundet (wie im Backend und in der Übersicht). */
  nextDays(): number { const c = this.cycle(); return c ? Math.ceil(c.targetDays / 2) : 0; }

  /** „Zyklus N starten“: Zyklus beginnen und in den Löser; Fehler → Snackbar (API-Grund), Plan neu laden. */
  startNext(p: WoodpeckerPlan): void {
    if (this.starting()) return;
    this.starting.set(true);
    this.service.startNextCycle(p.id).subscribe({
      next: plan => {
        this.starting.set(false);
        this.planChange.emit(plan);
        void this.router.navigate(['/woodpecker', plan.id, 'solve']);
      },
      error: err => {
        this.starting.set(false);
        this.snackbar.warn(apiErrorText(err, this.translate, 'woodpecker.saveFailed'));
        this.service.list().subscribe({
          next: plans => this.planChange.emit(activeWoodpeckerPlan(plans)),
          error: () => { /* Stand bleibt; das nächste Dashboard-Laden holt ihn */ },
        });
      },
    });
  }
}
