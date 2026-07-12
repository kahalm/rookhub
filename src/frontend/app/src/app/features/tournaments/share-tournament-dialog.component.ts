import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { QrCodeComponent } from '../../shared/qr-code/qr-code.component';

@Component({
  selector: 'app-share-tournament-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TranslateModule, QrCodeComponent],
  template: `
    <h2 class="dialog-title">{{ 'tournaments.share.title' | translate }}</h2>
    <div class="qr-container">
      <app-qr-code [data]="data.url" [width]="220" />
    </div>
    <div class="link-row">
      <input class="link-input" [value]="data.url" readonly #linkInput />
      <button mat-icon-button [attr.aria-label]="'tournaments.share.copyLink' | translate" (click)="copyLink()">
        <mat-icon>content_copy</mat-icon>
      </button>
    </div>
    <div class="dialog-actions">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </div>
  `,
  styles: [`
    :host { display: block; padding: 1.25rem; }
    .dialog-title { margin: 0 0 1rem; font-size: 1.2rem; }
    .qr-container { display: flex; justify-content: center; margin-bottom: 1rem; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; }
    .link-input {
      flex: 1; padding: 0.5rem; border: 1px solid color-mix(in srgb, currentColor 20%, transparent); border-radius: 4px;
      font-size: 0.85rem; background: var(--mat-sys-surface-container, #f5f5f5); color: inherit;
    }
    .dialog-actions { display: flex; justify-content: flex-end; margin-top: 1rem; }
  `]
})
export class ShareTournamentDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: { url: string },
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  copyLink(): void {
    if (navigator.clipboard?.writeText) {
      navigator.clipboard.writeText(this.data.url).then(() => {
        this.snackbar.copy(this.translate.instant('tournaments.share.linkCopied'));
      }).catch(() => this.fallbackCopy());
    } else {
      this.fallbackCopy();
    }
  }

  private fallbackCopy(): void {
    const textarea = document.createElement('textarea');
    textarea.value = this.data.url;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    try {
      document.execCommand('copy');
      this.snackbar.copy(this.translate.instant('tournaments.share.linkCopied'));
    } catch {
      this.snackbar.copy(this.translate.instant('tournaments.share.copyFailed'));
    }
    document.body.removeChild(textarea);
  }
}
