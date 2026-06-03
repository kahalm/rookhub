import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule } from '@ngx-translate/core';
import { RepertoireKind } from '../../core/repertoire.types';

@Component({
  selector: 'app-create-repertoire-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatCheckboxModule, MatSelectModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ 'repertoire.dialog.title' | translate }}</h2>
    <mat-dialog-content>
      <form class="dialog-form">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'repertoire.dialog.name' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" name="name" required>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'repertoire.dialog.description' | translate }}</mat-label>
          <textarea matInput [(ngModel)]="description" name="description" rows="3"></textarea>
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
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="!name" (click)="dialogRef.close({ name, description, isPublic, kind })">{{ 'repertoire.dialog.create' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`.dialog-form { display: flex; flex-direction: column; gap: 0.5rem; min-width: 300px; } mat-form-field { width: 100%; }`]
})
export class CreateRepertoireDialogComponent {
  name = '';
  description = '';
  isPublic = false;
  kind: RepertoireKind = 0; // None

  constructor(public dialogRef: MatDialogRef<CreateRepertoireDialogComponent>) {}
}
