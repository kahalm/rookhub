import { Component, ChangeDetectionStrategy, Inject, Optional } from '@angular/core';
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
import { AuthPrefillService } from '../../core/auth-prefill.service';
import { SnackbarService } from '../../core/snackbar.service';
import { sanitizeReturnUrl } from '../../core/return-url.util';
import { LEGAL_SITE, LegalSite, defaultLegalSite } from '../legal/legal-site';
import { apiErrorCodeText } from '../../core/api-error';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
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
          <!-- LeagueHub (UX-033): wer hier ist, soll wissen, dass es nur für eine Gruppe ist und welches Konto gilt. -->
          @if (leagueHub) {
            <p class="auth-required site-note">{{ 'auth.login.leaguehubNote' | translate }}</p>
          }
          <form (ngSubmit)="onSubmit()" class="auth-form">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.login.usernameLabel' | translate }}</mat-label>
              <!-- Handy-Tastatur: iOS setzte den ersten Buchstaben gross und die Autokorrektur machte aus dem
                   Benutzernamen ein Woerterbuchwort; autocomplete gibt dem Passwort-Manager den Fuell-Hinweis. -->
              <input matInput [(ngModel)]="username" name="username" required autofocus
                     autocomplete="username" autocapitalize="none" autocorrect="off" spellcheck="false">
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.login.passwordLabel' | translate }}</mat-label>
              <input matInput type="password" [(ngModel)]="password" name="password" required autocomplete="current-password">
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
        @if (legal.imprint) {
          <span>·</span>
          <a routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</a>
        }
      </div>
    </div>
  `,
  styles: [`
    .auth-container { display: flex; flex-direction: column; justify-content: center; align-items: center; min-height: 80vh; }
    .legal-links { margin-top: 1rem; text-align: center; font-size: 0.8rem; }
    /* Theme-Token statt festem Hellblau: im hellen Modus war #90caf9 auf Weiss praktisch unsichtbar.
       Beruehrziel 45px hoch (15px Padding, mindestens 44px – UX-017) fuer den Daumen; das negative Margin haelt die
       Layouthoehe bei 15px, damit sich nichts verschiebt (die Karte endet 16px hoeher, es gibt keine Ueberlappung). */
    .legal-links a { color: var(--mat-sys-primary); display: inline-block; padding: 15px 4px; margin: -15px 0; }
    .legal-links span { color: color-mix(in srgb, currentColor 53%, transparent); margin: 0 6px; }
    mat-card { width: 400px; max-width: 90vw; }
    .auth-required { background: rgba(144, 202, 249, 0.15); border-left: 3px solid #90caf9; padding: 0.6rem 0.8rem; border-radius: 4px; margin: 0.5rem 0 0; font-size: 0.9rem; }
    .auth-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
    /* Handy: 'Noch kein Konto? Registrieren' und 'Passwort vergessen?' passen nicht nebeneinander und brachen
       innerhalb des 40px-Buttons zweizeilig um (bei grosser Systemschrift lief der Text aus dem Kasten). */
    @media (max-width: 768px) { mat-card-actions { flex-wrap: wrap; } }
  `]
})
export class LoginComponent {
  // username/password über den Prefill-Service, damit die Eingaben beim
  // Wechsel zur Registrierung (und zurück) erhalten bleiben.
  get username(): string { return this.prefill.username; }
  set username(v: string) { this.prefill.username = v; }
  get password(): string { return this.prefill.password; }
  set password(v: string) { this.prefill.password = v; }
  rememberMe = false;
  loading = false;

  returnUrl: string;
  authRequired = false;
  /** KidHub hat kein Impressum (siehe LEGAL_SITE). */
  readonly legal: LegalSite;
  /** LeagueHub (LEGAL_SITE.kind): Hinweis auf die geschlossene Gruppe und das RookHub-Konto (UX-033). */
  readonly leagueHub: boolean;

  constructor(private auth: AuthService, private prefill: AuthPrefillService, private router: Router, private route: ActivatedRoute, private snackbar: SnackbarService, private translate: TranslateService,
              // Optional + Rueckfall: die Specs bauen die Komponente mit `new`, ausserhalb der DI.
              @Optional() @Inject(LEGAL_SITE) legal?: LegalSite) {
    this.legal = legal ?? defaultLegalSite();
    this.leagueHub = this.legal.kind === 'leaguehub';
    const raw = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
    this.returnUrl = sanitizeReturnUrl(raw);
    this.authRequired = this.route.snapshot.queryParams['authRequired'] === '1';
  }


  onSubmit(): void {
    this.loading = true;
    this.auth.login(this.username, this.password, this.rememberMe).subscribe({
      next: () => {
        this.prefill.clear();
        this.router.navigateByUrl(this.returnUrl);
      },
      error: (err) => {
        this.loading = false;
        const msg = apiErrorCodeText(err, this.translate)
          || err.error?.message
          || (err.error?.errors && Object.values(err.error.errors).flat().join(' '))
          || this.translate.instant('auth.login.failed');
        this.snackbar.warn(msg);
      }
    });
  }
}
