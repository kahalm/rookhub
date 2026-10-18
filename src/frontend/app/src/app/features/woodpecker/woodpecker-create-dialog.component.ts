import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
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

/** Kurs mit lösbaren Aufgaben — Kalkulationsbücher haben keine Lösung. EINE Regel für Dialog und Übersicht. */
export function isWoodpeckerCourse(c: CourseListItem): boolean {
  return !c.isCalculation && c.puzzleCount > 0;
}

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
  imports: [DatePipe, DecimalPipe, RouterLink, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule,
    TranslatePipe, IsoDateFieldComponent, LoadErrorComponent, LoadingSpinnerComponent],
  template: `
    <h2 mat-dialog-title>{{ (data.mode === 'create' ? 'woodpecker.create.title' : 'woodpecker.start.title') | translate }}</h2>
    <!-- c1-wp-dialog-kasten-im-kasten: EIN Scrollbereich (mat-dialog-content) mit Weiterscroll-Schatten; der Beginn
         steht vorn, damit Feld und Hinweis nie unter einer halb angeschnittenen Kurszeile verschwinden. -->
    <mat-dialog-content class="wp-content">
      @if (data.mode === 'create') {
        @if (loading()) {
          <app-loading-spinner />
        } @else if (loadFailed()) {
          <app-load-error (retry)="load()" />
        } @else if (noCourses()) {
          <!-- c1-wp-dialog-ohne-kurs-sackgasse: Leersatz + genau ein Weg weiter, kein unerfüllbares Formular. -->
          <div class="empty-state">
            <p>{{ 'woodpecker.create.noCourses' | translate }}</p>
            <a mat-flat-button color="primary" routerLink="/courses" mat-dialog-close>{{ 'woodpecker.create.toCourses' | translate }}</a>
          </div>
        }
      }
      @if (showForm()) {
        <app-iso-date-field class="date" [label]="'woodpecker.create.start' | translate"
          [value]="startYmd()" (valueChange)="startYmd.set($event)" [max]="today" />
        <p class="hint">{{ 'woodpecker.create.startHint' | translate }}</p>
      }
      @if (data.mode === 'create' && showForm()) {
        <p class="hint courses-hint">{{ 'woodpecker.create.coursesHint' | translate }}</p>
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
              <span class="c-count">{{ (c.puzzleCount === 1 ? 'woodpecker.puzzlesOne' : 'woodpecker.puzzles') | translate: { count: (c.puzzleCount | number) } }}</span>
              <!-- d3-rk-woodpecker-kursliste-doppelte-namen: nur bei gleichnamigen Kursen eine leise Zeile, die sie
                   unterscheidet (Herkunft, zuletzt geübt). -->
              @if (dupNames().has(c.displayName)) {
                <span class="c-meta">{{ originKey(c) | translate: { name: c.sharedByUsername } }}@if (c.lastActivityAt) {<span class="c-meta-sep">{{ 'woodpecker.create.metaLast' | translate: { date: (c.lastActivityAt | date: 'mediumDate') } }}</span>}</span>
              }
            </mat-checkbox>
          }
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <!-- d3-rk-woodpecker-leerdialog-luecke-abbrechen: ohne Kurs läuft kein Vorgang → „Schließen“ statt „Abbrechen“. -->
      <button mat-button mat-dialog-close [disabled]="busy()">{{ (noCourses() ? 'common.close' : 'common.cancel') | translate }}</button>
      @if (showForm()) {
        <button mat-flat-button color="primary" (click)="save()" [disabled]="!canSave()">
          {{ (data.mode === 'create' ? 'woodpecker.create.submit' : 'common.save') | translate }}
        </button>
      }
    </mat-dialog-actions>
    @if (data.mode === 'create' && showForm() && selected().length === 0) {
      <p class="reason">{{ 'woodpecker.create.chooseCourse' | translate }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .hint { margin: 0 0 8px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .courses-hint { margin-top: 8px; }
    .search { width: 100%; margin-bottom: 4px; }
    /* c1-wp-dialog-kasten-im-kasten: kein eigener Scrollkasten mehr — es scrollt nur mat-dialog-content. Weiterscroll-
       Schatten skriptlos wie die Repertoire-Linienliste (DESIGN 5.3): die Abdeckung (local) in Dialogfarbe wandert mit
       dem Inhaltsende, der Schatten (scroll) bleibt am unteren Rand — sichtbar nur, solange noch etwas kommt. */
    .wp-content {
      background:
        linear-gradient(to bottom, transparent, var(--mat-dialog-container-color, var(--mat-sys-surface)) 70%) center bottom / 100% 32px no-repeat local,
        radial-gradient(farthest-side at 50% 100%, color-mix(in srgb, currentColor 30%, transparent), transparent)
          center bottom / 100% 14px no-repeat scroll;
    }
    .courses { display: flex; flex-direction: column; }
    /* d3-rk-woodpecker-dialog-kurswahl-tippziel: jede Kurszeile ist ganzzeilig mindestens 44 px hoch und als Ganzes
       antippbar (DESIGN 2.5/7) — das Label füllt die Zeile. Material-Interna → ::ng-deep, auf diese Liste begrenzt. */
    .courses mat-checkbox { display: block; }
    :host ::ng-deep .courses .mdc-form-field { display: flex; width: 100%; min-height: 44px; align-items: center; }
    :host ::ng-deep .courses .mdc-label { flex: 1 1 auto; padding-block: 4px; }
    .c-name { margin-right: .5em; }
    .c-meta { display: block; font-size: .8em; color: var(--mat-sys-on-surface-variant); }
    .c-meta-sep::before { content: ' · '; }
    /* c1-wp-dialog-zahlformat: Zahl + Einheit bleiben zusammen (U+00A0 im Text, nowrap hier). */
    .c-count { font-size: .85em; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; white-space: nowrap; }
    .date { display: block; margin-top: 8px; }
    /* c1-wp-dialog-leersatz-blass-hell: im Dialog ist currentColor schon on-surface-variant — nicht ein zweites Mal
       abblassen (DESIGN 5.10; hell sonst ≈ 3,1:1). */
    .empty-state { color: var(--mat-sys-on-surface-variant); }
    /* d3-rk-woodpecker-leerdialog-luecke-abbrechen: mat-dialog-content polstert schon — die 2rem der globalen
       .empty-state rissen große Lücken über und unter dem Satz. */
    .wp-content .empty-state { padding: 8px 0 0; }
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
  /** Namen, die mehr als einmal vorkommen — nur dort steht die unterscheidende Meta-Zeile. */
  readonly dupNames = computed(() => {
    const seen = new Set<string>(), dup = new Set<string>();
    for (const c of this.courses()) (seen.has(c.displayName) ? dup : seen).add(c.displayName);
    return dup;
  });
  /** Kursliste geladen, aber kein lösbarer Kurs — dann nur Leersatz + Weg zu den Kursen. */
  readonly noCourses = computed(() => this.data.mode === 'create' && !this.loading() && !this.loadFailed() && this.courses().length === 0);
  /** Datum, Kursliste und „Training starten“ nur, wenn es etwas zu wählen gibt (Modus `start`: immer). */
  readonly showForm = computed(() => this.data.mode === 'start' || (!this.loading() && !this.loadFailed() && this.courses().length > 0));
  readonly canSave = computed(() => !this.busy() && !!this.startYmd() && this.startYmd() <= this.today
    && (this.data.mode === 'start' || this.selected().length > 0));

  ngOnInit(): void { if (this.data.mode === 'create') this.load(); }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.courseService.getCourses().subscribe({
      // Nur Kurse mit lösbaren Aufgaben — Kalkulationsbücher haben keine Lösung.
      next: list => {
        this.courses.set(list.filter(isWoodpeckerCourse));
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.loadFailed.set(true); },
    });
  }

  isSelected(bookId: number): boolean { return this.selected().includes(bookId); }

  /** Herkunft eines Kurses für die Meta-Zeile: von jemandem geteilt, eigener Kurs oder freigegeben. */
  originKey(c: CourseListItem): string {
    return c.isShared && c.sharedByUsername ? 'woodpecker.create.metaShared'
      : c.isOwned ? 'woodpecker.create.metaOwn' : 'woodpecker.create.metaPublic';
  }

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
