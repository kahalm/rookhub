import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe } from '@ngx-translate/core';

export interface CourseThemesDialogData {
  bookId: number;
  displayName: string;
  /** Aktuell gesetzte Theme-Keys (mind. ["tactics"]). */
  themes: string[];
}

/** Alle wählbaren Buch-Themen-Keys (= Backend-`ChessableTheme`-Keys). */
const ALL_THEMES = ['tactics', 'endgame', 'opening', 'middlegame', 'other'] as const;

/**
 * Multi-Select-Dialog für die Themen-Tags eines Kurs-Buchs (Admin/Besitzer). Gibt beim Speichern
 * die ausgewählten Keys zurück; leer = Rückfall auf Default „tactics" (serverseitig).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-course-themes-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'courses.themes.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ 'courses.themes.hint' | translate:{ name: data.displayName } }}</p>
      <div class="theme-list">
        @for (t of allThemes; track t) {
          <mat-checkbox [(ngModel)]="selected[t]">{{ ('trainingGoals.theme.' + t) | translate }}</mat-checkbox>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="close()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { margin: 0 0 12px; font-size: .9rem; opacity: .85; }
    .theme-list { display: flex; flex-direction: column; gap: 6px; }
  `],
})
export class CourseThemesDialogComponent {
  readonly allThemes = ALL_THEMES;
  selected: Record<string, boolean> = {};

  constructor(
    private ref: MatDialogRef<CourseThemesDialogComponent, string[] | undefined>,
    @Inject(MAT_DIALOG_DATA) public data: CourseThemesDialogData,
  ) {
    for (const t of ALL_THEMES) this.selected[t] = (data.themes ?? []).includes(t);
  }

  close(): void { this.ref.close(undefined); }

  save(): void {
    // Reihenfolge stabil nach ALL_THEMES; leere Auswahl ist erlaubt (Server → Default „tactics").
    this.ref.close(ALL_THEMES.filter(t => this.selected[t]));
  }
}
