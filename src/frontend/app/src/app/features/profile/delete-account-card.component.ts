import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterModule } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';

/**
 * Karte „Konto löschen" (DSGVO). Aus <c>ProfileComponent</c> ausgegliedert; self-contained —
 * ruft <see cref="AuthService.deleteAccount"/> (dessen logout() navigiert bereits zu /login).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-delete-account-card',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, RouterModule, TranslatePipe,
  ],
  template: `
    <div class="danger-section">
      <h4>{{ 'profile.delete.title' | translate }}</h4>
      <p class="danger-hint">{{ 'profile.delete.hint' | translate }}</p>
      @if (!showDelete) {
        <button mat-stroked-button color="warn" type="button" (click)="showDelete = true">
          <mat-icon>delete_forever</mat-icon> {{ 'profile.delete.button' | translate }}
        </button>
      } @else {
        <div class="danger-confirm">
          <p class="danger-warn">{{ 'profile.delete.warn' | translate }}</p>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.delete.password' | translate }}</mat-label>
            <input matInput type="password" [(ngModel)]="deletePassword" name="delPwd" autocomplete="current-password">
          </mat-form-field>
          <div class="danger-actions">
            <button mat-button type="button" (click)="cancelDelete()">{{ 'common.cancel' | translate }}</button>
            <button mat-raised-button color="warn" type="button" (click)="deleteAccount()" [disabled]="!deletePassword || deleting">
              {{ deleting ? ('profile.delete.deleting' | translate) : ('profile.delete.confirm' | translate) }}
            </button>
          </div>
        </div>
      }
      <p class="danger-link"><a routerLink="/account-deletion">{{ 'profile.delete.moreInfo' | translate }}</a></p>
    </div>
  `,
  styles: [`
    mat-form-field { width: 100%; }
    .danger-section h4 { margin: 0 0 0.25rem; color: #ef9a9a; }
    .danger-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0 0 0.5rem; }
    .danger-warn { color: #ef9a9a; font-size: 0.9rem; }
    .danger-confirm { display: flex; flex-direction: column; gap: 0.25rem; max-width: 360px; }
    .danger-actions { display: flex; gap: 8px; justify-content: flex-end; }
    .danger-link { margin: 0.75rem 0 0; font-size: 0.85rem; }
    .danger-link a { color: #90caf9; }
  `]
})
export class DeleteAccountCardComponent {
  showDelete = false;
  deletePassword = '';
  deleting = false;

  constructor(
    private auth: AuthService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  cancelDelete(): void {
    this.showDelete = false;
    this.deletePassword = '';
  }

  deleteAccount(): void {
    if (!this.deletePassword || this.deleting) return;
    this.deleting = true;
    this.auth.deleteAccount(this.deletePassword).subscribe({
      next: () => {
        // logout() in deleteAccount navigiert bereits zu /login
        this.snackbar.success(this.translate.instant('profile.delete.done'));
      },
      error: (err) => {
        this.deleting = false;
        this.snackbar.info(this.translate.instant(
          err?.status === 401 ? 'profile.delete.wrongPassword' : 'profile.delete.failed'));
      }
    });
  }
}
