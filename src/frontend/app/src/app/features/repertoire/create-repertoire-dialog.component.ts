import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';

@Component({
  selector: 'app-create-repertoire-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>New Repertoire</h2>
    <mat-dialog-content>
      <form class="dialog-form">
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput [(ngModel)]="name" name="name" required>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Description</mat-label>
          <textarea matInput [(ngModel)]="description" name="description" rows="3"></textarea>
        </mat-form-field>
        <mat-checkbox [(ngModel)]="isPublic" name="isPublic">Public</mat-checkbox>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-raised-button color="primary" [disabled]="!name" (click)="dialogRef.close({ name, description, isPublic })">Create</button>
    </mat-dialog-actions>
  `,
  styles: [`.dialog-form { display: flex; flex-direction: column; gap: 0.5rem; min-width: 300px; } mat-form-field { width: 100%; }`]
})
export class CreateRepertoireDialogComponent {
  name = '';
  description = '';
  isPublic = false;

  constructor(public dialogRef: MatDialogRef<CreateRepertoireDialogComponent>) {}
}
