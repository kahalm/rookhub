import { Component, ChangeDetectionStrategy, Inject, Optional } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { DISCORD_INVITE_URL } from '../../core/community';

/** Ergebnis des Dialogs: der Name steht immer, das PGN ist optional — ein Kurs darf leer
 *  entstehen und wird dann auf der Detailseite Kapitel für Kapitel gefüllt. */
export interface CreateCourseDialogResult {
  name: string;
  file: File | null;
}

/** Womit der Dialog geöffnet wurde. `games` = „eigene Partien importieren" (Datei PFLICHT),
 *  sonst der leere eigene Kurs, der danach Kapitel für Kapitel gefüllt wird. */
export interface CreateCourseDialogData {
  mode?: 'course' | 'games';
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-create-course-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (games ? 'courses.import.title' : 'courses.create.title') | translate }}</h2>
    <mat-dialog-content>
      <div class="dialog-form">
        <p class="hint">{{ (games ? 'courses.import.hint' : 'courses.create.hint') | translate }}</p>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'courses.create.nameLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" name="name" maxlength="200" required
                 [placeholder]="'courses.create.namePlaceholder' | translate">
        </mat-form-field>

        <input #fileInput type="file" accept=".pgn" hidden (change)="onFileSelected($event)">
        <div class="pgn-row">
          <button mat-stroked-button type="button" class="pick-btn" (click)="fileInput.click()">
            <mat-icon>attach_file</mat-icon>
            @if (file) { {{ file.name }} } @else {
              {{ (games ? 'courses.import.attachPgn' : 'courses.create.attachPgn') | translate }} }
          </button>
          @if (file) {
            <button mat-icon-button type="button" [attr.aria-label]="'common.delete' | translate"
                    (click)="clearFile(fileInput)"><mat-icon>close</mat-icon></button>
          }
        </div>
        <p class="note">
          {{ (games ? 'courses.import.pgnNote' : 'courses.create.pgnNote') | translate }}
          <a [href]="discordUrl" target="_blank" rel="noopener noreferrer">Discord</a>.
        </p>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="!canSubmit()"
              (click)="submit()">
        {{ (games ? 'courses.import.button' : 'courses.create.button') | translate }}
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
    .pgn-row { display: flex; align-items: center; gap: 0.25rem; }
  `]
})
export class CreateCourseDialogComponent {
  name = '';
  file: File | null = null;
  readonly discordUrl = DISCORD_INVITE_URL;
  /** „Partien importieren" statt „Kurs erstellen": ohne Datei gibt es hier nichts anzulegen. */
  readonly games: boolean;

  constructor(public dialogRef: MatDialogRef<CreateCourseDialogComponent, CreateCourseDialogResult>,
              @Optional() @Inject(MAT_DIALOG_DATA) data?: CreateCourseDialogData) {
    this.games = data?.mode === 'games';
  }

  canSubmit(): boolean {
    if (!this.name.trim()) return false;
    return !this.games || !!this.file;   // Partien-Import ohne PGN waere ein leerer Kurs
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file = input.files?.[0] ?? null;
    // Namen aus dem Dateinamen vorschlagen, solange keiner getippt wurde: wer eine Datei
    // waehlt, hat den Namen schon gesagt und soll ihn nicht zweimal eingeben.
    if (this.file && !this.name.trim()) {
      this.name = CreateCourseDialogComponent.nameFromFile(this.file.name);
    }
  }

  /** „meine-partien_2026.pgn" -> „meine partien 2026". */
  static nameFromFile(fileName: string): string {
    const base = fileName.replace(/\.[^.]+$/, '');
    return base.replace(/[_\-]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 200);
  }

  /** Angehängtes PGN wieder loswerden — auch im Input selbst, sonst löst dieselbe Datei
   *  beim erneuten Wählen kein change-Ereignis mehr aus. */
  clearFile(input: HTMLInputElement): void {
    this.file = null;
    input.value = '';
  }

  submit(): void {
    if (!this.canSubmit()) return;
    this.dialogRef.close({ name: this.name.trim(), file: this.file });
  }
}
