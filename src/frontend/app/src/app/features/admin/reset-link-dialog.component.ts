import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';

export interface ResetLinkDialogData {
  username: string;
  link: string;
  /** ISO-UTC; im Dialog lokal formatiert. */
  expiresAt: string;
  /** Hat das Konto eine E-Mail-Adresse? Dann ginge auch „Passwort vergessen“. */
  hasEmail: boolean;
}

/**
 * Zeigt den vom Admin erzeugten Reset-Link (UX-002 / P-2) zum Weitergeben: read-only Feld + Kopieren, Ablauf und
 * Einmaligkeit. Der Admin setzt und sieht kein Passwort. Kopier-Muster wie im Linien-Teilen-Dialog.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-reset-link-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'admin.users.resetLink.title' | translate: { username: data.username } }}</h2>
    <mat-dialog-content>
      <div class="link-row">
        <input class="link-input" [value]="data.link" readonly (focus)="$any($event.target).select()"
               [attr.aria-label]="'admin.users.resetLink.title' | translate: { username: data.username }" />
        <button mat-stroked-button (click)="copyLink()">
          <mat-icon>content_copy</mat-icon>
          {{ 'common.copy' | translate }}
        </button>
      </div>
      <p class="hint">{{ 'admin.users.resetLink.validity' | translate: { date: (data.expiresAt | date:'medium') } }}</p>
      <p class="hint">{{ 'admin.users.resetLink.selfService' | translate }}</p>
      <p class="hint mail">{{ (data.hasEmail ? 'admin.users.resetLink.hasEmail' : 'admin.users.resetLink.noEmail') | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display: block; max-width: 520px; }
    .link-row { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
    .link-input {
      flex: 1 1 16rem; min-width: 0; padding: 0.5rem; border-radius: 4px; font-size: 0.85rem; color: inherit;
      border: 1px solid color-mix(in srgb, currentColor 20%, transparent);
      background: var(--mat-sys-surface-container, #f5f5f5);
    }
    .hint { font-size: 0.875rem; margin: 0.75rem 0 0; }
    .mail { opacity: 0.7; }
  `]
})
export class ResetLinkDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: ResetLinkDialogData,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  copyLink(): void {
    if (navigator.clipboard?.writeText) {
      navigator.clipboard.writeText(this.data.link)
        .then(() => this.snackbar.copy(this.translate.instant('admin.users.resetLink.copied')))
        .catch(() => this.fallbackCopy());
    } else {
      this.fallbackCopy();
    }
  }

  private fallbackCopy(): void {
    const textarea = document.createElement('textarea');
    textarea.value = this.data.link;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    try {
      const ok = document.execCommand('copy');
      this.snackbar.copy(this.translate.instant(ok ? 'admin.users.resetLink.copied' : 'admin.users.resetLink.copyFailed'));
    } catch {
      this.snackbar.copy(this.translate.instant('admin.users.resetLink.copyFailed'));
    }
    document.body.removeChild(textarea);
  }
}
