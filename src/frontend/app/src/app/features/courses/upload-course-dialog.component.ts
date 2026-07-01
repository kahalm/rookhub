import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import { DISCORD_INVITE_URL } from '../../core/community';

export interface UploadCourseDialogResult {
  file: File;
  name: string;
}

@Component({
  selector: 'app-upload-course-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ 'courses.upload.title' | translate }}</h2>
    <mat-dialog-content>
      <div class="dialog-form">
        <p class="hint">{{ 'courses.upload.hint' | translate }}</p>
        <p class="note">
          {{ 'courses.upload.restriction' | translate }}
          <a [href]="discordUrl" target="_blank" rel="noopener noreferrer">Discord</a>.
        </p>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'courses.upload.nameLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" name="name" maxlength="200"
                 [placeholder]="'courses.upload.namePlaceholder' | translate">
        </mat-form-field>

        <input #fileInput type="file" accept=".pgn" hidden (change)="onFileSelected($event)">
        <button mat-stroked-button type="button" class="pick-btn" (click)="fileInput.click()">
          <mat-icon>upload_file</mat-icon>
          @if (file) { {{ file.name }} } @else { {{ 'courses.upload.choosePgn' | translate }} }
        </button>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="!file"
              (click)="submit()">
        {{ 'courses.upload.button' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 0.6rem; min-width: min(360px, 80vw); }
    mat-form-field { width: 100%; }
    .hint { margin: 0; font-size: 0.88rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .note { margin: 0; font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .note a { color: #5865F2; font-weight: 500; text-decoration: none; }
    .note a:hover { text-decoration: underline; }
    .pick-btn { align-self: flex-start; }
  `]
})
export class UploadCourseDialogComponent {
  name = '';
  file: File | null = null;
  readonly discordUrl = DISCORD_INVITE_URL;

  constructor(public dialogRef: MatDialogRef<UploadCourseDialogComponent, UploadCourseDialogResult>) {}

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file = input.files?.[0] ?? null;
  }

  submit(): void {
    if (!this.file) return;
    this.dialogRef.close({ file: this.file, name: this.name.trim() });
  }
}
