import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import {
  TrainingGoalService, ActivityPreset, ActivityPresetInput, ManualActivityKind,
  ActivityTheme, ACTIVITY_THEMES, TIMER_KINDS,
} from './training-goals.service';
import { activityKindIcon } from './activity-timer-tile.component';

/**
 * Karte „Timer-Vorlagen": wiederverwendbare Schnellstart-Vorlagen (Label + Art + Thema) für den
 * Dashboard-Aktivitäts-Timer, mit Anlegen/Bearbeiten/Löschen. Aus <c>TrainingGoalsComponent</c>
 * ausgegliedert; verwaltet die Vorlagenliste vollständig selbst (unabhängig vom Ziel-/Tracker-Stand).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-activity-presets-card',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe,
  ],
  templateUrl: './activity-presets-card.component.html',
  styles: [`
    .preset-form { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; align-items: start; }
    .preset-form .actions { display: flex; gap: 8px; align-items: center; margin-top: 4px; }
    .preset-list { list-style: none; padding: 0; margin: 12px 0 0; }
    .preset-list li { display: flex; align-items: center; gap: 10px; padding: 6px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .preset-list .p-icon { color: color-mix(in srgb, currentColor 55%, transparent); }
    .preset-list .p-label { font-weight: 600; }
    .preset-list .p-kind { font-size: .8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .preset-list .p-theme {
      font-size: .72rem; padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent);
      color: color-mix(in srgb, currentColor 75%, transparent);
    }
    .preset-list .p-actions { display: flex; gap: 2px; margin-left: auto; }
    .preset-empty { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; }
  `],
})
export class ActivityPresetsCardComponent implements OnInit {
  readonly timerKinds: ManualActivityKind[] = TIMER_KINDS;
  readonly activityThemes: ActivityTheme[] = ACTIVITY_THEMES;

  presets: ActivityPreset[] = [];
  savingPreset = false;
  editingPresetId: number | null = null;
  presetEdit: ActivityPresetInput = { label: '', kind: 'OfflineStudy', theme: null };

  constructor(
    private service: TrainingGoalService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.service.listPresets().subscribe({ next: p => this.presets = p, error: () => {} });
  }

  presetIcon(kind: ManualActivityKind): string { return activityKindIcon(kind); }

  editPreset(p: ActivityPreset): void {
    this.editingPresetId = p.id;
    this.presetEdit = { label: p.label, kind: p.kind, theme: p.theme ?? null };
  }

  cancelPresetEdit(): void {
    this.editingPresetId = null;
    this.presetEdit = { label: '', kind: 'OfflineStudy', theme: null };
  }

  savePreset(): void {
    const label = (this.presetEdit.label ?? '').trim();
    if (!label) return;
    this.savingPreset = true;
    const payload: ActivityPresetInput = { label, kind: this.presetEdit.kind, theme: this.presetEdit.theme ?? null };
    const req = this.editingPresetId
      ? this.service.updatePreset(this.editingPresetId, payload)
      : this.service.addPreset(payload);
    req.subscribe({
      next: saved => {
        // In-place aktualisieren / anhängen.
        const idx = this.presets.findIndex(p => p.id === saved.id);
        if (idx >= 0) this.presets[idx] = saved;
        else this.presets = [...this.presets, saved];
        this.savingPreset = false;
        this.cancelPresetEdit();
      },
      error: err => {
        this.savingPreset = false;
        this.snackbar.info(err?.error?.error ?? this.translate.instant('trainingGoals.presets.saveFailed'),
          { action: 'common.ok', duration: 3000 });
      },
    });
  }

  deletePreset(p: ActivityPreset): void {
    if (!confirm(this.translate.instant('trainingGoals.presets.deleteConfirm', { label: p.label }))) return;
    this.service.deletePreset(p.id).subscribe({
      next: () => {
        this.presets = this.presets.filter(x => x.id !== p.id);
        if (this.editingPresetId === p.id) this.cancelPresetEdit();
      },
      error: () => this.snackbar.info(this.translate.instant('trainingGoals.presets.deleteFailed'),
        { action: 'common.ok', duration: 3000 }),
    });
  }
}
