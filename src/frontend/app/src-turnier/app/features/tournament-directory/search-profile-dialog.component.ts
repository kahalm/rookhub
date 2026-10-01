import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, Inject, inject, signal } from '@angular/core';
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
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { GeolocationFailure, GeolocationService } from '../../core/geolocation.service';
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
  /**
   * Die Ortssuche fand nichts bzw. scheiterte. Ohne die beiden stand nach der Sanduhr nichts da,
   * und der Dialog verlangte trotzdem „einen Ort aus der Liste" (Codereview F6-007).
   */
  noMatch = false;
  searchFailed = false;
  error: string | null = null;

  /**
   * Standort wie in der Filterleiste: ohne Treffer im Ortslexikon war ein Profil sonst nicht
   * anzulegen. In Signalen, weil die Ortung ausserhalb der Angular-Zone antwortet.
   */
  private readonly geolocation = inject(GeolocationService);
  private readonly destroyRef = inject(DestroyRef);
  readonly locating = signal(false);
  readonly locationError = signal<GeolocationFailure | null>(null);

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
      // Der gespeicherte Ortstext gehoert zu den gespeicherten Koordinaten — sonst gilt er beim
      // Oeffnen sofort als „abweichend" und die Koordinaten waeren weg.
      this.chosenLabel = profile.placeQuery ?? null;
    }

    // switchMap statt verschachtelter Subscribes: bei schnellem Tippen darf nicht die Antwort
    // einer aelteren Anfrage die neuere ueberschreiben.
    this.placeTerm$.pipe(
      debounceTime(250),
      // Der Vergleich muss die ANGEZEIGTEN Vorschlaege mit einbeziehen: „ab" tippen, auf „a"
      // loeschen (Liste geleert), wieder „b" — derselbe Begriff, aber die Liste ist leer und das
      // Sanduhr-Flag steht. Ohne diese Bedingung verwirft distinctUntilChanged die Anfrage, und
      // die Sanduhr bleibt fuer immer.
      distinctUntilChanged((a, b) => a === b && this.suggestions.length > 0),
      // Fehler INNEN abfangen: draussen beendete der erste den Strom, und die Sanduhr blieb stehen.
      switchMap(term => this.directory.places(term).pipe(
        map(results => ({ results, failed: false })),
        catchError(() => of({ results: [] as GeoPlaceSuggestion[], failed: true })),
      )),
      takeUntilDestroyed(),
    ).subscribe(({ results, failed }) => {
      this.suggestions = results;
      this.searching = false;
      this.searchFailed = failed;
      this.noMatch = !failed && results.length === 0;
    });
  }

  get geolocationSupported(): boolean {
    return this.geolocation.supported;
  }

  /**
   * Den Standort des Browsers als Mittelpunkt nehmen — erst die Ortung, dann der naechste Ort aus
   * dem eigenen Lexikon als NAME. Findet sich keiner, gelten die Koordinaten trotzdem (dann stehen
   * sie selbst im Feld): der Server braucht Koordinaten, keinen Lexikon-Treffer.
   */
  useCurrentLocation(): void {
    this.locationError.set(null);
    this.locating.set(true);
    this.geolocation.current().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: fix => {
        const fallback = `${fix.lat.toFixed(3)}, ${fix.lon.toFixed(3)}`;
        const apply = (label: string) => {
          this.lat = fix.lat;
          this.lon = fix.lon;
          this.placeQuery = label;
          this.chosenLabel = label;
          this.suggestions = [];
          this.noMatch = false;
          this.searchFailed = false;
          this.error = null;
          this.locating.set(false);
        };
        this.directory.nearestPlace(fix.lat, fix.lon)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: place => apply(place?.label ?? fallback),
            error: () => apply(fallback),
          });
      },
      error: (failure: GeolocationFailure) => {
        this.locating.set(false);
        this.locationError.set(failure);
      },
    });
  }

  /** Die Beschriftung, zu der die aktuell gehaltenen Koordinaten gehoeren. */
  private chosenLabel: string | null = null;

  onPlaceInput(value: string): void {
    this.placeQuery = value;

    // Weicht der Text von der zuletzt GEWAEHLTEN Beschriftung ab, gelten die Koordinaten nicht
    // mehr. Ohne das speichert „Zuhause/Wien" nach dem Umtippen auf „Berlin" die Wiener
    // Koordinaten unter dem Namen Berlin — und dieses Profil steuert Ansicht UND naechtliche
    // Meldung, die Falschangabe waere danach nirgends zu sehen.
    if (value.trim() !== (this.chosenLabel ?? '').trim()) {
      this.lat = null;
      this.lon = null;
      this.chosenLabel = null;
    }

    const term = value.trim();
    this.noMatch = false;
    this.searchFailed = false;
    this.locationError.set(null);
    if (term.length < 2) {
      this.suggestions = [];
      this.searching = false;
      return;
    }
    this.searching = true;
    this.placeTerm$.next(term);
  }

  choose(suggestion: GeoPlaceSuggestion): void {
    this.placeQuery = suggestion.label;
    this.chosenLabel = suggestion.label;
    this.lat = suggestion.lat;
    this.lon = suggestion.lon;
    this.suggestions = [];
    this.noMatch = false;
    this.searchFailed = false;
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
