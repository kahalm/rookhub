import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { clampGoal } from './goal.util';
import { SnackbarService } from '../../core/snackbar.service';
import {
  TrainingGoalService, ManualActivity, ManualActivityInput, ManualActivityKind,
  ActivityTheme, ACTIVITY_THEMES,
} from './training-goals.service';
import { MANUAL_KINDS, isMinutesKind } from './manual-activity.util';

/**
 * Karte „Manuelle Offline-Aktivität eintragen": Formular (Art/Datum/Menge/Thema/Notiz) + Liste der
 * eigenen Einträge mit Bearbeiten/Löschen. Aus <c>TrainingGoalsComponent</c> ausgegliedert; delegiert
 * das CRUD an den <see cref="TrainingGoalService"/> und meldet Änderungen über <c>(changed)</c>, damit
 * der Eltern-Container Heute-/Tracker-Werte neu lädt.
 */
@Component({
  selector: 'app-manual-activities-card',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, TranslateModule,
  ],
  templateUrl: './manual-activities-card.component.html',
  styles: [`
    .muted { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; }
    .small { font-size: .8rem; }
    .actions { display: flex; gap: 8px; margin-top: 8px; }
    .manual-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin: 8px 0; }
    .manual-fields .note-field { grid-column: 1 / -1; }
    .manual-list { list-style: none; padding: 0; margin: 12px 0 0; }
    .manual-list li { display: flex; align-items: center; gap: 10px; padding: 6px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); font-size: .9rem; }
    .manual-list .m-date { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 65%, transparent); }
    .manual-list .m-kind { font-weight: 600; }
    .manual-list .m-amount { white-space: nowrap; font-variant-numeric: tabular-nums; }
    .manual-list .m-amount .unit { color: color-mix(in srgb, currentColor 50%, transparent); font-size: .8em; }
    .manual-list .m-note { flex: 1; color: color-mix(in srgb, currentColor 60%, transparent); overflow-wrap: anywhere; }
    .manual-list .m-actions { display: flex; gap: 2px; margin-left: auto; }
    .manual-list .m-theme {
      font-size: .72rem; padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent);
      color: color-mix(in srgb, currentColor 75%, transparent);
    }
  `],
})
export class ManualActivitiesCardComponent {
  /** Vom Eltern-Container geladene Liste (Quelle bleibt dort, damit Heute/Tracker konsistent bleiben). */
  @Input() manualList: ManualActivity[] = [];
  /** Feuert nach erfolgreichem Anlegen/Ändern/Löschen → Eltern lädt Fortschritt neu. */
  @Output() changed = new EventEmitter<void>();

  readonly manualKinds = MANUAL_KINDS;
  readonly activityThemes: ActivityTheme[] = ACTIVITY_THEMES;

  savingManual = false;
  editingManualId: number | null = null;
  manualEdit: ManualActivityInput = this.emptyManual();

  constructor(
    private service: TrainingGoalService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  /** Lokales Datum als yyyy-MM-dd (für date-Input + Default). */
  get todayDate(): string {
    const d = new Date();
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
  }

  /** Wird die aktuell gewählte Art in Minuten gemessen (sonst Partienzahl)? */
  get manualMinutes(): boolean { return isMinutesKind(this.manualEdit.kind); }
  isMinutes(kind: ManualActivityKind): boolean { return isMinutesKind(kind); }

  private emptyManual(): ManualActivityInput {
    return { kind: 'OtbGame', date: this.todayDate, amount: 1, note: '', theme: null };
  }

  saveManual(): void {
    const input: ManualActivityInput = {
      kind: this.manualEdit.kind,
      date: this.manualEdit.date || this.todayDate,
      amount: clampGoal(this.manualEdit.amount, this.manualMinutes ? 600 : 50) || 1,
      note: this.manualEdit.note?.trim() || null,
      // Themen-Zuordnung ist bei OtbGame zeitunwirksam → nicht mitspeichern.
      theme: this.manualMinutes ? (this.manualEdit.theme ?? null) : null,
    };
    this.savingManual = true;
    const req = this.editingManualId
      ? this.service.updateManual(this.editingManualId, input)
      : this.service.addManual(input);
    req.subscribe({
      next: () => {
        this.savingManual = false;
        this.snackbar.success(this.translate.instant('trainingGoals.manual.saved'));
        this.cancelManualEdit();
        this.changed.emit();
      },
      error: () => { this.savingManual = false; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }

  editManual(m: ManualActivity): void {
    this.editingManualId = m.id;
    this.manualEdit = { kind: m.kind, date: m.date, amount: m.amount, note: m.note ?? '', theme: m.theme ?? null };
  }

  cancelManualEdit(): void {
    this.editingManualId = null;
    this.manualEdit = this.emptyManual();
  }

  deleteManual(m: ManualActivity): void {
    this.service.deleteManual(m.id).subscribe({
      next: () => {
        if (this.editingManualId === m.id) this.cancelManualEdit();
        this.snackbar.success(this.translate.instant('trainingGoals.manual.deleted'));
        this.changed.emit();
      },
      error: () => this.snackbar.warn(this.translate.instant('trainingGoals.error')),
    });
  }

}
