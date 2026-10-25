import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe } from '@ngx-translate/core';
import { SearchProfile } from './tournament-directory.model';

/**
 * Das ⋮-Menue der Filterleiste: Suchprofil waehlen, neu anlegen, bearbeiten, loeschen. Reine
 * Darstellung (Zerlegung Z28) — Dialog, Speichern und Rueckgaengig liegen in
 * `TournamentDirectoryComponent` (`newProfile`, `editProfile`, `deleteProfile`, `onProfileChange`).
 */
@Component({
  selector: 'app-search-profile-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <button mat-icon-button [matMenuTriggerFor]="profileMenu"
            [attr.aria-label]="'tournamentDirectory.profile.manage' | translate">
      <mat-icon>more_vert</mat-icon>
    </button>
    <mat-menu #profileMenu="matMenu">
      <!-- Ohne Anmeldung fuehrt „Neues Suchprofil" zur Anmeldung (requireAccount) — Filter speichern braucht ein Konto. -->
      @if (profiles().length > 0) {
        <button mat-menu-item [matMenuTriggerFor]="profilePick">
          <mat-icon>bookmarks</mat-icon>
          <span>{{ 'tournamentDirectory.profile.label' | translate }}</span>
        </button>
      }
      <button mat-menu-item (click)="create.emit()">
        <mat-icon>add_location_alt</mat-icon>
        <span>{{ 'tournamentDirectory.profile.new' | translate }}</span>
      </button>
      @if (activeProfile(); as active) {
        <button mat-menu-item (click)="edit.emit(active)">
          <mat-icon>edit</mat-icon>
          <span>{{ 'common.edit' | translate }}</span>
        </button>
        <button mat-menu-item (click)="remove.emit(active)">
          <mat-icon>delete</mat-icon>
          <span>{{ 'tournamentDirectory.profile.delete' | translate }}</span>
        </button>
      }
    </mat-menu>

    <mat-menu #profilePick="matMenu">
      <button mat-menu-item (click)="pick.emit(null)">
        {{ 'tournamentDirectory.profile.none' | translate }}
      </button>
      @for (profile of profiles(); track profile.id) {
        <button mat-menu-item (click)="pick.emit(profile.id)">
          {{ profile.name }} · {{ profile.radiusKm }} km
        </button>
      }
    </mat-menu>
  `,
  styles: [`:host { display: contents; }`],
})
export class SearchProfileMenuComponent {
  readonly profiles = input<SearchProfile[]>([]);
  /** Das gewaehlte Profil — nur dann gibt es „Bearbeiten" und „Loeschen". */
  readonly activeProfile = input<SearchProfile | null>(null);

  /** Ein Profil (oder „kein Profil" = null) wurde gewaehlt. */
  readonly pick = output<number | null>();
  readonly create = output<void>();
  readonly edit = output<SearchProfile>();
  readonly remove = output<SearchProfile>();
}
