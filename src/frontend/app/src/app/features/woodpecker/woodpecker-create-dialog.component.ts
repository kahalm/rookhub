import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { IsoDateFieldComponent } from '../../shared/date-field/iso-date-field.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CourseListItem, CourseService } from '../courses/course.service';
import { startOfLocalDayIso, todayYmd, WoodpeckerPlan, WoodpeckerService, ymdOf } from './woodpecker.service';

/** `create`: Kurse wählen + Start; `start`: nur den Start des laufenden Zyklus verschieben. */
export type WoodpeckerDialogData =
  | { mode: 'create' }
  | { mode: 'start'; plan: WoodpeckerPlan };

/**
 * Neues Woodpecker-Training: einen oder mehrere Kurse ankreuzen (Reihenfolge des Ankreuzens = Reihenfolge in
 * Zyklus 1) und den Beginn wählen — heute oder früher. Ein früherer Beginn übernimmt, was der Nutzer seit diesem Tag
 * in den Kursen gelöst hat. Alles andere (Fristen, Pause, Zyklenzahl) steht fest wie im Buch.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-woodpecker-create-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule,
    TranslatePipe, IsoDateFieldComponent, LoadErrorComponent, LoadingSpinnerComponent],
  template: `
    <h2 mat-dialog-title>{{ (data.mode === 'create' ? 'woodpecker.create.title' : 'woodpecker.start.title') | translate }}</h2>
    <mat-dialog-content>
      @if (data.mode === 'create') {
        <p class="hint">{{ 'woodpecker.create.coursesHint' | translate }}</p>
        @if (loading()) {
          <app-loading-spinner />
        } @else if (loadFailed()) {
          <app-load-error (retry)="load()" />
        } @else if (courses().length === 0) {
          <p class="empty-state">{{ 'woodpecker.create.noCourses' | translate }}</p>
        } @else {
          @if (courses().length > 8) {
            <mat-form-field appearance="outline" class="search" subscriptSizing="dynamic">
              <mat-label>{{ 'woodpecker.create.search' | translate }}</mat-label>
              <input matInput [value]="query()" (input)="query.set($any($event.target).value)" />
            </mat-form-field>
          }
          <div class="courses">
            @for (c of filtered(); track c.bookId) {
              <mat-checkbox [checked]="isSelected(c.bookId)" (change)="toggle(c.bookId, $event.checked)">
                <span class="c-name">{{ c.displayName }}</span>
                <span class="c-count">{{ (c.puzzleCount === 1 ? 'woodpecker.puzzlesOne' : 'woodpecker.puzzles') | translate: { count: c.puzzleCount } }}</span>
              </mat-checkbox>
            }
          </div>
        }
      }
      <app-iso-date-field class="date" [label]="'woodpecker.create.start' | translate"
        [value]="startYmd()" (valueChange)="startYmd.set($event)" [max]="today" />
      <p class="hint">{{ 'woodpecker.create.startHint' | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="busy()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="!canSave()">
        {{ (data.mode === 'create' ? 'woodpecker.create.submit' : 'common.save') | translate }}
      </button>
    </mat-dialog-actions>
    @if (data.mode === 'create' && selected().length === 0 && !loading()) {
      <p class="reason">{{ 'woodpecker.create.chooseCourse' | translate }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .hint { margin: 0 0 8px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .search { width: 100%; margin-bottom: 4px; }
    .courses { display: flex; flex-direction: column; max-height: 40vh; overflow-y: auto; margin-bottom: 12px; }
    .c-name { margin-right: .5em; }
    .c-count { font-size: .85em; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    .date { margin-top: 8px; }
    .empty-state { text-align: center; padding: 2rem 1rem; }
    .reason { margin: 0 24px 12px; text-align: right; font-size: .8rem; color: var(--mat-sys-on-surface-variant); }
  `],
})
export class WoodpeckerCreateDialogComponent implements OnInit {
  readonly data = inject<WoodpeckerDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<WoodpeckerCreateDialogComponent, WoodpeckerPlan | null>);
  private courseService = inject(CourseService);
  private woodpecker = inject(WoodpeckerService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly today = todayYmd();
  readonly startYmd = signal(this.data.mode === 'start' ? ymdOf(this.data.plan.cycles[this.data.plan.cycles.length - 1].startedAt) : this.today);
  readonly loading = signal(this.data.mode === 'create');
  readonly loadFailed = signal(false);
  readonly busy = signal(false);
  readonly courses = signal<CourseListItem[]>([]);
  readonly query = signal('');
  /** Gewählte Kurse in der Reihenfolge des Ankreuzens. */
  readonly selected = signal<number[]>([]);

  readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    return q ? this.courses().filter(c => c.displayName.toLowerCase().includes(q)) : this.courses();
  });
  readonly canSave = computed(() => !this.busy() && !!this.startYmd() && this.startYmd() <= this.today
    && (this.data.mode === 'start' || this.selected().length > 0));

  ngOnInit(): void { if (this.data.mode === 'create') this.load(); }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.courseService.getCourses().subscribe({
      // Nur Kurse mit lösbaren Aufgaben — Kalkulationsbücher haben keine Lösung.
      next: list => {
        this.courses.set(list.filter(c => !c.isCalculation && c.puzzleCount > 0));
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.loadFailed.set(true); },
    });
  }

  isSelected(bookId: number): boolean { return this.selected().includes(bookId); }

  toggle(bookId: number, checked: boolean): void {
    this.selected.update(s => checked ? [...s.filter(id => id !== bookId), bookId] : s.filter(id => id !== bookId));
  }

  save(): void {
    if (!this.canSave()) return;
    this.busy.set(true);
    // Beginn des gewählten Tages — auch bei „heute“, damit schon heute Gelöstes mitzählt.
    const start = startOfLocalDayIso(this.startYmd());
    const call = this.data.mode === 'create'
      ? this.woodpecker.create(this.selected(), start)
      : this.woodpecker.updateStart(this.data.plan.id, start);
    call.subscribe({
      next: plan => this.ref.close(plan),
      error: err => {
        this.busy.set(false);
        this.snackbar.warn(apiErrorText(err, this.translate, 'woodpecker.saveFailed'));
      },
    });
  }
}
