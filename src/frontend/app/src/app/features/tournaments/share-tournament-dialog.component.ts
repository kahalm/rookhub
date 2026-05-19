import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { QRCodeComponent } from 'angularx-qrcode';

@Component({
  selector: 'app-share-tournament-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, MatSnackBarModule, QRCodeComponent],
  template: `
    <h2 class="dialog-title">Turnier teilen</h2>
    <div class="qr-container">
      <qrcode [qrdata]="data.url" [width]="220" errorCorrectionLevel="M"></qrcode>
    </div>
    <div class="link-row">
      <input class="link-input" [value]="data.url" readonly #linkInput />
      <button mat-icon-button (click)="copyLink()">
        <mat-icon>content_copy</mat-icon>
      </button>
    </div>
    <div class="dialog-actions">
      <button mat-button mat-dialog-close>Schliessen</button>
    </div>
  `,
  styles: [`
    :host { display: block; padding: 1.25rem; }
    .dialog-title { margin: 0 0 1rem; font-size: 1.2rem; }
    .qr-container { display: flex; justify-content: center; margin-bottom: 1rem; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; }
    .link-input {
      flex: 1; padding: 0.5rem; border: 1px solid #ccc; border-radius: 4px;
      font-size: 0.85rem; background: #f5f5f5; color: #333;
    }
    .dialog-actions { display: flex; justify-content: flex-end; margin-top: 1rem; }
  `]
})
export class ShareTournamentDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: { url: string },
    private snackBar: MatSnackBar
  ) {}

  copyLink(): void {
    navigator.clipboard.writeText(this.data.url).then(() => {
      this.snackBar.open('Link kopiert!', 'Close', { duration: 2000 });
    });
  }
}
