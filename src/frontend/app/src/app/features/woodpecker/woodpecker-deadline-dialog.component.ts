import { ChangeDetectionStrategy, Component, computed, inject, Injectable, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { map, Observable, of } from 'rxjs';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { WOODPECKER_MAX_EXTENSION_DAYS, WoodpeckerCycle, WoodpeckerPlan, WoodpeckerService } from './woodpecker.service';

export interface WoodpeckerDeadlineDialogData { plan: WoodpeckerPlan; }

/**
 * „Zeit ist um – verlängern oder abschließen?“ (1.007.0, Nutzerwunsch 22.11.2026: „wenn ein Woodpecker-Zyklus fertig
 * ist, ich aber nicht alles gemacht habe, soll er fragen, ob er es extenden soll oder ob ich den Zyklus abschließen
 * will“). Erscheint, wenn der Server `deadlinePassed` meldet — beim Öffnen der Übersicht und des Lösers
 * ({@link WoodpeckerDeadlinePrompt}), und über den Knopf auf der Karte.
 *
 * - **Verlängern** (Hauptknopf): Tage vorbelegt mit dem Vorschlag des Servers (offene Aufgaben im bisherigen Tempo).
 * - **Abschließen**: offene Aufgaben bleiben unbearbeitet; danach Pause und der nächste Zyklus in halber Zeit.
 * - **Später**: nichts gespeichert, in dieser Sitzung nicht noch einmal von selbst gefragt.
 *
 * Gespeichert wird erst mit dem Knopf; scheitert es (offline, Fehler), bleibt das Fenster mit der Wahl offen
 * (`warn`-Snackbar, DESIGN 5.6). Ergebnis von `afterClosed()`: der neue Plan, sonst `undefined`.
 */
@Component({
  selector: 'app-woodpecker-deadline-dialog',
  imports: [DatePipe, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'woodpecker.deadline.title' | translate: { n: cycle.number } }}</h2>
    <mat-dialog-content class="wdl-content">
      <p class="wdl-name">{{ plan.name }}</p>
      <p class="wdl-intro">
        @if (cycle.number === 1) {
          {{ 'woodpecker.deadline.introSet' | translate: { at: (cycle.deadline | date: 'short'), n: plan.setSize, open: open } }}
        } @else {
          {{ 'woodpecker.deadline.intro' | translate: { at: (cycle.deadline | date: 'short'), done: cycle.attempted, total: plan.setSize, open: open } }}
        }
      </p>

      <h3 class="wdl-head">{{ 'woodpecker.deadline.extendHead' | translate }}</h3>
      <mat-form-field appearance="outline" class="wdl-days" subscriptSizing="dynamic">
        <mat-label>{{ 'woodpecker.deadline.days' | translate }}</mat-label>
        <input matInput type="number" min="1" [max]="maxDays" step="1" [ngModel]="days()" (ngModelChange)="days.set($event)"
          [disabled]="busy()" />
        <mat-hint>
          @if (daysValid()) {
            {{ 'woodpecker.deadline.newDeadline' | translate: { at: (newDeadline() | date: 'short') } }}
          } @else {
            {{ 'woodpecker.deadline.daysRange' | translate: { max: maxDays } }}
          }
        </mat-hint>
      </mat-form-field>
      @if (plan.suggestedExtensionDays) {
        <p class="hint">{{ 'woodpecker.deadline.suggestion' | translate: { days: plan.suggestedExtensionDays } }}</p>
      }

      <h3 class="wdl-head">{{ 'woodpecker.deadline.finishHead' | translate }}</h3>
      <p class="hint">
        @if (cycle.number >= plan.maxCycles) {
          {{ 'woodpecker.deadline.finishLast' | translate }}
        } @else if (cycle.number === 1) {
          {{ 'woodpecker.deadline.finishSet' | translate: { n: plan.setSize, next: cycle.number + 1, days: nextDays } }}
        } @else {
          {{ 'woodpecker.deadline.finishHint' | translate: { next: cycle.number + 1, days: nextDays } }}
        }
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" class="wdl-later" [disabled]="busy()" (click)="ref.close()">{{ 'woodpecker.deadline.later' | translate }}</button>
      <button mat-stroked-button type="button" class="wdl-finish" [disabled]="busy()" (click)="finish()">{{ 'woodpecker.deadline.finish' | translate }}</button>
      <button mat-flat-button color="primary" type="button" class="wdl-extend" [disabled]="busy() || !daysValid()" (click)="extend()">
        {{ 'woodpecker.deadline.extend' | translate: { days: daysValid() ? days() : '–' } }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .wdl-content { display: flex; flex-direction: column; }
    .wdl-name { margin: 0 0 8px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .wdl-intro { margin: 0 0 8px; }
    .wdl-head { margin: 12px 0 8px; font-size: 1rem; font-weight: 500; }
    .wdl-days { width: 160px; max-width: 100%; }
    .hint { margin: 4px 0 0; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
  `],
})
export class WoodpeckerDeadlineDialogComponent {
  private readonly data = inject<WoodpeckerDeadlineDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<WoodpeckerDeadlineDialogComponent, WoodpeckerPlan | undefined>>(MatDialogRef);
  private readonly service = inject(WoodpeckerService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  readonly plan = this.data.plan;
  readonly cycle: WoodpeckerCycle = this.plan.cycles[this.plan.cycles.length - 1];
  readonly maxDays = WOODPECKER_MAX_EXTENSION_DAYS;
  /** Noch offen: Zyklus 1 = Aufgaben der Kurse, die noch nicht im Satz sind; sonst Satz minus bearbeitet. */
  readonly open = Math.max(0, this.cycle.number === 1 ? this.plan.availableCount - this.plan.setSize : this.plan.setSize - this.cycle.attempted);
  /** Zieldauer des nächsten Zyklus: halbiert, aufgerundet (wie im Backend). */
  readonly nextDays = Math.ceil(this.cycle.targetDays / 2);

  readonly days = signal<number>(Math.min(this.maxDays, Math.max(1, this.plan.suggestedExtensionDays ?? this.cycle.targetDays)));
  readonly busy = signal(false);
  readonly daysValid = computed(() => { const d = Number(this.days()); return Number.isInteger(d) && d >= 1 && d <= this.maxDays; });
  /** Vorschau der neuen Frist: wie im Backend ab der späteren von jetzt und bisheriger Frist. */
  readonly newDeadline = computed(() => {
    const from = Math.max(Date.now(), new Date(this.cycle.deadline).getTime());
    return new Date(from + Number(this.days()) * 86_400_000);
  });

  extend(): void {
    if (!this.daysValid()) return;
    this.save(() => this.service.extendCycle(this.plan.id, Number(this.days())), 'woodpecker.deadline.extended');
  }

  finish(): void { this.save(() => this.service.finishCycle(this.plan.id), 'woodpecker.deadline.finished'); }

  private save(call: () => Observable<WoodpeckerPlan>, doneKey: string): void {
    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      this.snackbar.warn(this.translate.instant('woodpecker.deadline.offline'));
      return;
    }
    this.busy.set(true);
    call().subscribe({
      next: plan => { this.busy.set(false); this.snackbar.success(this.translate.instant(doneKey)); this.ref.close(plan); },
      error: err => { this.busy.set(false); this.snackbar.warn(apiErrorText(err, this.translate, 'woodpecker.saveFailed')); },
    });
  }
}

/**
 * Fragt nach Ablauf der Frist (`deadlinePassed`) EINMAL je Sitzung, Plan und Frist von selbst — „Später“ heißt also:
 * bis zum nächsten Laden der App nicht wieder. Die Entscheidung selbst steht auf dem Server (verlängert → neue Frist,
 * abgeschlossen → Zyklus beendet), danach meldet der Plan kein `deadlinePassed` mehr und es wird nicht mehr gefragt.
 */
@Injectable({ providedIn: 'root' })
export class WoodpeckerDeadlinePrompt {
  private readonly dialog = inject(MatDialog);
  private readonly asked = new Set<string>();

  private key(p: WoodpeckerPlan): string {
    const c = p.cycles[p.cycles.length - 1];
    return `${p.id}:${c?.number}:${c?.deadline}`;
  }

  /** Muss für diesen Plan von selbst gefragt werden? */
  shouldAsk(plan: WoodpeckerPlan | null | undefined): boolean {
    return !!plan && plan.status === 'solving' && plan.deadlinePassed === true && !this.asked.has(this.key(plan));
  }

  /** Von selbst fragen, falls nötig; sonst sofort `null`. Ergebnis: neuer Plan nach einer Entscheidung, sonst `null`. */
  askIfDue(plan: WoodpeckerPlan | null | undefined): Observable<WoodpeckerPlan | null> {
    if (!plan || !this.shouldAsk(plan)) return of(null);
    return this.open(plan);
  }

  /** Fenster öffnen (auch auf Knopfdruck); zählt als gefragt. */
  open(plan: WoodpeckerPlan): Observable<WoodpeckerPlan | null> {
    this.asked.add(this.key(plan));
    return this.dialog.open<WoodpeckerDeadlineDialogComponent, WoodpeckerDeadlineDialogData, WoodpeckerPlan | undefined>(
      WoodpeckerDeadlineDialogComponent, { data: { plan }, width: '440px', maxWidth: '94vw', autoFocus: 'dialog' },
    ).afterClosed().pipe(map(p => p ?? null));
  }
}
