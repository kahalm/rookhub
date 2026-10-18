import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { PctPipe } from '../../shared/format/pct';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CourseService } from '../courses/course.service';
import { WoodpeckerCreateDialogComponent, WoodpeckerDialogData, isWoodpeckerCourse } from './woodpecker-create-dialog.component';
import { daysLeft, WoodpeckerCycle, WoodpeckerPlan, WoodpeckerService } from './woodpecker.service';

/**
 * `/woodpecker`: eigene Woodpecker-Trainings. Je Training eine Karte mit dem laufenden Zyklus (Frist, Fortschritt),
 * den bisherigen Zyklen als kleine Tabelle und GENAU einer Hauptaktion: „Weiter lösen“ bzw. „Zyklus N starten“.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-woodpecker-list',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatMenuModule, MatProgressBarModule,
    TranslatePipe, PctPipe, HelpHintComponent, IconLabelDirective, LoadErrorComponent, LoadingSpinnerComponent],
  template: `
    <div class="page">
      <div class="header">
        <h1>{{ 'woodpecker.title' | translate }}</h1>
        <div class="header-actions">
          @if (plans().length > 0) {
            <button mat-stroked-button (click)="create()"><mat-icon>add</mat-icon>{{ 'woodpecker.new' | translate }}</button>
          }
        </div>
      </div>
      @let intro = 'woodpecker.intro' | translate;
      <!-- Letztes Wort + (?) untrennbar: ein geschütztes Leerzeichen vor dem Inline-Block-Hinweis hält in Chrome NICHT (Tablet brach das (?) allein um). -->
      <p class="intro">{{ introHead(intro) }}<span class="intro-tail">{{ introTail(intro) }}&nbsp;<app-help-hint [text]="'woodpecker.rules' | translate" /></span></p>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (loadFailed()) {
        <app-load-error (retry)="load()" />
      } @else if (plans().length === 0) {
        <!-- d3-rk-woodpecker-leerdialog-luecke-abbrechen: ohne lösbaren Kurs gleich der einzige Weg weiter („Zu den
             Kursen“) statt „Neues Training“ → Dialog → anderer Knopf. Solange die Kursliste lädt oder scheitert, bleibt
             „Neues Training“ (der Dialog meldet den Fehler selbst). -->
        <div class="empty-state">
          @if (hasCourses() === false) {
            <p>{{ 'woodpecker.create.noCourses' | translate }}</p>
            <a mat-flat-button color="primary" routerLink="/courses">{{ 'woodpecker.create.toCourses' | translate }}</a>
          } @else {
            <p>{{ 'woodpecker.empty' | translate }}</p>
            <button mat-flat-button color="primary" (click)="create()"><mat-icon>add</mat-icon>{{ 'woodpecker.new' | translate }}</button>
          }
        </div>
      } @else {
        <div class="plans">
          @for (p of plans(); track p.id) {
            @let c = current(p);
            <mat-card appearance="outlined" class="plan">
              <div class="plan-head">
                <div class="plan-title">
                  <h2 [title]="p.name">{{ p.name }}</h2>
                  <div class="meta">
                    <span class="chip" [class.done]="p.status === 'completed'">{{ ('woodpecker.status.' + p.status) | translate }}</span>
                    <span>{{ 'woodpecker.cycleOf' | translate: { n: c.number, max: p.maxCycles } }}</span>
                    <span class="sep">{{ (c.targetDays === 1 ? 'woodpecker.targetOne' : 'woodpecker.target') | translate: { days: c.targetDays } }}</span>
                  </div>
                </div>
                <button mat-icon-button [matMenuTriggerFor]="menu" [appIconLabel]="'common.moreActions' | translate"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #menu="matMenu">
                  @if (p.status === 'solving' && c.number === 1) {
                    <button mat-menu-item (click)="changeStart(p)"><mat-icon>event</mat-icon>{{ 'woodpecker.changeStart' | translate }}</button>
                    @if (p.setSize > 0) {
                      <button mat-menu-item (click)="finishSet(p)"><mat-icon>flag</mat-icon>{{ 'woodpecker.finishSet' | translate }}</button>
                    }
                  }
                  <button mat-menu-item class="delete-item" (click)="remove(p)"><mat-icon>delete</mat-icon>{{ 'common.delete' | translate }}</button>
                </mat-menu>
              </div>

              <div class="state">
                @switch (p.status) {
                  @case ('solving') {
                    <!-- d3-rk-woodpecker-balken-zeit-statt-satz: Zyklus 1 misst der Balken die verstrichene Frist — er steht
                         deshalb direkt unter der Fristzeile (mit Beschriftung), nicht unter „Satz: 0 Aufgaben“. -->
                    @if (c.number === 1) {
                      <p>{{ (p.setSize === 1 ? 'woodpecker.setGrowingOne' : 'woodpecker.setGrowing') | translate: { n: p.setSize, total: p.availableCount } }}</p>
                    } @else {
                      <p>{{ 'woodpecker.cycleProgress' | translate: { done: c.attempted, total: p.setSize } }}</p>
                      <mat-progress-bar mode="determinate" [value]="p.setSize ? 100 * c.attempted / p.setSize : 0" />
                    }
                    <p class="deadline" [class.over]="overdue(c)">
                      {{ (overdue(c) ? 'woodpecker.overdue' : (left(c) === 1 ? 'woodpecker.daysLeftOne' : 'woodpecker.daysLeft')) | translate: { days: left(c) } }}
                      · {{ c.deadline | date: 'short' }}
                    </p>
                    @if (c.number === 1) {
                      <mat-progress-bar class="time-bar" mode="determinate" [value]="timePercent(c)"
                        [attr.aria-label]="'woodpecker.timeBar' | translate: { used: elapsedDays(c), total: c.targetDays }" />
                    }
                  }
                  @case ('break') {
                    <p>{{ 'woodpecker.breakUntil' | translate: { n: c.number + 1, at: (p.nextCycleAt | date: 'short') } }}</p>
                  }
                  @case ('ready') {
                    <p>{{ (nextDays(c) === 1 ? 'woodpecker.readyOne' : 'woodpecker.ready') | translate: { n: c.number + 1, days: nextDays(c), size: p.setSize } }}</p>
                  }
                  @case ('completed') {
                    <p>{{ 'woodpecker.completedAt' | translate: { at: (p.completedAt | date: 'mediumDate') } }}</p>
                  }
                }
              </div>

              @if (hasFinishedCycle(p)) {
                <div class="cycles-wrap">
                  <table class="cycles">
                    <thead><tr>
                      <th>{{ 'woodpecker.col.cycle' | translate }}</th>
                      <th class="num">{{ 'woodpecker.col.puzzles' | translate }}</th>
                      <th class="num">{{ 'woodpecker.col.solved' | translate }}</th>
                      <th class="num">{{ 'woodpecker.col.time' | translate }}</th>
                      <th class="num">{{ 'woodpecker.col.days' | translate }}</th>
                    </tr></thead>
                    <tbody>
                      @for (cy of p.cycles; track cy.number) {
                        <tr>
                          <td>{{ cy.number }}</td>
                          <td class="num">{{ cy.attempted }}</td>
                          <td class="num">{{ cy.attempted ? (cy.solved / cy.attempted | pct) : '–' }}</td>
                          <td class="num">{{ clock(cy.timeSeconds) }}</td>
                          <td class="num">{{ usedDays(cy) }} / {{ cy.targetDays }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }

              <div class="actions">
                @switch (p.status) {
                  @case ('solving') {
                    <a mat-flat-button color="primary" [routerLink]="['/woodpecker', p.id, 'solve']"><mat-icon>play_arrow</mat-icon>{{ 'woodpecker.continue' | translate }}</a>
                  }
                  @case ('ready') {
                    <button mat-flat-button color="primary" [disabled]="busy() === p.id" (click)="startNext(p)"><mat-icon>replay</mat-icon>{{ 'woodpecker.startCycle' | translate: { n: c.number + 1 } }}</button>
                  }
                  @case ('break') {
                    <div class="locked">
                      <button mat-flat-button color="primary" disabled><mat-icon>replay</mat-icon>{{ 'woodpecker.startCycle' | translate: { n: c.number + 1 } }}</button>
                      <span class="reason">{{ 'woodpecker.breakReason' | translate }}</span>
                    </div>
                  }
                }
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    /* c1-wp-woodpecker-seitenpolster-96vw: ohne 96vw — das zog am Handy/Tablet zusätzlich 2 vw je Seite ab (24/32 px
       statt 16 px, DESIGN 3.2); den Rand gibt das Polster. */
    .page { max-width: var(--page-max-width); margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .header h1 { margin: 0; }
    .header-actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    /* c1-wp-intro-hilfe-eigene-zeile: (?) steht im Satz (inline-flex, Abstand = das Leerzeichen im Markup; DESIGN 5.11),
       nicht als eigener Flex-Eintrag, der am Handy allein in eine Zeile rutscht. */
    .intro { margin: 8px 0 16px; }
    .intro-tail { white-space: nowrap; }
    .empty-state { text-align: center; padding: 2rem 1rem; }
    .plans { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 360px), 1fr)); gap: 16px; }
    .plan { display: flex; flex-direction: column; padding: 16px; gap: 8px; }
    .plan-head { display: flex; align-items: flex-start; gap: 8px; }
    .plan-title { flex: 1 1 auto; min-width: 0; }
    .plan-title h2 { margin: 0; font-size: 1.1rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .plan-head button { flex: none; }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .sep::before { content: '· '; }
    .chip { font-size: .72rem; padding: 1px 7px; border-radius: 999px; background: color-mix(in srgb, currentColor 10%, transparent); }
    .chip.done { color: var(--rh-success); }
    .state p { margin: 4px 0; }
    .deadline { font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .time-bar { margin-bottom: 4px; }
    .deadline.over { color: var(--rh-warn); }
    .cycles-wrap { overflow-x: auto; }
    .cycles { width: 100%; border-collapse: collapse; font-size: .85rem; }
    .cycles th, .cycles td { padding: 4px 6px; text-align: left; white-space: nowrap; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .cycles th { font-weight: 500; color: var(--mat-sys-on-surface-variant); }
    .cycles .num { text-align: right; font-variant-numeric: tabular-nums; }
    .actions { margin-top: auto; display: flex; gap: 8px; align-items: center; }
    .locked { display: flex; flex-direction: column; gap: 4px; max-width: 280px; }
    .reason { font-size: .8rem; color: var(--mat-sys-on-surface-variant); }
    /* c1-wp-uebersicht-ultrawide: Kartenraster nimmt die Breitbild-Stufe (DESIGN 4.1); darunter unverändert. */
    @media (min-width: 2400px) {
      .page { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
    }
  `],
})
export class WoodpeckerListComponent implements OnInit {
  /** Einleitung ohne ihr letztes Wort (das hängt mit dem (?) in .intro-tail). */
  introHead(text: string): string { const i = text.trimEnd().lastIndexOf(' '); return i < 0 ? '' : text.slice(0, i + 1); }
  /** Letztes Wort der Einleitung. */
  introTail(text: string): string { const t = text.trimEnd(); const i = t.lastIndexOf(' '); return i < 0 ? t : t.slice(i + 1); }

  private service = inject(WoodpeckerService);
  private dialog = inject(MatDialog);
  private confirm = inject(ConfirmService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private courses = inject(CourseService);

  readonly plans = signal<WoodpeckerPlan[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  /** Lösbare Kurse vorhanden? null = (noch) unbekannt; nur geprüft, solange es kein Training gibt. */
  readonly hasCourses = signal<boolean | null>(null);
  /** Plan, für den gerade eine Aktion läuft. */
  readonly busy = signal<number | null>(null);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service.list().subscribe({
      next: list => {
        this.plans.set(list);
        this.loading.set(false);
        if (list.length === 0) this.checkCourses();
      },
      error: () => { this.loading.set(false); this.loadFailed.set(true); },
    });
  }

  /** Gibt es überhaupt einen lösbaren Kurs? Dieselbe Auswahl wie im Dialog (keine Kalkulation, mindestens 1 Aufgabe). */
  private checkCourses(): void {
    this.courses.getCourses().subscribe({
      next: list => this.hasCourses.set(list.some(isWoodpeckerCourse)),
      error: () => this.hasCourses.set(null),   // unbekannt → „Neues Training“ wie bisher
    });
  }

  current(p: WoodpeckerPlan): WoodpeckerCycle { return p.cycles[p.cycles.length - 1]; }
  hasFinishedCycle(p: WoodpeckerPlan): boolean { return p.cycles.some(c => c.finishedAt != null); }
  left(c: WoodpeckerCycle): number { return daysLeft(c.deadline); }
  overdue(c: WoodpeckerCycle): boolean { return new Date(c.deadline).getTime() < Date.now(); }
  /** Zieltage des nächsten Zyklus: halbiert, aufgerundet (wie im Backend). */
  nextDays(c: WoodpeckerCycle): number { return Math.ceil(c.targetDays / 2); }
  clock(seconds: number): string { return formatSecondsClock(seconds); }

  /** Anteil der verstrichenen Frist in Prozent (Zyklus 1: der Satz wächst, die Zeit läuft). */
  timePercent(c: WoodpeckerCycle): number {
    const start = new Date(c.startedAt).getTime(), end = new Date(c.deadline).getTime();
    return Math.min(100, Math.max(0, 100 * (Date.now() - start) / (end - start)));
  }

  /** Verstrichene volle Tage des laufenden Zyklus (für die Beschriftung des Zeitbalkens), höchstens die Frist. */
  elapsedDays(c: WoodpeckerCycle): number {
    return Math.min(c.targetDays, Math.max(0, Math.floor((Date.now() - new Date(c.startedAt).getTime()) / 86_400_000)));
  }

  /** Gebrauchte Tage eines Zyklus (aufgerundet; laufend = bis jetzt). */
  usedDays(c: WoodpeckerCycle): number {
    const end = c.finishedAt ? new Date(c.finishedAt).getTime() : Date.now();
    return Math.max(1, Math.ceil((end - new Date(c.startedAt).getTime()) / 86_400_000));
  }

  create(): void {
    const data: WoodpeckerDialogData = { mode: 'create' };
    this.dialog.open(WoodpeckerCreateDialogComponent, { data, width: '560px', maxWidth: '94vw', autoFocus: 'dialog' })
      .afterClosed().subscribe((plan: WoodpeckerPlan | null | undefined) => {
        if (plan) this.plans.update(list => [plan, ...list]);
      });
  }

  changeStart(p: WoodpeckerPlan): void {
    const data: WoodpeckerDialogData = { mode: 'start', plan: p };
    this.dialog.open(WoodpeckerCreateDialogComponent, { data, width: '440px', maxWidth: '94vw', autoFocus: 'dialog' })
      .afterClosed().subscribe((plan: WoodpeckerPlan | null | undefined) => { if (plan) this.replace(plan); });
  }

  finishSet(p: WoodpeckerPlan): void {
    this.confirm.ask('woodpecker.finishSetConfirm', { n: p.setSize }).subscribe(ok => {
      if (ok) this.run(p, this.service.finishSet(p.id));
    });
  }

  startNext(p: WoodpeckerPlan): void { this.run(p, this.service.startNextCycle(p.id)); }

  remove(p: WoodpeckerPlan): void {
    this.confirm.ask('woodpecker.deleteConfirm', { name: p.name }).subscribe(ok => {
      if (!ok) return;
      this.service.delete(p.id).subscribe({
        next: () => this.plans.update(list => list.filter(x => x.id !== p.id)),
        error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'woodpecker.saveFailed')),
      });
    });
  }

  private run(p: WoodpeckerPlan, call: ReturnType<WoodpeckerService['get']>): void {
    this.busy.set(p.id);
    call.subscribe({
      next: plan => { this.busy.set(null); this.replace(plan); },
      error: err => { this.busy.set(null); this.snackbar.warn(apiErrorText(err, this.translate, 'woodpecker.saveFailed')); },
    });
  }

  private replace(plan: WoodpeckerPlan): void {
    this.plans.update(list => list.map(x => x.id === plan.id ? plan : x));
  }
}
