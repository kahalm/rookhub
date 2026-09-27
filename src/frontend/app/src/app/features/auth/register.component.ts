import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { AuthPrefillService } from '../../core/auth-prefill.service';
import { sanitizeReturnUrl } from '../../core/return-url.util';

/** Mindestlänge des Benutzernamens — wie `[MinLength(3)]` am RegisterDto (API). */
export const USERNAME_MIN_LENGTH = 3;

/**
 * Mindestlänge des Passworts — MUSS zu `PasswordPolicyAttribute.MinimumLength` (API) passen.
 * Hier stand 4, der Server verlangt 8: ein kurzes Passwort kam durch die Prüfung im Formular
 * und scheiterte dann mit „Password must be at least 8 characters long." — auf Englisch, auch
 * auf der Kinderseite, deren Nutzer genau solche kurzen Passwörter wählen.
 */
export const PASSWORD_MIN_LENGTH = 8;

/** Was bei der Registrierung schiefging — steuert Text und Anmelde-Knopf. */
export type RegisterError = 'taken' | 'passwordRejected' | 'invalid' | 'failed';

/**
 * HTTP-Fehler → Fehlerart. 409 heißt „Name ODER E-Mail vergeben": der Server sagt bewusst nicht,
 * welches von beiden (Audit-Befund, kein Enumeration-Oracle) — also auch das Formular nicht.
 * Bisher zeigte es die rohe englische Server-Meldung als Snackbar, die nach Sekunden verschwand,
 * ohne Weg zur Anmeldung (27.09.: ein Besucher mit Konto versuchte sich neu zu registrieren und gab auf).
 */
export function registerErrorOf(err: any): RegisterError {
  if (err?.status === 409) return 'taken';
  if (err?.status === 400) {
    const fields = Object.keys(err.error?.errors ?? {});
    return fields.some(f => f.toLowerCase() === 'password') ? 'passwordRejected' : 'invalid';
  }
  return 'failed';
}

const ERROR_KEYS: Record<RegisterError, string> = {
  taken: 'auth.register.taken',
  passwordRejected: 'auth.register.passwordRejected',
  invalid: 'auth.register.invalid',
  failed: 'auth.register.failed',
};

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, TranslatePipe],
  template: `
    <div class="auth-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'auth.register.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form #f="ngForm" (ngSubmit)="onSubmit(f)" class="auth-form">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.usernameLabel' | translate }}</mat-label>
              <!-- Handy-Tastatur: ohne autocapitalize="none" wurde der Benutzername als 'Kahalm' statt 'kahalm'
                   eingegeben und so gespeichert; new-password laesst den Passwort-Manager ein starkes Passwort vorschlagen. -->
              <input matInput [(ngModel)]="username" name="username" required [minlength]="usernameMin"
                     autocomplete="username" autocapitalize="none" autocorrect="off" spellcheck="false">
              <mat-error>{{ 'auth.register.usernameTooShort' | translate:{ min: usernameMin } }}</mat-error>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.emailLabel' | translate }}</mat-label>
              <input matInput type="email" [(ngModel)]="email" name="email" email autocomplete="email">
              <mat-hint>{{ 'auth.register.emailHint' | translate }}</mat-hint>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.passwordLabel' | translate }}</mat-label>
              <input matInput type="password" [(ngModel)]="password" name="password" required [minlength]="passwordMin" autocomplete="new-password">
              <mat-hint>{{ 'auth.register.passwordHint' | translate:{ min: passwordMin } }}</mat-hint>
              <mat-error>{{ 'auth.register.passwordHint' | translate:{ min: passwordMin } }}</mat-error>
            </mat-form-field>
            <button mat-raised-button color="primary" type="submit" [disabled]="loading()">
              {{ loading() ? ('auth.register.submitting' | translate) : ('auth.register.submit' | translate) }}
            </button>
          </form>
          <!-- Im Formular statt als Snackbar: bleibt stehen, bis der Nutzer etwas tut, und bietet
               beim vergebenen Namen gleich den Weg zur Anmeldung an (Eingaben nimmt das Prefill mit). -->
          @if (error(); as e) {
            <div class="form-error" role="alert">
              <p>{{ errorKey(e) | translate:{ min: passwordMin } }}</p>
              @if (e === 'taken') {
                <a mat-stroked-button color="primary" routerLink="/login" [queryParams]="{ returnUrl: returnUrl }">
                  {{ 'auth.register.toLogin' | translate }}
                </a>
              }
            </div>
          }
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
    .form-error { margin-top: 12px; padding: 12px; border-radius: 8px;
                  background: rgba(211, 47, 47, 0.08); border: 1px solid rgba(211, 47, 47, 0.35); }
    .form-error p { margin: 0 0 8px; }
    .form-error p:last-child { margin-bottom: 0; }
  `]
})
export class RegisterComponent {
  // username/email/password über den Prefill-Service, damit die Eingaben beim
  // Wechsel zum Login (und zurück) erhalten bleiben.
  get username(): string { return this.prefill.username; }
  set username(v: string) { this.prefill.username = v; }
  get email(): string { return this.prefill.email; }
  set email(v: string) { this.prefill.email = v; }
  get password(): string { return this.prefill.password; }
  set password(v: string) { this.prefill.password = v; }
  // Signals statt Felder: nach einer HTTP-Antwort rendert Angular 22 eine unmarkierte View nicht neu —
  // ein im error-Callback gesetztes Feld bliebe unsichtbar (der Knopf hinge auf „Wird registriert …").
  readonly loading = signal(false);
  readonly error = signal<RegisterError | null>(null);
  readonly usernameMin = USERNAME_MIN_LENGTH;
  readonly passwordMin = PASSWORD_MIN_LENGTH;

  returnUrl: string;

  constructor(private auth: AuthService, private prefill: AuthPrefillService, private router: Router, private route: ActivatedRoute) {
    const raw = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
    this.returnUrl = sanitizeReturnUrl(raw);
  }


  errorKey(e: RegisterError): string {
    return ERROR_KEYS[e];
  }

  onSubmit(form?: NgForm): void {
    // Ungültig (zu kurz, leer): gar nicht erst senden — die Felder zeigen selbst, was fehlt.
    if (form?.invalid) {
      form.control.markAllAsTouched();
      return;
    }
    this.error.set(null);
    this.loading.set(true);
    // Email ist optional: leeres Feld als null senden (Backend [EmailAddress] lehnt "" ab, null nicht).
    const email = this.email.trim() || null;
    this.auth.register(this.username, email, this.password).subscribe({
      next: () => {
        this.prefill.clear();
        // navigateByUrl (nicht navigate([...])): returnUrl ist ein kompletter Pfad und kann mehrere
        // Segmente haben (z.B. /tournaments/123) — navigate([...]) würde den Slash url-encoden → 404.
        const sep = this.returnUrl.includes('?') ? '&' : '?';
        this.router.navigateByUrl(`${this.returnUrl}${sep}quickstart=1`);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(registerErrorOf(err));
      }
    });
  }
}
