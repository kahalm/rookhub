import { Component, ChangeDetectionStrategy, Inject, Optional, signal } from '@angular/core';
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
import { sanitizeReturnUrl } from '../../core/return-url.util';
import { LEGAL_SITE, LegalSite, defaultLegalSite } from '../legal/legal-site';
import { apiErrorCodeText } from '../../core/api-error';
import { AUTH_INTRO, AuthIntro } from './auth-intro';

/**
 * Erstes Segment des Ziels → Name des Bereichs aus dem Menü (`nav.*`), damit der Anmelde-Hinweis sagt, wohin es danach
 * geht (UX-027): vorher stand auf 35 geschützten Routen derselbe Satz „… um fortzufahren“, ob der Gast von einer
 * Revanche, einer Partie oder einem Aufgabenblatt kam. Nur Bereiche mit Menüeintrag, sonst bleibt der allgemeine Satz.
 */
const AREA_KEYS: Readonly<Record<string, string>> = {
  dashboard: 'nav.dashboard', friends: 'nav.friends', repertoires: 'nav.repertoires', puzzles: 'nav.puzzles',
  favorites: 'nav.favorites', worksheets: 'nav.worksheets', weekly: 'nav.weekly', analysis: 'nav.analysis',
  games: 'nav.games', reconstruct: 'nav.reconstruct', remembered: 'nav.remembered', stats: 'nav.stats',
  leaderboards: 'nav.leaderboards', 'training-goals': 'nav.trainingGoals', catalog: 'nav.catalog',
  chessable: 'nav.chessable', profile: 'nav.profile', admin: 'nav.admin', courses: 'nav.courses',
  tournaments: 'nav.tournaments',
};

/** i18n-Schlüssel des Bereichs, in den die Anmeldung zurückführt — `null`, wenn es keinen Menünamen dafür gibt. */
export function loginAreaKey(returnUrl: string): string | null {
  const first = returnUrl.split(/[?#]/)[0].split('/').find(s => s) ?? '';
  return Object.prototype.hasOwnProperty.call(AREA_KEYS, first) ? AREA_KEYS[first] : null;
}

/** Was bei der Anmeldung schiefging — steuert Text und nächsten Schritt (UX-019). */
export type LoginError = 'credentials' | 'rateLimited' | 'offline' | 'failed';

/**
 * HTTP-Fehler → Fehlerart. Bisher stand bei jedem Fehler ohne Servertext dasselbe „Anmeldung fehlgeschlagen“ als
 * Snackbar: ein 429 des IP-Limiters (10/min, den sich die Sitzungsprobe jedes Seitenstarts mit der Anmeldung teilt)
 * sah aus wie ein Tippfehler, und wer weiter probierte, verlängerte die Sperre.
 */
export function loginErrorOf(err: any): LoginError {
  if (err?.status === 401 || err?.status === 400) return 'credentials';
  if (err?.status === 429) return 'rateLimited';
  if (err?.status === 0) return 'offline';
  return 'failed';
}

/** Wartezeit eines 429: Rumpf (`retryAfterSeconds`), sonst Header `Retry-After`, sonst das Fenster des Limiters (1 min). */
export function loginRetryAfterSeconds(err: any): number {
  const fromBody = Number(err?.error?.retryAfterSeconds);
  if (Number.isFinite(fromBody) && fromBody > 0) return Math.ceil(fromBody);
  const fromHeader = Number(err?.headers?.get?.('Retry-After'));
  if (Number.isFinite(fromHeader) && fromHeader > 0) return Math.ceil(fromHeader);
  return 60;
}

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
            <!-- UX-027: wohin es nach der Anmeldung geht (Menüname des Ziels) und dass ein Konto nichts kostet. -->
            <p class="auth-required">
              @if (areaKey; as area) {
                {{ 'auth.login.requiredFor' | translate:{ area: (area | translate) } }}
              } @else {
                {{ 'auth.login.required' | translate }}
              }
              @if (showFreeNote) {
                <span class="auth-sub">{{ 'auth.login.freeNote' | translate }}</span>
              }
            </p>
          }
          <!-- LeagueHub (UX-033): wer hier ist, soll wissen, dass es nur für eine Gruppe ist und welches Konto gilt. -->
          @if (leagueHub) {
            <p class="auth-required site-note">{{ 'auth.login.leaguehubNote' | translate }}</p>
          }
          <!-- Turnierseite (UX-027): die Maske ist dort die Startseite — was die Seite bietet und welches Konto gilt. -->
          @if (intro.login; as introKey) {
            <p class="auth-required site-note">{{ introKey | translate }}</p>
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
          <!-- Im Formular statt als Snackbar (UX-019): bleibt stehen, bis der Nutzer etwas tut, und nennt den naechsten
               Schritt — beim falschen Passwort „Passwort vergessen?“, beim Rate-Limit die Wartezeit. -->
          @if (error(); as e) {
            <div class="form-error" role="alert">
              <p>{{ e.text }}</p>
              @if (e.kind === 'credentials') {
                <a mat-stroked-button color="primary" routerLink="/forgot-password">{{ 'auth.login.forgotLink' | translate }}</a>
              }
            </div>
          }
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
    .auth-sub { display: block; margin-top: 0.3rem; opacity: 0.85; }
    .auth-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
    .form-error { margin-top: 12px; padding: 12px; border-radius: 8px;
                  background: rgba(211, 47, 47, 0.08); border: 1px solid rgba(211, 47, 47, 0.35); }
    .form-error p { margin: 0 0 8px; }
    .form-error p:last-child { margin-bottom: 0; }
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
  // Signal statt Feld: nach einer HTTP-Antwort rendert Angular 22 eine unmarkierte View nicht neu (wie bei der Registrierung).
  readonly error = signal<{ kind: LoginError; text: string } | null>(null);

  returnUrl: string;
  authRequired = false;
  /** KidHub hat kein Impressum (siehe LEGAL_SITE). */
  readonly legal: LegalSite;
  /** LeagueHub (LEGAL_SITE.kind): Hinweis auf die geschlossene Gruppe und das RookHub-Konto (UX-033). */
  readonly leagueHub: boolean;
  /** App-eigene Einleitung (Turnierseite, UX-027); RookHub: leer. */
  readonly intro: AuthIntro;
  /** Menüname des Ziels für den Anmelde-Hinweis (UX-027), sonst der allgemeine Satz. */
  readonly areaKey: string | null;
  /** „Konto kostenlos, E-Mail freiwillig“ — nicht auf LeagueHub (Konto allein öffnet dort nichts) und nicht, wo die
   *  Einleitung der Oberfläche das schon sagt. */
  readonly showFreeNote: boolean;

  constructor(private auth: AuthService, private prefill: AuthPrefillService, private router: Router, private route: ActivatedRoute, private translate: TranslateService,
              // Optional + Rueckfall: die Specs bauen die Komponente mit `new`, ausserhalb der DI.
              @Optional() @Inject(LEGAL_SITE) legal?: LegalSite,
              @Optional() @Inject(AUTH_INTRO) intro?: AuthIntro) {
    this.legal = legal ?? defaultLegalSite();
    this.leagueHub = this.legal.kind === 'leaguehub';
    this.intro = intro ?? {};
    const raw = this.route.snapshot.queryParams['returnUrl'] || '/dashboard';
    this.returnUrl = sanitizeReturnUrl(raw);
    this.authRequired = this.route.snapshot.queryParams['authRequired'] === '1';
    this.areaKey = loginAreaKey(this.returnUrl);
    this.showFreeNote = !this.leagueHub && !this.intro.login;
  }


  onSubmit(): void {
    this.loading = true;
    this.error.set(null);
    this.auth.login(this.username, this.password, this.rememberMe).subscribe({
      next: () => {
        this.prefill.clear();
        this.router.navigateByUrl(this.returnUrl);
      },
      error: (err) => {
        this.loading = false;
        const kind = loginErrorOf(err);
        this.error.set({ kind, text: this.errorText(kind, err) });
      }
    });
  }

  /** Text zur Fehlerart in der Sprache der Oberfläche — Servertexte nie roh (die API spricht Englisch). */
  private errorText(kind: LoginError, err: any): string {
    // Der IP-Limiter (code rate_limited oder ohne Rumpf) bekommt die Wartezeit; die Konto-Bremse (login_throttled,
    // wenige Sekunden) und alles andere mit Code (z. B. account_locked) behalten ihren übersetzten Code-Text.
    const byCode = apiErrorCodeText(err, this.translate);
    if (kind === 'rateLimited' && !(err?.error?.code === 'login_throttled' && byCode))
      return this.translate.instant('auth.login.rateLimited', { seconds: loginRetryAfterSeconds(err) });
    if (byCode) return byCode;
    if (kind === 'credentials') return this.translate.instant('apiErrors.login_invalid');
    if (kind === 'offline') return this.translate.instant('auth.login.offline');
    return this.translate.instant('auth.login.failed');
  }
}
