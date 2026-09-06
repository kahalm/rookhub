import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSliderModule } from '@angular/material/slider';
import { TranslatePipe } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TournamentDirectoryService } from './tournament-directory.service';
import { GeoPlaceSuggestion, SearchProfile, SearchProfileInput } from './tournament-directory.model';

export interface SearchProfileDialogData {
  profile: SearchProfile | null;
}

/**
 * Suchprofil anlegen oder aendern. Der Ort wird ueber den Gazetteer aufgeloest statt frei
 * eingetippt: der Server braucht Koordinaten, um nachts ohne Browser rechnen zu koennen — ein
 * blosser Ortsname wuerde die Benachrichtigung stumm lassen.
 */
@Component({
  selector: 'app-search-profile-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatSliderModule, TranslatePipe,
  ],
  templateUrl: './search-profile-dialog.component.html',
  styleUrls: ['./search-profile-dialog.component.scss'],
})
export class SearchProfileDialogComponent {
  readonly speeds = ['Standard', 'Rapid', 'Blitz'];

  name = '';
  placeQuery = '';
  lat: number | null = null;
  lon: number | null = null;
  radiusKm = 100;
  selectedSpeeds: string[] = [];
  weekendOnly = false;
  minPlayers: number | null = null;
  notifyNew = true;

  suggestions: GeoPlaceSuggestion[] = [];
  searching = false;
  error: string | null = null;

  private readonly placeTerm$ = new Subject<string>();

  constructor(
    private directory: TournamentDirectoryService,
    private dialogRef: MatDialogRef<SearchProfileDialogComponent, SearchProfileInput | null>,
    @Inject(MAT_DIALOG_DATA) public data: SearchProfileDialogData,
  ) {
    const profile = data.profile;
    if (profile) {
      this.name = profile.name;
      this.placeQuery = profile.placeQuery ?? '';
      this.lat = profile.lat;
      this.lon = profile.lon;
      this.radiusKm = profile.radiusKm;
      this.selectedSpeeds = [...profile.speeds];
      this.weekendOnly = profile.weekendOnly;
      this.minPlayers = profile.minPlayers;
      this.notifyNew = profile.notifyNew;
    }

    // switchMap statt verschachtelter Subscribes: bei schnellem Tippen darf nicht die Antwort
    // einer aelteren Anfrage die neuere ueberschreiben.
    this.placeTerm$.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      switchMap(term => this.directory.places(term)),
      takeUntilDestroyed(),
    ).subscribe({
      next: results => { this.suggestions = results; this.searching = false; },
      error: () => { this.suggestions = []; this.searching = false; },
    });
  }

  onPlaceInput(value: string): void {
    this.placeQuery = value;
    if (value.trim().length < 2) {
      this.suggestions = [];
      return;
    }
    this.searching = true;
    this.placeTerm$.next(value.trim());
  }

  choose(suggestion: GeoPlaceSuggestion): void {
    this.placeQuery = suggestion.label;
    this.lat = suggestion.lat;
    this.lon = suggestion.lon;
    this.suggestions = [];
    this.error = null;
  }

  save(): void {
    if (!this.name.trim()) {
      this.error = 'tournamentDirectory.profile.errorName';
      return;
    }
    if (this.lat == null || this.lon == null) {
      this.error = 'tournamentDirectory.profile.errorPlace';
      return;
    }

    this.dialogRef.close({
      name: this.name.trim(),
      placeQuery: this.placeQuery.trim() || null,
      lat: this.lat,
      lon: this.lon,
      radiusKm: this.radiusKm,
      federations: [],
      speeds: this.selectedSpeeds,
      weekendOnly: this.weekendOnly,
      minPlayers: this.minPlayers && this.minPlayers > 0 ? this.minPlayers : null,
      notifyNew: this.notifyNew,
      sortOrder: this.data.profile?.sortOrder ?? 0,
    });
  }
}
