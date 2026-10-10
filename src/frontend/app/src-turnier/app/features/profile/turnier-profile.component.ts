import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import {
  ProfileIdentityFormComponent, emailAnchorChanged,
} from '@rh/shared/profile-identity-form/profile-identity-form.component';
import { AuthService } from '@rh/core/auth.service';
import { ProfileService } from '@rh/core/profile.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { RookHubLinkComponent } from '../../shell/rookhub-link.component';

/**
 * Was hier gepflegt werden kann: Name, Anzeigename, E-Mail — und die beiden Spielerkennungen.
 */
interface TurnierProfile {
  username: string;
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  displayName: string | null;
  fideId: string | null;
  chessResultsId: string | null;
}

/**
 * Die Profilseite der Turnierseite.
 *
 * <p><b>Warum sie hier eigenstaendig ist.</b> Beide Oberflaechen teilen Konto, Datenbank und API,
 * aber RookHubs Profilseite ist eine Sammlung aus Chessable-Zugang, Engine-Token, API-Tokens,
 * Brett-Einstellungen und Offline-Speicher — das hat auf einer Turnierseite nichts zu tun. Was
 * hier gebraucht wird, ist ein kleiner Teil davon, und der geht ueber denselben Endpunkt
 * (`PUT /api/profile`): eine Aenderung hier steht auch in RookHub.</p>
 *
 * <p><b>Der Name ist kein Beiwerk.</b> Der Turnierverlauf sucht auf chess-results ueber den
 * NAMEN — es gibt dort keine Suche ueber eine Kennung. Ohne Nachnamen im Profil hat diese Seite
 * also keinen Verlauf, und genau deshalb steht sie an dieser Stelle. Die FIDE-ID entscheidet
 * danach, welche der Namensgleichen gemeint ist; ohne sie sieht man womoeglich fremde Turniere,
 * und der Hinweistext sagt das.</p>
 */
@Component({
  selector: 'app-turnier-profile',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule,
    MatInputModule, TranslatePipe, LoadingSpinnerComponent, HelpHintComponent,
    ProfileIdentityFormComponent, RouterLink, RookHubLinkComponent,
  ],
  template: `
    <div class="page">
      <h1>{{ 'turnier.profile.title' | translate }}</h1>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (profile(); as p) {
        <!-- Dieselben sechs Felder samt Spielersuche wie in RookHub — EINE Komponente
             (shared/profile-identity-form), nicht zwei Formulare mit demselben Inhalt. Die
             Suche ist der Grund, warum das hier mehr ist als ein Aufraeumen: sie fuellt die
             Kennungen, und an denen haengt der Turnierverlauf. -->
        <mat-card class="card">
          <h2>
            {{ 'turnier.profile.person' | translate }}
            <!-- EIN Hilfe-Icon fuer Name UND Kennungen, wie in training-goals. Zwei gleiche
                 ?-Icons nebeneinander (22 px, 7 px Abstand) sind nicht unterscheidbar — auf dem
                 Handy trifft man zufaellig eines und sieht nur die halbe Erklaerung. -->
            <app-help-hint [text]="('turnier.profile.nameHelp' | translate) + '\n\n' + ('turnier.profile.identityHelp' | translate)" />
          </h2>

          <app-profile-identity-form [profile]="p" [savedEmail]="savedEmail()"
                                     [(currentPassword)]="currentPassword" />
        </mat-card>

        <div class="actions">
          <button mat-flat-button (click)="save()" [disabled]="saving()">
            {{ 'common.save' | translate }}
          </button>
        </div>
      } @else {
        <!-- Ohne Formular und ohne Knopf blieb nur Neuladen — in der PWA unsichtbar (UX-074). -->
        <mat-card class="card failed" role="alert">
          <p>{{ 'turnier.profile.loadError' | translate }}</p>
          <button mat-stroked-button (click)="load()">
            <mat-icon>refresh</mat-icon> {{ 'common.retry' | translate }}
          </button>
        </mat-card>
      }

      <!-- Passwort und Loeschen gibt es nur in RookHubs Profil (dasselbe Konto). Ohne Verweis an
           dieser Stelle fand ein reiner Turnierseiten-Nutzer beides nicht (Codereview UX-079). Auch
           wenn das Profil nicht laden konnte: gerade dann sucht man womoeglich das Passwort. -->
      @if (!loading()) {
        <mat-card class="card spaced account">
          <h2>{{ 'turnier.profile.accountTitle' | translate }}</h2>
          <p class="muted">{{ 'turnier.profile.accountText' | translate }}</p>
          <div class="account-links">
            <!-- Eine Zeile, mittig: Passwort als Umriss-Knopf, Loeschen als roter Text-Knopf — es ist die gefaehrliche
                 der beiden Aktionen und darf nicht gleichrangig aussehen (UI-Sweep t-profile-links). -->
            <trn-rookhub-link class="pw-link" path="profile">{{ 'turnier.profile.changePassword' | translate }}</trn-rookhub-link>
            <!-- Die eigene Loeschseite erklaert, was verschwindet, und fuehrt dann nach RookHub. -->
            <a mat-button routerLink="/account-deletion" class="delete-link">{{ 'turnier.profile.deleteAccount' | translate }}</a>
          </div>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .page {
      max-width: min(var(--page-max-width), 96vw);
      margin: 0 auto;
      padding: 1rem;
    }

    h1 { font-size: 1.4rem; margin: 0 0 1rem; }

    h2 {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      font-size: 1rem;
      margin: 0 0 0.75rem;
    }

    .card { padding: 1rem; }
    .card.spaced { margin-top: 1rem; }

    .row { display: flex; flex-wrap: wrap; gap: 0.75rem; }
    .row mat-form-field { flex: 1 1 200px; }

    .full { width: 100%; }
    /* Der Hinweistext unter einem Feld ist mehrzeilig — ohne Abstand laeuft er ins naechste. */
    .spaced { margin-top: 1.25rem; }

    .actions { display: flex; justify-content: flex-end; margin-top: 1rem; }
    .account p { margin: 0 0 0.5rem; }
    .account-links { display: flex; flex-wrap: wrap; justify-content: center; align-items: center; gap: 0.5rem 1rem; }
    .pw-link ::ng-deep .rh-link {
      display: inline-flex; align-items: center; height: 40px; padding: 0 24px; box-sizing: border-box;
      border: 1px solid var(--mat-sys-outline); border-radius: 20px;
      text-decoration: none; font-weight: 500; font-size: 0.875rem;
    }
    .pw-link ::ng-deep .rh-link:hover { background: color-mix(in srgb, var(--mat-sys-primary) 8%, transparent); }
    .delete-link.mat-mdc-button { color: var(--rh-error); }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .card.failed { display: flex; flex-direction: column; align-items: flex-start; gap: 0.75rem; }
    .card.failed p { margin: 0; }
  `],
})
export class TurnierProfileComponent implements OnInit {
  private readonly profiles = inject(ProfileService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  /** Signale, weil die Antwort ausserhalb der Angular-Zone eintrifft (siehe Turnierkalender). */
  readonly profile = signal<TurnierProfile | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  /** Zuletzt gespeicherte E-Mail — Vergleichsstand fuer „Adresse geaendert, Passwort noetig". */
  readonly savedEmail = signal<string | null>(null);
  /** Aktuelles Passwort, nur fuer einen E-Mail-Wechsel (Feld im Identitaets-Formular). */
  readonly currentPassword = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.profiles.getProfile<TurnierProfile>().subscribe({
      next: profile => {
        this.profile.set(profile);
        this.savedEmail.set(profile?.email ?? null);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  save(): void {
    const profile = this.profile();
    if (!profile || this.saving()) return;

    // Ein E-Mail-WECHSEL (auch Erst-Setzen/Entfernen) braucht das aktuelle Passwort, sonst 403.
    // Unter Impersonation sperrt der Server die Adresse ohnehin — dort gibt es kein Feld.
    const askPassword = !this.auth.isImpersonating
      && emailAnchorChanged(this.savedEmail(), profile.email);
    if (askPassword && !this.currentPassword()) {
      this.snackbar.warn(this.translate.instant('profile.emailPasswordRequired'));
      return;
    }
    this.saving.set(true);

    // Nur die Felder DIESER Seite. Ein vollstaendiges Profil-Objekt zurueckzuschicken hiesse,
    // die Einstellungen aus RookHub (Brett, Offline-Speicher, Zugaenge) mit dem Stand von hier zu
    // ueberschreiben — und die kennt diese Seite nicht.
    this.profiles.updateProfile<TurnierProfile>({
      email: profile.email ?? '',
      // Nur bei einem Wechsel: bei unveraenderter Adresse hat das Passwort im Body nichts zu suchen.
      ...(askPassword ? { currentPassword: this.currentPassword() } : {}),
      firstName: profile.firstName,
      lastName: profile.lastName,
      displayName: profile.displayName,
      fideId: profile.fideId,
      chessResultsId: profile.chessResultsId,
    }).subscribe({
      next: saved => {
        this.profile.set(saved);
        this.savedEmail.set(saved?.email ?? null);
        this.currentPassword.set('');
        this.saving.set(false);
        this.snackbar.success(this.translate.instant('profile.saved'));
      },
      error: err => {
        this.saving.set(false);
        // 403 bei einem Wechsel = Passwort falsch (leer faengt die Pruefung oben ab); die
        // Impersonations-403 faellt nicht hierher (askPassword ist dort false).
        const passwordRejected = err?.status === 403 && askPassword;
        if (passwordRejected) this.currentPassword.set('');
        const key = passwordRejected ? 'profile.emailPasswordWrong'
          : err?.status === 409 ? 'profile.emailTaken'
          : err?.status === 400 ? 'profile.emailInvalid'
          : 'turnier.profile.saveError';
        this.snackbar.warn(this.translate.instant(key));
      },
    });
  }
}
