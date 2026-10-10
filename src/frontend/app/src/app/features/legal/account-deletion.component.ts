import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { HandoffService } from '../../core/handoff.service';
import { ACCOUNT_DELETE_QUERY, ACCOUNT_DELETE_ROUTE, LEGAL_SITE, legalBackLink } from './legal-site';

/**
 * Öffentlich (ohne Login) erreichbare Info-Seite zur Konto-Löschung — erfüllt die
 * Google-Play-Anforderung einer öffentlich zugänglichen URL zur Löschanforderung.
 * Route: /account-deletion
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-account-deletion',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, RouterModule, TranslatePipe],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header>
          <h1 mat-card-title>{{ 'legal.accountDeletion.title' | translate }}</h1>
        </mat-card-header>
        <mat-card-content>
          <p>{{ (home ? 'legal.accountDeletion.introPartner' : 'legal.accountDeletion.intro') | translate }}</p>

          <h2>{{ 'legal.accountDeletion.inAppTitle' | translate }}</h2>
          <p>{{ (home ? 'legal.accountDeletion.inPartner' : 'legal.accountDeletion.inApp') | translate }}</p>

          <!-- Vor dem Knopf: sofort und ohne Rueckgaengig — die PGN-Exporte gibt es je Kurs, Repertoire und Partie (UX-021). -->
          <p class="backup"><strong>{{ 'legal.accountDeletion.backupTitle' | translate }}:</strong>
            {{ 'legal.accountDeletion.backup' | translate }}</p>
          @if (!home) {
            <p class="backup-links">
              @for (l of exportLinks; track l.path) {
                <a [routerLink]="l.path" data-login-required>{{ l.label | translate }}</a>
              }
            </p>
          } @else if (homeUrl) {
            <p class="backup-links">
              @for (l of exportLinks; track l.path) {
                <a [href]="homeUrl + l.path">{{ l.label | translate }}</a>
              }
            </p>
          }

          @if (!home) {
            <!-- Der eine Knopf zur Karte im Profil; abgemeldet fuehrt der authGuard ueber die Anmeldung dorthin
                 (data-login-required: die Routen-Specs lassen diesen Link deshalb eine Anmeldung verlangen). -->
            <p class="action">
              <a mat-flat-button color="warn" class="delete-now" data-login-required
                 [routerLink]="deleteRoute" [queryParams]="deleteQuery">{{ 'legal.accountDeletion.deleteNow' | translate }}</a>
            </p>
          } @else if (homeDeleteUrl) {
            <!-- KidHub, LeagueHub, ClubHub, Turnierseite: dasselbe Konto, geloescht wird es in RookHub. -->
            <p class="action">
              <a mat-flat-button color="warn" class="delete-now" [href]="homeDeleteUrl"
                 (click)="openOnHome($event)">{{ 'legal.accountDeletion.deleteOnRookHub' | translate }}</a>
            </p>
          }

          <!-- Was ProfileService.DeleteAccountAsync wirklich loescht, in Nutzersprache gruppiert (UX-021). -->
          <h2>{{ 'legal.accountDeletion.removedTitle' | translate }}</h2>
          <ul class="removed">
            @for (key of removedKeys; track key) {
              <li>{{ 'legal.accountDeletion.' + key | translate }}</li>
            }
          </ul>

          <h2>{{ 'legal.accountDeletion.keptTitle' | translate }}</h2>
          <p>{{ 'legal.accountDeletion.kept' | translate }}</p>
          @if (league) {
            <!-- LeagueHub: hochgeladene Vereinspartien bleiben (mit Namen, also nicht „anonym"), ohne Vermerk des
                 Hochladenden — selbst loeschen geht danach nicht mehr (CanDelete: Verwalter oder der Hochladende). -->
            <h2>{{ 'legal.accountDeletion.keptLeagueTitle' | translate }}</h2>
            <p class="league-kept">{{ 'legal.accountDeletion.keptLeague' | translate }}
              <a routerLink="/verein" data-login-required>{{ 'legal.accountDeletion.leagueGamesLink' | translate }}</a></p>
            <p>{{ 'legal.accountDeletion.keptLeagueShares' | translate }}</p>
          }

          <h2>{{ 'legal.accountDeletion.contactTitle' | translate }}</h2>
          <p>
            {{ 'legal.accountDeletion.contact' | translate }}:
            <a [href]="'mailto:' + site.contactEmail">{{ site.contactEmail }}</a>
          </p>

          <p class="back">
            <!-- Aus der App gekommen: ein Schritt zurueck; direkt aufgerufen: das Ersatzziel (UX-017). -->
            @if (back.history) {
              <a [href]="back.href" (click)="back.go($event)">{{ back.label | translate }}</a>
            } @else {
              <a [routerLink]="back.link">{{ back.label | translate }}</a>
            }
          </p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .legal-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { max-width: 720px; width: 100%; }
    /* Theme-Token statt festem Hellblau/-grau (F7-015, wie die Anmeldemaske): #90caf9 hatte im hellen Modus — KidHub
       immer, RookHub/LeagueHub auf Wunsch — 1,75:1 auf Weiss, #bdbdbd 1,9:1. */
    /* Abschnitte als h2 unter dem h1-Titel (UX-057) — Aussehen wie vorher als h4. */
    h2 { margin: 1.25rem 0 0.25rem; font-size: 1em; font-weight: bold; color: var(--mat-sys-primary); }
    a { color: var(--mat-sys-primary); }
    .back { margin-top: 1.5rem; }
    .back a::before { content: '← ' / ''; }
    .action { margin: 0.75rem 0 0.25rem; }
    .backup-links { display: flex; flex-wrap: wrap; gap: 0.25rem 1rem; margin-top: -0.25rem; }
    .backup-links a, .league-kept a { display: inline-block; padding: 10px 0; }
  `]
})
export class AccountDeletionComponent {
  /** Kontakt je Oberflaeche (KidHub: eigene Adresse, siehe LEGAL_SITE). */
  readonly site = inject(LEGAL_SITE);
  /** „Zurueck" dorthin, wo man herkam; direkt aufgerufen zur Anmeldung (KidHub: Startseite). */
  readonly back = legalBackLink('legal.accountDeletion.back');

  private readonly handoff = inject(HandoffService);
  /** Wird das Konto woanders gefuehrt (KidHub, LeagueHub, ClubHub, Turnierseite → RookHub)? Sonst ist hier RookHub. */
  readonly home = this.site.accountHome ?? null;
  /** Ziel in RookHub: das Profil mit aufgeklappter Karte „Konto loeschen" (Codereview UX-023). */
  readonly deleteRoute = ACCOUNT_DELETE_ROUTE;
  readonly deleteQuery = ACCOUNT_DELETE_QUERY;
  /** RookHub dieser Oberflaeche (ohne Schraegstrich am Ende), `null` auf RookHub selbst und ohne bekannte Adresse. */
  readonly homeUrl = this.home ? this.handoff.accountHomeUrl : null;
  /** LeagueHub: was mit Vereinspartien, Entwuerfen und Teilen-Links geschieht (UX-021). */
  readonly league = this.site.kind === 'leaguehub';
  /** „Was entfernt wird" — Gruppen aus DeleteAccountAsync (eigene Kurse samt Freigaben und fremdem Fortschritt,
   *  Partien und Fotos, Aufgabenblaetter und Teilen-Links, KidHub, Verbindungen); LeagueHub zusaetzlich die Entwuerfe. */
  readonly removedKeys = [
    'removed1', 'removedCourses', 'removed2', 'removedGames', 'removedShared', 'removedKids', 'removedConnections',
    ...(this.league ? ['removedLeague'] : []),
  ];
  /** Wo es die PGN-Exporte gibt: je Kurs, in der Repertoire-Liste und in jeder Partie. */
  readonly exportLinks = [
    { path: '/courses', label: 'nav.courses' },
    { path: '/repertoires', label: 'nav.repertoires' },
    { path: '/games', label: 'nav.games' },
  ];
  /** `profile?section=delete` — ohne fuehrenden Schraegstrich, wie ihn der Sprung erwartet. */
  private readonly deletePath = `${ACCOUNT_DELETE_ROUTE.slice(1)}?${new URLSearchParams(ACCOUNT_DELETE_QUERY)}`;
  /** Dasselbe Ziel als Adresse auf RookHub — `null` ohne bekannte Adresse (localhost, IP): dann nur der Text. */
  readonly homeDeleteUrl = this.homeUrl ? `${this.homeUrl}/${this.deletePath}` : null;

  /** Angemeldet nimmt der Sprung die Anmeldung mit (Einmal-Code), sonst meldet man sich drueben an. Strg/Mittelklick
   *  (neuer Tab) bleibt beim schlichten Link. */
  openOnHome(e: MouseEvent): void {
    if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    void this.handoff.jumpToAccountHome(this.deletePath);
  }
}
