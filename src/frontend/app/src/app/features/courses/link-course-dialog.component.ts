import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CourseService } from './course.service';
import { SnackbarService } from '../../core/snackbar.service';

export interface LinkCourseCandidate { bookId: number; displayName: string; }
export interface LinkCourseDialogData {
  bookId: number;
  displayName: string;
  currentLinkedBookId: number | null;
  currentLinkedName: string | null;
  candidates: LinkCourseCandidate[];
}

/**
 * Dialog „Kurs verknüpfen" (Buch ↔ Workbook): zeigt die aktuelle Verknüpfung (mit Lösen-Knopf) und
 * lässt einen anderen eigenen/zugänglichen Kurs als Partner auswählen. Self-contained; schließt mit
 * `true`, wenn sich etwas geändert hat (Liste soll neu laden).
 */
@Component({
  selector: 'app-link-course-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatSelectModule, MatTooltipModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ 'courses.link.title' | translate:{ name: data.displayName } }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ 'courses.link.hint' | translate }}</p>

      @if (data.currentLinkedBookId) {
        <div class="current">
          <mat-icon>link</mat-icon>
          <span class="cur-name">{{ 'courses.link.currently' | translate:{ name: data.currentLinkedName || '' } }}</span>
          <button mat-icon-button class="unlink-btn" [disabled]="busy"
                  [matTooltip]="'courses.link.unlink' | translate" (click)="unlink()">
            <mat-icon>link_off</mat-icon>
          </button>
        </div>
      }

      @if (data.candidates.length === 0) {
        <p class="empty">{{ 'courses.link.noCandidates' | translate }}</p>
      } @else {
        <mat-form-field appearance="outline" class="picker">
          <mat-label>{{ 'courses.link.pick' | translate }}</mat-label>
          <mat-select [(ngModel)]="selected">
            @for (c of data.candidates; track c.bookId) {
              <mat-option [value]="c.bookId">{{ c.displayName }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(changed)">{{ 'common.close' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="!selected || busy" (click)="link()">
        {{ 'courses.link.linkButton' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { margin: 0 0 12px; font-size: 0.88rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .current { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; min-width: min(360px, 80vw); }
    .current mat-icon { color: var(--mat-sys-primary, #3f51b5); }
    .cur-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .picker { width: 100%; }
    .empty { font-style: italic; font-size: 0.85rem; color: color-mix(in srgb, currentColor 55%, transparent); margin: 0; }
  `]
})
export class LinkCourseDialogComponent {
  selected: number | null = null;
  busy = false;
  changed = false;

  constructor(
    public dialogRef: MatDialogRef<LinkCourseDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) public data: LinkCourseDialogData,
    private courseService: CourseService,
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  link(): void {
    if (!this.selected || this.busy) return;
    this.busy = true;
    this.courseService.linkCourse(this.data.bookId, this.selected).subscribe({
      next: () => {
        this.changed = true;
        this.snackbar.success(this.translate.instant('courses.link.linked'));
        this.dialogRef.close(true);
      },
      error: err => {
        this.busy = false;
        this.snackbar.info(err?.error?.message || this.translate.instant('courses.link.failed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  unlink(): void {
    if (this.busy) return;
    this.busy = true;
    this.courseService.unlinkCourse(this.data.bookId).subscribe({
      next: () => {
        this.changed = true;
        this.data.currentLinkedBookId = null;
        this.busy = false;
        this.snackbar.info(this.translate.instant('courses.link.unlinked'), { action: 'common.ok', duration: 2500 });
      },
      error: () => {
        this.busy = false;
        this.snackbar.info(this.translate.instant('courses.link.failed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }
}
