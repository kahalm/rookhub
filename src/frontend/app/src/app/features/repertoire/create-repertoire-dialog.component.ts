import { Component, Inject, Optional } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule } from '@ngx-translate/core';
import { RepertoireKind } from '../../core/repertoire.types';
import { Repertoire } from '../../core/models';

@Component({
  selector: 'app-create-repertoire-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatCheckboxModule, MatSelectModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ (editMode ? 'repertoire.dialog.editTitle' : 'repertoire.dialog.title') | translate }}</h2>
    <mat-dialog-content>
      <form class="dialog-form">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'repertoire.dialog.name' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" name="name" required maxlength="200">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'repertoire.dialog.description' | translate }}</mat-label>
          <textarea matInput [(ngModel)]="description" name="description" rows="3" maxlength="1000"></textarea>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'repertoire.dialog.kind' | translate }}</mat-label>
          <mat-select [(ngModel)]="kind" name="kind">
            <mat-option [value]="0">{{ 'repertoire.kind.none' | translate }}</mat-option>
            <mat-option [value]="1">{{ 'repertoire.kind.opening' | translate }}</mat-option>
            <mat-option [value]="2">{{ 'repertoire.kind.middlegame' | translate }}</mat-option>
            <mat-option [value]="3">{{ 'repertoire.kind.endgame' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-checkbox [(ngModel)]="isPublic" name="isPublic">{{ 'repertoire.dialog.public' | translate }}</mat-checkbox>
        <mat-checkbox [(ngModel)]="useForExtension" name="useForExtension">{{ 'repertoire.dialog.useForExtension' | translate }}</mat-checkbox>
        <p class="ext-note">{{ 'repertoire.dialog.useForExtensionHint' | translate }}</p>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="!name" (click)="dialogRef.close({ name, description, isPublic, kind, useForExtension })">
        {{ (editMode ? 'repertoire.dialog.save' : 'repertoire.dialog.create') | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`.dialog-form { display: flex; flex-direction: column; gap: 0.5rem; min-width: min(300px, 78vw); } mat-form-field { width: 100%; }
    .ext-note { margin: 0 0 0 30px; font-size: 0.78rem; line-height: 1.3; color: color-mix(in srgb, currentColor 60%, transparent); }`]
})
export class CreateRepertoireDialogComponent {
  name = '';
  description = '';
  isPublic = false;
  kind: RepertoireKind = 0;
  /** Default true: neue Repertoires werden standardmaessig von der Extension genutzt (abwaehlbar). */
  useForExtension = true;
  editMode = false;

  constructor(
    public dialogRef: MatDialogRef<CreateRepertoireDialogComponent>,
    @Optional() @Inject(MAT_DIALOG_DATA) data: Repertoire | null
  ) {
    if (data) {
      this.editMode = true;
      this.name = data.name;
      this.description = data.description ?? '';
      this.isPublic = data.isPublic;
      this.kind = data.kind as RepertoireKind;
      this.useForExtension = data.useForExtension;
    }
  }
}
