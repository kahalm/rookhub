import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { QrCodeComponent } from '../../shared/qr-code/qr-code.component';
import { SnackbarService } from '../../core/snackbar.service';

/**
 * Zeigt den öffentlichen Nur-Ansehen-Link (<c>/l/{token}</c>) einer geteilten Repertoire-Linie:
 * QR-Code + kopierbare URL. Analog zum Puzzle-Teilen-Dialog.
 */
@Component({
  selector: 'app-share-line-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TranslateModule, QrCodeComponent],
  template: `
    <h2 class="dialog-title">{{ 'repertoire.shareLine.title' | translate }}</h2>
    @if (data.lineTitle) { <div class="line-name">{{ data.lineTitle }}</div> }
    <div class="qr-container">
      <app-qr-code [data]="data.url" [width]="220" />
    </div>
    <div class="link-row">
      <input class="link-input" [value]="data.url" readonly />
      <button mat-icon-button (click)="copyLink()" [attr.aria-label]="'common.copy' | translate">
        <mat-icon>content_copy</mat-icon>
      </button>
    </div>
    <p class="hint">{{ 'repertoire.shareLine.hint' | translate }}</p>
    <div class="dialog-actions">
      <span class="spacer"></span>
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </div>
  `,
  styles: [`
    :host { display: block; padding: 1.25rem; max-width: 340px; }
    .dialog-title { margin: 0 0 0.5rem; font-size: 1.2rem; }
    .line-name { text-align: center; margin-bottom: 0.75rem; font-weight: 500; opacity: 0.85; }
    .qr-container { display: flex; justify-content: center; margin-bottom: 1rem; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; }
    .link-input {
      flex: 1; padding: 0.5rem; border: 1px solid color-mix(in srgb, currentColor 20%, transparent); border-radius: 4px;
      font-size: 0.85rem; background: var(--mat-sys-surface-container, #f5f5f5); color: inherit;
    }
    .hint { font-size: 0.8rem; opacity: 0.65; margin: 0.75rem 0 0; }
    .dialog-actions { display: flex; align-items: center; margin-top: 1rem; }
    .dialog-actions .spacer { flex: 1; }
  `]
})
export class ShareLineDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: { url: string; lineTitle?: string },
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  copyLink(): void {
    if (navigator.clipboard?.writeText) {
      navigator.clipboard.writeText(this.data.url)
        .then(() => this.snackbar.copy(this.translate.instant('puzzles.share.copied')))
        .catch(() => this.fallbackCopy());
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
      this.snackbar.copy(this.translate.instant('puzzles.share.copied'));
    } catch {
      this.snackbar.copy(this.translate.instant('puzzles.share.copyFailed'));
    }
    document.body.removeChild(textarea);
  }
}
