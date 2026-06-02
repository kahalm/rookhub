import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatSnackBarModule, TranslateModule],
  template: `
    <div class="auth-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'auth.register.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form (ngSubmit)="onSubmit()" class="auth-form">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.usernameLabel' | translate }}</mat-label>
              <input matInput [(ngModel)]="username" name="username" required minlength="3">
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.emailLabel' | translate }}</mat-label>
              <input matInput type="email" [(ngModel)]="email" name="email" required>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.passwordLabel' | translate }}</mat-label>
              <input matInput type="password" [(ngModel)]="password" name="password" required minlength="8" pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).{8,}">
              <mat-hint>{{ 'auth.register.passwordHint' | translate }}</mat-hint>
            </mat-form-field>
            <button mat-raised-button color="primary" type="submit" [disabled]="loading">
              {{ loading ? ('auth.register.submitting' | translate) : ('auth.register.submit' | translate) }}
            </button>
          </form>
        </mat-card-content>
        <mat-card-actions>
          <a mat-button routerLink="/login" [queryParams]="{ returnUrl: returnUrl }">{{ 'auth.register.loginLink' | translate }}</a>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: [`
    .auth-container { display: flex; justify-content: center; align-items: center; min-height: 80vh; }
    mat-card { width: 400px; max-width: 90vw; }
    .auth-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
  `]
})
export class RegisterComponent {
  username = '';
  email = '';
  password = '';
  loading = false;

  returnUrl: string;

  constructor(private auth: AuthService, private router: Router, private route: ActivatedRoute, private snackBar: MatSnackBar, private translate: TranslateService) {
    const raw = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
    this.returnUrl = this.sanitizeReturnUrl(raw);
  }

  private sanitizeReturnUrl(url: string): string {
    if (!url.startsWith('/') || url.startsWith('//') || url.includes('://')) return '/dashboard';
    return url;
  }

  onSubmit(): void {
    this.loading = true;
    this.auth.register(this.username, this.email, this.password).subscribe({
      next: () => {
        // navigateByUrl (nicht navigate([...])): returnUrl ist ein kompletter Pfad und kann mehrere
        // Segmente haben (z.B. /tournaments/123) — navigate([...]) würde den Slash url-encoden → 404.
        const sep = this.returnUrl.includes('?') ? '&' : '?';
        this.router.navigateByUrl(`${this.returnUrl}${sep}quickstart=1`);
      },
      error: (err) => {
        this.loading = false;
        const msg = err.error?.message
          || (err.error?.errors && Object.values(err.error.errors).flat().join(' '))
          || this.translate.instant('auth.register.failed');
        this.snackBar.open(msg, this.translate.instant('common.close'), { duration: 5000 });
      }
    });
  }
}
