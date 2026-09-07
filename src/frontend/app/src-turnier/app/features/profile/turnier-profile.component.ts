import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { ProfileIdentityFormComponent } from '@rh/shared/profile-identity-form/profile-identity-form.component';
import { ProfileService } from '@rh/core/profile.service';
import { SnackbarService } from '@rh/core/snackbar.service';

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
    ProfileIdentityFormComponent,
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
            <app-help-hint [text]="'turnier.profile.nameHelp' | translate" />
            <app-help-hint [text]="'turnier.profile.identityHelp' | translate" />
          </h2>

          <app-profile-identity-form [profile]="p" />
        </mat-card>

        <div class="actions">
          <button mat-flat-button (click)="save()" [disabled]="saving()">
            {{ 'common.save' | translate }}
          </button>
        </div>
      } @else {
        <p class="muted">{{ 'turnier.profile.loadError' | translate }}</p>
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
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
  `],
})
export class TurnierProfileComponent implements OnInit {
  private readonly profiles = inject(ProfileService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  /** Signale, weil die Antwort ausserhalb der Angular-Zone eintrifft (siehe Turnierkalender). */
  readonly profile = signal<TurnierProfile | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);

  ngOnInit(): void {
    this.profiles.getProfile<TurnierProfile>().subscribe({
      next: profile => {
        this.profile.set(profile);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  save(): void {
    const profile = this.profile();
    if (!profile || this.saving()) return;
    this.saving.set(true);

    // Nur die Felder DIESER Seite. Ein vollstaendiges Profil-Objekt zurueckzuschicken hiesse,
    // die Einstellungen aus RookHub (Brett, Offline-Speicher, Zugaenge) mit dem Stand von hier zu
    // ueberschreiben — und die kennt diese Seite nicht.
    this.profiles.updateProfile<TurnierProfile>({
      email: profile.email ?? '',
      firstName: profile.firstName,
      lastName: profile.lastName,
      displayName: profile.displayName,
      fideId: profile.fideId,
      chessResultsId: profile.chessResultsId,
    }).subscribe({
      next: saved => {
        this.profile.set(saved);
        this.saving.set(false);
        this.snackbar.success(this.translate.instant('profile.saved'));
      },
      error: err => {
        this.saving.set(false);
        const key = err?.status === 409 ? 'profile.emailTaken'
          : err?.status === 400 ? 'profile.emailInvalid'
          : 'turnier.profile.saveError';
        this.snackbar.warn(this.translate.instant(key));
      },
    });
  }
}
