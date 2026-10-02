import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { PASSWORD_MIN_LENGTH } from './register.component';

/** Was beim Zuruecksetzen schiefging — steuert Text und Knopf im Fehlerblock. */
export type ResetError = 'mismatch' | 'passwordRejected' | 'expired' | 'failed';

/**
 * HTTP-Fehler → Fehlerart (Codereview UX-018). Bisher kam der rohe englische Servertext als Snackbar, die nach
 * Sekunden verschwand: „Password must be at least 8 characters long. …" bzw. „Invalid or expired reset token.".
 * 400 mit Feldfehler am neuen Passwort = Passwort abgelehnt (zu kurz, zu bekannt); jede andere 400 betrifft den Link
 * (abgelaufen, benutzt, unvollstaendig — `ResetPasswordDto` hat sonst nur `Token`). Alles Uebrige: fehlgeschlagen.
 */
export function resetErrorOf(err: any): ResetError {
  if (err?.status !== 400) return 'failed';
  const fields = Object.keys(err.error?.errors ?? {});
  return fields.some(f => f.toLowerCase() === 'newpassword') ? 'passwordRejected' : 'expired';
}

const ERROR_KEYS: Record<ResetError, string> = {
  mismatch: 'auth.reset.mismatch',
  // Derselbe Satz wie beim Registrieren: gleiche Regel (PasswordPolicy), gleicher Platzhalter {{min}}.
  passwordRejected: 'auth.register.passwordRejected',
  expired: 'auth.reset.expired',
  failed: 'auth.reset.failed',
};

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, TranslatePipe],
  template: `
    <div class="auth-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'auth.reset.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (!token) {
            <p class="auth-info">{{ 'auth.reset.missingToken' | translate }}</p>
          } @else {
            <form #f="ngForm" (ngSubmit)="onSubmit(f)" class="auth-form">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.reset.passwordLabel' | translate }}</mat-label>
                <!-- Mindestlaenge wie der Server (UX-018): hier stand 4, die API verlangt 8 — der Knopf liess ein kurzes
                     Passwort zu, das dann am Server scheiterte. -->
                <input matInput type="password" [(ngModel)]="password" name="password" required [minlength]="passwordMin" autofocus autocomplete="new-password">
                <mat-hint>{{ 'auth.reset.passwordHint' | translate:{ min: passwordMin } }}</mat-hint>
                <mat-error>{{ 'auth.reset.passwordHint' | translate:{ min: passwordMin } }}</mat-error>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.reset.confirmLabel' | translate }}</mat-label>
                <input matInput type="password" [(ngModel)]="confirm" name="confirm" required [minlength]="passwordMin" autocomplete="new-password">
                <mat-error>{{ 'auth.reset.passwordHint' | translate:{ min: passwordMin } }}</mat-error>
              </mat-form-field>
              <!-- Knopf immer aktiv wie bei Anmelden/Registrieren (UX-052); geprueft wird beim Absenden. -->
              <button mat-raised-button color="primary" type="submit" [disabled]="loading()">
                {{ loading() ? ('auth.reset.submitting' | translate) : ('auth.reset.submit' | translate) }}
              </button>
            </form>
            <!-- Im Formular statt als Snackbar (wie beim Registrieren): bleibt stehen, in der Sprache der Seite, und
                 bietet beim abgelaufenen Link gleich den naechsten Schritt an. -->
            @if (error(); as e) {
              <div class="form-error" role="alert">
                <p>{{ errorKey(e) | translate:{ min: passwordMin } }}</p>
                @if (e === 'expired') {
                  <a mat-stroked-button color="primary" routerLink="/forgot-password">{{ 'auth.reset.requestNew' | translate }}</a>
                }
              </div>
            }
          }
        </mat-card-content>
        <mat-card-actions>
          <a mat-button routerLink="/forgot-password">{{ 'auth.reset.requestNew' | translate }}</a>
          <a mat-button routerLink="/login">{{ 'auth.forgot.backToLogin' | translate }}</a>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: [`
    .auth-container { display: flex; justify-content: center; align-items: center; min-height: 80vh; }
    mat-card { width: 400px; max-width: 90vw; }
    .auth-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    .auth-info { background: rgba(144, 202, 249, 0.15); border-left: 3px solid #90caf9; padding: 0.6rem 0.8rem; border-radius: 4px; margin: 0.5rem 0 0; font-size: 0.9rem; }
    mat-form-field { width: 100%; }
    .form-error { margin-top: 12px; padding: 12px; border-radius: 8px;
                  background: rgba(211, 47, 47, 0.08); border: 1px solid rgba(211, 47, 47, 0.35); }
    .form-error p { margin: 0 0 8px; }
    .form-error p:last-child { margin-bottom: 0; }
    /* Handy: 'Neuen Link anfordern' und 'Zurueck zur Anmeldung' passen nicht nebeneinander (gleiches Muster wie
       im Login) und brachen innerhalb des 40px-Buttons zweizeilig um. */
    @media (max-width: 768px) { mat-card-actions { flex-wrap: wrap; } }
  `]
})
export class ResetPasswordComponent {
  token = '';
  password = '';
  confirm = '';
  // Signals statt Felder: nach einer HTTP-Antwort rendert Angular 22 eine unmarkierte View nicht neu —
  // ein im error-Callback gesetzter Fehler bliebe unsichtbar (siehe RegisterComponent).
  readonly loading = signal(false);
  readonly error = signal<ResetError | null>(null);
  readonly passwordMin = PASSWORD_MIN_LENGTH;

  constructor(private auth: AuthService, private router: Router, private route: ActivatedRoute, private snackbar: SnackbarService, private translate: TranslateService) {
    this.token = this.route.snapshot.queryParams['token'] || '';
  }

  get canSubmit(): boolean {
    return this.password.length >= this.passwordMin && this.password === this.confirm;
  }

  errorKey(e: ResetError): string {
    return ERROR_KEYS[e];
  }

  onSubmit(form?: NgForm): void {
    if (form?.invalid) {
      form.control.markAllAsTouched();
      return;
    }
    if (this.password !== this.confirm) {
      this.error.set('mismatch');
      return;
    }
    this.error.set(null);
    this.loading.set(true);
    this.auth.resetPassword(this.token, this.password).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('auth.reset.success'));
        this.router.navigate(['/login']);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(resetErrorOf(err));
      }
    });
  }
}
