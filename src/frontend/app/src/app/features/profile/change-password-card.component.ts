import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';

/**
 * Karte „Passwort ändern". Aus <c>ProfileComponent</c> ausgegliedert; self-contained —
 * ruft <see cref="AuthService.changePassword"/> und behandelt Validierung/Snackbars selbst.
 */
@Component({
  selector: 'app-change-password-card',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, TranslatePipe,
  ],
  template: `
    <div class="changepwd-section">
      <h4>{{ 'profile.changePwd.title' | translate }}</h4>
      @if (!showChangePwd) {
        <button mat-stroked-button type="button" (click)="showChangePwd = true">
          <mat-icon>lock</mat-icon> {{ 'profile.changePwd.button' | translate }}
        </button>
      } @else {
        <div class="changepwd-form">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.changePwd.current' | translate }}</mat-label>
            <input matInput type="password" [(ngModel)]="changePwdCurrent" name="cpwdCurrent" autocomplete="current-password">
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.changePwd.new' | translate }}</mat-label>
            <input matInput type="password" [(ngModel)]="changePwdNew" name="cpwdNew" autocomplete="new-password">
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.changePwd.confirm' | translate }}</mat-label>
            <input matInput type="password" [(ngModel)]="changePwdConfirm" name="cpwdConfirm" autocomplete="new-password">
          </mat-form-field>
          <div class="changepwd-actions">
            <button mat-button type="button" (click)="cancelChangePwd()">{{ 'common.cancel' | translate }}</button>
            <button mat-raised-button color="primary" type="button" (click)="changePassword()"
              [disabled]="!changePwdCurrent || !changePwdNew || !changePwdConfirm || changingPwd">
              {{ changingPwd ? ('profile.changePwd.saving' | translate) : ('profile.changePwd.save' | translate) }}
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    mat-form-field { width: 100%; }
    .changepwd-section h4 { margin: 0 0 0.5rem; color: #90caf9; }
    .changepwd-form { display: flex; flex-direction: column; gap: 0.25rem; max-width: 360px; }
    .changepwd-actions { display: flex; gap: 8px; justify-content: flex-end; }
  `]
})
export class ChangePasswordCardComponent {
  showChangePwd = false;
  changePwdCurrent = '';
  changePwdNew = '';
  changePwdConfirm = '';
  changingPwd = false;

  constructor(
    private auth: AuthService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  cancelChangePwd(): void {
    this.showChangePwd = false;
    this.changePwdCurrent = '';
    this.changePwdNew = '';
    this.changePwdConfirm = '';
  }

  changePassword(): void {
    if (!this.changePwdCurrent || !this.changePwdNew || !this.changePwdConfirm || this.changingPwd) return;
    if (this.changePwdNew !== this.changePwdConfirm) {
      this.snackbar.info(this.translate.instant('profile.changePwd.mismatch'));
      return;
    }
    this.changingPwd = true;
    this.auth.changePassword(this.changePwdCurrent, this.changePwdNew).subscribe({
      next: () => {
        this.changingPwd = false;
        this.cancelChangePwd();
        this.snackbar.success(this.translate.instant('profile.changePwd.done'));
      },
      error: (err) => {
        this.changingPwd = false;
        this.snackbar.info(this.translate.instant(
          err?.status === 401 ? 'profile.changePwd.wrongPassword' : 'profile.changePwd.failed'));
      }
    });
  }
}
