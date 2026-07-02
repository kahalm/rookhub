import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin, of } from 'rxjs';
import { RepertoireTrainingService, SrLevel } from './repertoire-training.service';

/**
 * Bearbeitet die 9 SR-Intervalle: entweder die globalen Nutzer-Defaults oder — bei aktivem Schalter
 * — einen pro-Repertoire-Override. Richtig → nächste Stufe, falsch → Stufe 1; das Intervall der
 * jeweiligen Stufe bestimmt, wann die Linie wieder fällig wird.
 */
@Component({
  selector: 'app-sr-config-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatInputModule, MatSelectModule, MatSlideToggleModule, MatProgressSpinnerModule, TranslateModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'srConfig.title' | translate }}</h2>
    <mat-dialog-content>
      @if (loading) {
        <div class="load"><mat-spinner diameter="32"></mat-spinner></div>
      } @else {
        <mat-slide-toggle [(ngModel)]="override" (change)="onToggle()">
          {{ 'srConfig.overrideForRepertoire' | translate }}
        </mat-slide-toggle>
        <p class="hint">{{ (override ? 'srConfig.hintRepertoire' : 'srConfig.hintGlobal') | translate }}</p>

        <div class="rows">
          @for (lvl of levels; track $index) {
            <div class="row">
              <span class="lv">{{ 'srConfig.level' | translate }} {{ $index + 1 }}</span>
              <mat-form-field appearance="outline" class="num" subscriptSizing="dynamic">
                <input matInput type="number" min="0.1" step="0.5" [(ngModel)]="lvl.value">
              </mat-form-field>
              <mat-form-field appearance="outline" class="unit" subscriptSizing="dynamic">
                <mat-select [(ngModel)]="lvl.unit">
                  <mat-option value="h">{{ 'srConfig.unit.h' | translate }}</mat-option>
                  <mat-option value="d">{{ 'srConfig.unit.d' | translate }}</mat-option>
                  <mat-option value="w">{{ 'srConfig.unit.w' | translate }}</mat-option>
                  <mat-option value="mo">{{ 'srConfig.unit.mo' | translate }}</mat-option>
                </mat-select>
              </mat-form-field>
            </div>
          }
        </div>
        <button mat-button class="reset" (click)="resetDefaults()">
          <mat-icon>restart_alt</mat-icon> {{ 'srConfig.resetDefaults' | translate }}
        </button>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="saving || loading">
        {{ 'common.save' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .load { display: flex; justify-content: center; padding: 24px; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); font-size: .85rem; margin: 6px 0 12px; }
    .rows { display: flex; flex-direction: column; gap: 6px; }
    .row { display: flex; align-items: center; gap: 10px; }
    .lv { width: 70px; font-size: .9rem; }
    .num { width: 90px; } .unit { width: 130px; }
    .reset { margin-top: 8px; }
  `],
})
export class SrConfigDialogComponent implements OnInit {
  loading = true;
  saving = false;
  override = false;
  levels: SrLevel[] = [];
  private userLevels: SrLevel[] = [];
  private hadOverride = false;
  private defaults: SrLevel[] = [];

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: { repertoireId: number },
    private training: RepertoireTrainingService,
    private ref: MatDialogRef<SrConfigDialogComponent>,
  ) {}

  ngOnInit(): void {
    this.training.getConfig(this.data.repertoireId).subscribe({
      next: cfg => {
        this.userLevels = cfg.user.map(l => ({ ...l }));
        this.defaults = cfg.effective.map(l => ({ ...l }));   // Fallback-Basis für „Standard"
        this.hadOverride = cfg.repertoire != null;
        this.override = this.hadOverride;
        this.levels = (cfg.repertoire ?? cfg.user).map(l => ({ ...l }));
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  onToggle(): void {
    // Beim Umschalten die passende Ausgangsbasis in die editierbaren Felder laden.
    this.levels = (this.override ? this.levels : this.userLevels).map(l => ({ ...l }));
  }

  resetDefaults(): void {
    this.levels = this.defaults.map(l => ({ ...l }));
  }

  save(): void {
    this.saving = true;
    const clean = this.levels.map(l => ({ value: Number(l.value) || 0, unit: l.unit }));
    if (this.override) {
      this.training.setRepertoireConfig(this.data.repertoireId, clean).subscribe({
        next: () => this.ref.close(true),
        error: () => { this.saving = false; },
      });
    } else {
      // Globale Defaults setzen; einen etwaigen Repertoire-Override aufheben.
      forkJoin({
        user: this.training.setUserConfig(clean),
        rep: this.hadOverride ? this.training.setRepertoireConfig(this.data.repertoireId, null) : of(void 0),
      }).subscribe({
        next: () => this.ref.close(true),
        error: () => { this.saving = false; },
      });
    }
  }
}
