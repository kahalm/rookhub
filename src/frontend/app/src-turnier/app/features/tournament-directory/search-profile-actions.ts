import { WritableSignal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '@rh/core/snackbar.service';
import { SearchProfileDialogComponent, SearchProfileDialogData } from './search-profile-dialog.component';
import { SearchProfileService } from './search-profile.service';
import { SearchProfile } from './tournament-directory.model';

/** Was die Suchprofil-Aktionen vom Turnierkalender brauchen. */
export interface SearchProfileActionsHost {
  readonly dialog: MatDialog;
  readonly snackbar: SnackbarService;
  readonly translate: TranslateService;
  readonly service: SearchProfileService;
  /** Die Profile der Leiste — die Aktionen pflegen sie mit. */
  readonly profiles: WritableSignal<SearchProfile[]>;
  /** Das gerade gewaehlte Profil (`filter.profileId`). */
  activeId(): number | null;
  /** Ein Profil (oder keines) waehlen — `TournamentDirectoryComponent.onProfileChange`. */
  select(profileId: number | null): void;
}

/**
 * Anlegen, Bearbeiten und Loeschen (mit Rueckgaengig) der Suchprofile des Turnierkalenders.
 * Herausgezogen aus `TournamentDirectoryComponent` (Zerlegung Z28), Verhalten unveraendert; wer
 * ohne Konto „Neues Suchprofil" waehlt, wird vorher in der Komponente zur Anmeldung geschickt.
 */
export class SearchProfileActions {
  constructor(private readonly host: SearchProfileActionsHost) {}

  /**
   * Der Dialog speichert selbst und schliesst mit dem GESPEICHERTEN Profil — ein Fehler (Name
   * vergeben, Obergrenze) bleibt im Dialog samt Eingaben stehen (Codereview 2026-09-29, F6-003).
   */
  open(profile: SearchProfile | null): void {
    const data: SearchProfileDialogData = { profile };
    this.host.dialog.open<SearchProfileDialogComponent, SearchProfileDialogData, SearchProfile | null>(
      SearchProfileDialogComponent, { data, width: '460px' })
      .afterClosed().subscribe(saved => {
        if (!saved) return;
        this.host.profiles.update(list => (profile
          ? list.map(p => (p.id === saved.id ? saved : p))
          : [...list, saved]));
        this.host.select(saved.id);
      });
  }

  /**
   * Suchprofil loeschen — mit Rueckgaengig (Codereview 2026-09-29, F6-014). „Loeschen" steht im
   * Menue direkt unter „Bearbeiten"; ein Fehlgriff nahm Ort, Radius, Filter und die naechtliche
   * Meldung ohne Rueckweg mit. Wie „Nicht mehr merken" in der Merkliste kein Bestaetigungsdialog,
   * sondern 6 s Rueckgaengig: es legt das Profil mit denselben Feldern neu an (neue Id) und setzt es
   * an seinen alten Platz; war es gewaehlt, ist es danach wieder gewaehlt. Der naechtliche Sweep
   * meldet nur neu gefundene Turniere, ein neu angelegtes Profil loest also keine Nachmeldung aus.
   */
  remove(profile: SearchProfile): void {
    const { service, profiles, snackbar, translate } = this.host;
    service.remove(profile.id).subscribe({
      next: () => {
        const index = profiles().findIndex(p => p.id === profile.id);
        const wasActive = this.host.activeId() === profile.id;
        profiles.update(list => list.filter(p => p.id !== profile.id));
        if (wasActive) this.host.select(null);
        const ref = snackbar.show(
          translate.instant('tournamentDirectory.profile.deleted', { name: profile.name }),
          { action: 'common.undo', duration: 6000 });
        ref.onAction().subscribe(() => this.restore(profile, index, wasActive));
      },
      error: () => snackbar.warn(translate.instant('tournamentDirectory.profile.deleteError')),
    });
  }

  private restore(profile: SearchProfile, index: number, reselect: boolean): void {
    const { id: _id, ...input } = profile;
    this.host.service.create(input).subscribe({
      next: created => {
        this.host.profiles.update(list => {
          const next = [...list];
          next.splice(index < 0 ? next.length : Math.min(index, next.length), 0, created);
          return next;
        });
        if (reselect) this.host.select(created.id);
      },
      error: () => this.host.snackbar.warn(this.host.translate.instant('tournamentDirectory.profile.undoFailed')),
    });
  }
}
