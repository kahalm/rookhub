import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';

interface ApiToken {
  id: number;
  name: string;
  prefix: string;
  scope: string;
  createdAt: string;
  lastUsedAt: string | null;
  expiresAt: string | null;
}

interface ApiTokenCreated extends ApiToken {
  rawToken: string;
}

@Component({
  selector: 'app-create-token-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatSelectModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ 'profile.tokens.dialog.title' | translate }}</h2>
    <mat-dialog-content>
      <form class="dialog-form">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'profile.tokens.dialog.name' | translate }}</mat-label>
          <input matInput [(ngModel)]="name" name="name" required maxlength="100">
          <mat-hint>{{ 'profile.tokens.dialog.nameHint' | translate }}</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'profile.tokens.dialog.expires' | translate }}</mat-label>
          <mat-select [(ngModel)]="expiresInDays" name="expiresInDays">
            <mat-option [value]="null">{{ 'profile.tokens.dialog.never' | translate }}</mat-option>
            <mat-option [value]="30">30 {{ 'profile.tokens.dialog.days' | translate }}</mat-option>
            <mat-option [value]="90">90 {{ 'profile.tokens.dialog.days' | translate }}</mat-option>
            <mat-option [value]="365">365 {{ 'profile.tokens.dialog.days' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="!name" (click)="dialogRef.close({ name, expiresInDays })">
        {{ 'profile.tokens.dialog.create' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`.dialog-form { display: flex; flex-direction: column; gap: 0.5rem; min-width: 320px; } mat-form-field { width: 100%; }`]
})
export class CreateTokenDialogComponent {
  name = '';
  expiresInDays: number | null = null;
  constructor(public dialogRef: MatDialogRef<CreateTokenDialogComponent>) {}
}

@Component({
  selector: 'app-show-token-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ 'profile.tokens.show.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="warning">⚠️ {{ 'profile.tokens.show.warning' | translate }}</p>
      <div class="token-box">
        <code>{{ token }}</code>
        <button mat-icon-button (click)="copy()" [attr.title]="'profile.tokens.show.copy' | translate">
          <mat-icon>content_copy</mat-icon>
        </button>
      </div>
      @if (copied) {
        <span class="copied">{{ 'profile.tokens.show.copied' | translate }}</span>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-raised-button color="primary" (click)="dialogRef.close()">{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .warning { color: #d32f2f; font-weight: 500; }
    .token-box { display: flex; align-items: center; gap: 8px; background: var(--mat-sys-surface-container, #f5f5f5); padding: 8px 12px; border-radius: 4px; }
    .token-box code { flex: 1; word-break: break-all; font-family: monospace; font-size: 0.9rem; }
    .copied { color: #2e7d32; font-size: 0.85rem; }
  `]
})
export class ShowTokenDialogComponent {
  token: string;
  copied = false;
  constructor(public dialogRef: MatDialogRef<ShowTokenDialogComponent>) {
    // Token via dialog data — Workaround mit injection via static (in OpenDialog gesetzt)
    this.token = ShowTokenDialogComponent.pendingToken;
    ShowTokenDialogComponent.pendingToken = '';
  }
  static pendingToken = '';

  copy(): void {
    navigator.clipboard.writeText(this.token).then(() => {
      this.copied = true;
      setTimeout(() => (this.copied = false), 2000);
    });
  }
}

@Component({
  selector: 'app-api-tokens',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatDialogModule, TranslateModule],
  template: `
    <mat-card class="tokens-card">
      <mat-card-header>
        <mat-card-title>{{ 'profile.tokens.title' | translate }}</mat-card-title>
        <mat-card-subtitle>{{ 'profile.tokens.subtitle' | translate }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (loading) {
          <p>{{ 'common.loading' | translate }}</p>
        } @else if (tokens.length === 0) {
          <p class="empty-hint">{{ 'profile.tokens.empty' | translate }}</p>
        } @else {
          <table class="tokens-table">
            <thead>
              <tr>
                <th>{{ 'profile.tokens.col.name' | translate }}</th>
                <th>{{ 'profile.tokens.col.prefix' | translate }}</th>
                <th>{{ 'profile.tokens.col.created' | translate }}</th>
                <th>{{ 'profile.tokens.col.lastUsed' | translate }}</th>
                <th>{{ 'profile.tokens.col.expires' | translate }}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (t of tokens; track t.id) {
                <tr>
                  <td>{{ t.name }}</td>
                  <td><code>{{ t.prefix }}…</code></td>
                  <td>{{ t.createdAt | date:'short' }}</td>
                  <td>{{ t.lastUsedAt ? (t.lastUsedAt | date:'short') : ('profile.tokens.never' | translate) }}</td>
                  <td>{{ t.expiresAt ? (t.expiresAt | date:'short') : ('profile.tokens.never' | translate) }}</td>
                  <td>
                    <button mat-icon-button color="warn" (click)="revoke(t)" [attr.title]="'profile.tokens.revoke' | translate">
                      <mat-icon>delete</mat-icon>
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </mat-card-content>
      <mat-card-actions>
        <button mat-raised-button color="primary" (click)="openCreateDialog()">
          <mat-icon>add</mat-icon> {{ 'profile.tokens.create' | translate }}
        </button>
      </mat-card-actions>
    </mat-card>
  `,
  styles: [`
    .tokens-card { margin-top: 1rem; }
    .empty-hint { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; }
    .tokens-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
    .tokens-table th, .tokens-table td { padding: 6px 10px; border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent); text-align: left; }
    .tokens-table code { font-family: monospace; background: color-mix(in srgb, currentColor 6%, transparent); padding: 2px 6px; border-radius: 3px; }
  `]
})
export class ApiTokensComponent implements OnInit {
  tokens: ApiToken[] = [];
  loading = true;

  constructor(private http: HttpClient, private dialog: MatDialog, private snackbar: SnackbarService, private translate: TranslateService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.http.get<ApiToken[]>('/api/profile/tokens').subscribe({
      next: t => { this.tokens = t; this.loading = false; },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('profile.tokens.loadFailed')); }
    });
  }

  openCreateDialog(): void {
    const ref = this.dialog.open(CreateTokenDialogComponent, { width: '420px' });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      this.http.post<ApiTokenCreated>('/api/profile/tokens', result).subscribe({
        next: t => {
          ShowTokenDialogComponent.pendingToken = t.rawToken;
          this.dialog.open(ShowTokenDialogComponent, { width: '520px', disableClose: true })
            .afterClosed().subscribe(() => this.load());
        },
        error: err => this.snackbar.info(err.error?.message || this.translate.instant('profile.tokens.createFailed'))
      });
    });
  }

  revoke(t: ApiToken): void {
    if (!confirm(this.translate.instant('profile.tokens.revokeConfirm', { name: t.name }))) return;
    this.http.delete(`/api/profile/tokens/${t.id}`).subscribe({
      next: () => this.load(),
      error: () => this.snackbar.info(this.translate.instant('profile.tokens.revokeFailed'))
    });
  }
}
