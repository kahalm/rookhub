import { DestroyRef, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { GeolocationFailure, GeolocationService } from '../../core/geolocation.service';
import { PlaceSearch } from './place-search';
import { TournamentDirectoryService } from './tournament-directory.service';
import { DEFAULT_RADIUS_KM, DirectoryFilter, GeoPlaceSuggestion, SearchProfile } from './tournament-directory.model';

/** Was der Ort-/Umkreis-Teil vom Turnierkalender braucht (live gelesen — der Filter wird beim Zuruecksetzen ersetzt). */
export interface DirectoryPlaceHost {
  filter(): DirectoryFilter;
  activeProfile(): SearchProfile | null;
  reload(): void;
}

/**
 * Ort + Umkreis des Turnierkalenders: das Ortsfeld (Beschriftung, Vorschlaege, Ortung des Browsers) und wie
 * es den Mittelpunkt im Filter setzt — gegen ein Suchprofil, das sonst serverseitig gewaenne.
 */
export class DirectoryPlaceController {
  /** Was im Ortsfeld steht — Beschriftung eines Gazetteer-Treffers oder Profilname. */
  label = '';
  /** Die Ortssuche (Vorschlaege, Sanduhr, „kein Ort gefunden", Fehler — Codereview F6-007). */
  private readonly search: PlaceSearch;
  readonly suggestions: PlaceSearch['suggestions'];
  readonly searching: PlaceSearch['searching'];
  readonly noMatch: PlaceSearch['noMatch'];
  readonly searchFailed: PlaceSearch['failed'];
  readonly locating = signal(false);
  readonly locationError = signal<GeolocationFailure | null>(null);

  /** Die Beschriftung, zu der die aktuell gehaltenen Koordinaten gehoeren. */
  private chosenLabel: string | null = null;

  constructor(
    private readonly directory: TournamentDirectoryService,
    private readonly geolocation: GeolocationService,
    private readonly destroyRef: DestroyRef,
    private readonly host: DirectoryPlaceHost,
  ) {
    this.search = new PlaceSearch(term => this.directory.places(term), this.destroyRef);
    this.suggestions = this.search.suggestions;
    this.searching = this.search.searching;
    this.noMatch = this.search.noMatch;
    this.searchFailed = this.search.failed;
  }

  start(): void { this.search.start(); }

  input(value: string): void {
    this.label = value;
    this.locationError.set(null);

    // Weicht der Text von der zuletzt GEWAEHLTEN Beschriftung ab, gelten die Koordinaten nicht
    // mehr. Sonst sucht die Seite im Umkreis von Wien weiter, waehrend „Berlin" im Feld steht.
    if (value.trim() !== (this.chosenLabel ?? '').trim()) this.clearCentre();

    this.search.input(value);
  }

  choose(place: GeoPlaceSuggestion): void {
    const filter = this.host.filter();
    this.label = place.label;
    this.chosenLabel = place.label;
    filter.lat = place.lat;
    filter.lon = place.lon;
    filter.radiusKm ??= DEFAULT_RADIUS_KM;
    // Ein selbst gewaehlter Ort ERSETZT das Suchprofil: liefe beides mit, gewaenne serverseitig
    // das Profil und das Feld behauptete etwas anderes als die Liste zeigt.
    filter.profileId = null;
    this.search.clear();
    this.host.reload();
  }

  clear(): void {
    this.label = '';
    this.search.clear();
    this.locationError.set(null);
    this.clearCentre();
    this.host.filter().profileId = null;
    this.host.reload();
  }

  clearCentre(): void {
    const filter = this.host.filter();
    filter.lat = null;
    filter.lon = null;
    this.chosenLabel = null;
  }

  /**
   * Den Standort des Browsers uebernehmen. Zwei Schritte, weil der Browser Koordinaten liefert,
   * im Feld aber ein NAME stehen soll: erst die Ortung, dann der naechstgelegene Ort aus dem
   * eigenen Lexikon. Findet sich keiner, gelten die Koordinaten trotzdem — dann stehen sie
   * selbst im Feld, statt dass die Umkreissuche stillschweigend ausfaellt.
   */
  useCurrentLocation(): void {
    this.locationError.set(null);
    this.locating.set(true);

    this.geolocation.current().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: fix => {
        const filter = this.host.filter();
        filter.lat = fix.lat;
        filter.lon = fix.lon;
        filter.radiusKm ??= DEFAULT_RADIUS_KM;
        filter.profileId = null;

        const fallback = `${fix.lat.toFixed(3)}, ${fix.lon.toFixed(3)}`;
        this.label = fallback;
        this.chosenLabel = fallback;
        // Ein stehengebliebenes „Kein Ort gefunden" widerspraeche dem Ort, der jetzt im Feld steht.
        this.search.clear();
        this.host.reload();

        // Der Name ist Beiwerk: die Suche laeuft schon mit den Koordinaten, und ein Fehlschlag
        // hier darf sie nicht anhalten.
        this.directory.nearestPlace(fix.lat, fix.lon)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: place => {
              if (!place) return;
              this.label = place.label;
              this.chosenLabel = place.label;
            },
            error: () => { /* Koordinaten bleiben stehen; nur der Name fehlt. */ },
          });

        this.locating.set(false);
      },
      error: (failure: GeolocationFailure) => {
        this.locating.set(false);
        this.locationError.set(failure);
      },
    });
  }

  get geolocationSupported(): boolean {
    return this.geolocation.supported;
  }

  radiusChange(radiusKm: number | null): void {
    const filter = this.host.filter();
    // Ein selbst gewaehlter Radius ist eine Abweichung vom Profil: bei gesetztem `profileId`
    // nimmt der Server DESSEN Radius, das Feld zeigte dann 50 km und die Liste 100. Also den
    // Mittelpunkt des Profils uebernehmen und ab hier selbst fuehren.
    if (filter.profileId !== null) this.adoptProfileCentre();
    // 0 = „Überall" aus der Auswahl (r2-t-radius-empty); im Filter bleibt das null.
    filter.radiusKm = radiusKm || null;
    this.host.reload();
  }

  /**
   * Das Profil aufloesen und als selbst gesetzten Mittelpunkt weiterfuehren. Gebraucht, sobald
   * der Nutzer Ort oder Radius anfasst: ab da beschreibt die Leiste, was gilt, und nicht mehr
   * das Profil.
   */
  private adoptProfileCentre(): void {
    const filter = this.host.filter();
    const profile = this.host.activeProfile();
    if (profile) {
      filter.lat = profile.lat;
      filter.lon = profile.lon;
      this.chosenLabel = this.label || null;
    }
    filter.profileId = null;
  }

  /**
   * Ein (anderes) Suchprofil gilt: das Ortsfeld zeigt IMMER den geltenden Mittelpunkt — auch den aus einem
   * Profil. Ohne das stuende dort der zuletzt getippte Ort, waehrend die Liste den Umkreis des Profils zeigt.
   */
  followProfile(): void {
    this.clearCentre();
    const profile = this.host.activeProfile();
    this.label = profile?.placeQuery ?? profile?.name ?? '';
    this.chosenLabel = this.label || null;
    this.host.filter().radiusKm = profile?.radiusKm ?? null;
    this.search.clear();
  }

  /** Ortsfeld zeigt den Ort eines Profils (Deep-Link / Start). */
  showProfile(profile: SearchProfile): void {
    this.label = profile.placeQuery ?? profile.name;
    this.chosenLabel = this.label;
  }

  /** Filter zurueckgesetzt: Feld leer, Vorschlaege und Ortungsfehler weg. */
  reset(): void {
    this.label = '';
    this.chosenLabel = null;
    this.search.clear();
    this.locationError.set(null);
  }

  /** Ein gemerkter, selbst gesetzter Mittelpunkt kam aus dem Speicher — seine Beschriftung gilt als gewaehlt. */
  markRestored(): void {
    this.chosenLabel = this.label || null;
  }
}
