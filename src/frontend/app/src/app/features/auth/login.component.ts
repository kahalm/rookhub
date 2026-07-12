import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatCheckboxModule, TranslatePipe],
  template: `
    <div class="auth-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'auth.login.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (authRequired) {
            <p class="auth-required">{{ 'auth.login.required' | translate }}</p>
          }
          <form (ngSubmit)="onSubmit()" class="auth-form">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.login.usernameLabel' | translate }}</mat-label>
              <input matInput [(ngModel)]="username" name="username" required autofocus>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.login.passwordLabel' | translate }}</mat-label>
              <input matInput type="password" [(ngModel)]="password" name="password" required>
            </mat-form-field>
            <mat-checkbox [(ngModel)]="rememberMe" name="rememberMe">{{ 'auth.login.rememberMe' | translate }}</mat-checkbox>
            <button mat-raised-button color="primary" type="submit" [disabled]="loading">
              {{ loading ? ('auth.login.submitting' | translate) : ('auth.login.submit' | translate) }}
            </button>
          </form>
        </mat-card-content>
        <mat-card-actions>
          <a mat-button routerLink="/register" [queryParams]="{ returnUrl: returnUrl }">{{ 'auth.login.registerLink' | translate }}</a>
          <a mat-button routerLink="/forgot-password">{{ 'auth.login.forgotLink' | translate }}</a>
        </mat-card-actions>
      </mat-card>
      <div class="legal-links">
        <a routerLink="/privacy">{{ 'legal.privacy.title' | translate }}</a>
        <span>·</span>
        <a routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</a>
      </div>
    </div>
  `,
  styles: [`
    .auth-container { display: flex; flex-direction: column; justify-content: center; align-items: center; min-height: 80vh; }
    .legal-links { margin-top: 1rem; text-align: center; font-size: 0.8rem; }
    .legal-links a { color: #90caf9; }
    .legal-links span { color: color-mix(in srgb, currentColor 53%, transparent); margin: 0 6px; }
    mat-card { width: 400px; max-width: 90vw; }
    .auth-required { background: rgba(144, 202, 249, 0.15); border-left: 3px solid #90caf9; padding: 0.6rem 0.8rem; border-radius: 4px; margin: 0.5rem 0 0; font-size: 0.9rem; }
    .auth-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
  `]
})
export class LoginComponent {
  username = '';
  password = '';
  rememberMe = false;
  loading = false;

  returnUrl: string;
  authRequired = false;

  constructor(private auth: AuthService, private router: Router, private route: ActivatedRoute, private snackbar: SnackbarService, private translate: TranslateService) {
    const raw = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
    this.returnUrl = this.sanitizeReturnUrl(raw);
    this.authRequired = this.route.snapshot.queryParams['authRequired'] === '1';
  }

  private sanitizeReturnUrl(url: string): string {
    if (!url.startsWith('/') || url.startsWith('//') || url.includes('://')) return '/dashboard';
    return url;
  }

  onSubmit(): void {
    this.loading = true;
    this.auth.login(this.username, this.password, this.rememberMe).subscribe({
      next: () => {
        this.router.navigateByUrl(this.returnUrl);
      },
      error: (err) => {
        this.loading = false;
        const msg = err.error?.message
          || (err.error?.errors && Object.values(err.error.errors).flat().join(' '))
          || this.translate.instant('auth.login.failed');
        this.snackbar.warn(msg);
      }
    });
  }
}
