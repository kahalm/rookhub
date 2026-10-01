import { Component, ChangeDetectionStrategy, Inject, Optional, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { AuthPrefillService } from '../../core/auth-prefill.service';
import { sanitizeReturnUrl } from '../../core/return-url.util';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { LEGAL_SITE, LegalSite, defaultLegalSite } from '../legal/legal-site';

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
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, HelpHintComponent, TranslatePipe],
  template: `
    <div class="auth-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'auth.register.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <!-- KidHub (UX-032): hier registriert sich meist ein Kind. Ein Satz an Kind und Eltern, der Rest hinter dem
               Hilfe-Icon; nur Hinweise, kein Pflicht-Haken (Entscheidung beim Betreiber, Art. 8 DSGVO). -->
          @if (kids) {
            <div class="kids-parents" role="note">
              <strong>{{ 'auth.register.kids.parentsTitle' | translate }}</strong>
              <app-help-hint [text]="'auth.register.kids.parentsHelp' | translate"></app-help-hint>
              <p>{{ 'auth.register.kids.parentsText' | translate }}</p>
            </div>
          }
          <form #f="ngForm" (ngSubmit)="onSubmit(f)" class="auth-form">
            <mat-form-field appearance="outline" [subscriptSizing]="kids ? 'dynamic' : 'fixed'">
              <mat-label>{{ 'auth.register.usernameLabel' | translate }}</mat-label>
              <!-- Handy-Tastatur: ohne autocapitalize="none" wurde der Benutzername als 'Kahalm' statt 'kahalm'
                   eingegeben und so gespeichert; new-password laesst den Passwort-Manager ein starkes Passwort vorschlagen. -->
              <input matInput [(ngModel)]="username" name="username" required [minlength]="usernameMin"
                     autocomplete="username" autocapitalize="none" autocorrect="off" spellcheck="false">
              @if (kids) {
                <!-- Der Benutzername steht fuer andere sichtbar (Bestenliste ohne Anzeigenamen, Freundessuche) — ein
                     Kind tippt sonst naheliegend Vor- und Nachnamen. -->
                <mat-hint>{{ 'auth.register.kids.usernameHint' | translate }}</mat-hint>
              }
              <mat-error>{{ 'auth.register.usernameTooShort' | translate:{ min: usernameMin } }}</mat-error>
            </mat-form-field>
            <!-- Der Hinweis nennt die Folge (UX-002): ohne E-Mail laesst sich ein vergessenes Passwort nie zuruecksetzen —
                 „Passwort vergessen“ nimmt nur eine E-Mail an. dynamic: der laengere Hinweis darf umbrechen. -->
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <!-- KidHub: Kinder haben meist keine eigene Adresse — die eines Elternteils ist die richtige Wahl. -->
              <mat-label>{{ (kids ? 'auth.register.kids.emailLabel' : 'auth.register.emailLabel') | translate }}</mat-label>
              <input matInput type="email" [(ngModel)]="email" name="email" email autocomplete="email">
              <mat-hint>{{ (kids ? 'auth.register.kids.emailHint' : 'auth.register.emailHint') | translate }}</mat-hint>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.register.passwordLabel' | translate }}</mat-label>
              <!-- Anzeigen-Schalter (UX-002): es gibt keine Wiederholung, ein unbemerkter Tippfehler sperrte das neue Konto
                   aus — ohne E-Mail endgueltig. -->
              <input matInput [type]="showPassword() ? 'text' : 'password'" [(ngModel)]="password" name="password" required [minlength]="passwordMin" autocomplete="new-password">
              <button mat-icon-button matSuffix type="button" class="pw-toggle" (click)="showPassword.set(!showPassword())"
                      [attr.aria-pressed]="showPassword()"
                      [attr.aria-label]="(showPassword() ? 'auth.register.hidePassword' : 'auth.register.showPassword') | translate">
                <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
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
          <!-- Datenschutz direkt an der Maske, die die Angaben abfragt (UX-017): bisher verlinkte nur /login die
               Datenschutzerklaerung. Ganzer Satz als Link: kein Satzbau-Problem in anderen Sprachen, und der Linktext
               sagt fuer sich, wohin er fuehrt. Die Route /privacy haben alle Oberflaechen. -->
          <p class="privacy-note">
            <a routerLink="/privacy">{{ (kids ? 'auth.register.kids.privacyNote' : 'auth.register.privacyNote') | translate }}</a>
          </p>
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
    .kids-parents { margin: 0.5rem 0 0; padding: 0.6rem 0.8rem; border-radius: 4px; font-size: 0.9rem;
                    background: color-mix(in srgb, var(--mat-sys-primary) 10%, transparent);
                    border-left: 3px solid var(--mat-sys-primary); }
    .kids-parents p { margin: 0.25rem 0 0; }
    .privacy-note { margin: 16px 0 0; font-size: 0.8rem; text-align: center; }
    /* Wie die Rechtslinks unter der Anmeldekarte: Theme-Farbe, Beruehrflaeche mindestens 44px (UX-017) ohne Layoutsprung. */
    .privacy-note a { color: var(--mat-sys-primary); display: inline-block; padding: 15px 4px; margin: -15px 0; }
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
  /** Passwort im Klartext zeigen (Schalter am Feld). */
  readonly showPassword = signal(false);
  readonly usernameMin = USERNAME_MIN_LENGTH;
  readonly passwordMin = PASSWORD_MIN_LENGTH;
  /** Kinderseite (LEGAL_SITE.kind): Eltern-Hinweis, Eltern-E-Mail, Spitzname statt Klarname (UX-032). */
  readonly kids: boolean;

  returnUrl: string;

  constructor(private auth: AuthService, private prefill: AuthPrefillService, private router: Router, private route: ActivatedRoute,
              // Optional + Rueckfall: die Specs bauen die Komponente mit `new`, ausserhalb der DI.
              @Optional() @Inject(LEGAL_SITE) legal?: LegalSite) {
    this.kids = (legal ?? defaultLegalSite()).kind === 'kidhub';
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
