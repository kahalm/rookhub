import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { LoadErrorComponent } from '@rh/shared/load-error/load-error.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { SnackbarService } from '@rh/core/snackbar.service';
import { ViewStateService } from '@rh/core/view-state.service';
import { AuthService } from '@rh/core/auth.service';
import { requireAccount } from '../../core/require-account';
import { wideScreen } from '../../core/wide-screen';
import { GeolocationFailure, GeolocationService } from '../../core/geolocation.service';
import { DirectoryFilterPanelComponent } from './directory-filter-panel.component';
import { DirectoryCalendarLoader, DirectoryListLoader, DirectoryPinsLoader } from './directory-loaders';
import { MissingTournamentDialogComponent } from './missing-tournament-dialog.component';
import { PlaceFieldComponent } from './place-field.component';
import { PlaceSearch } from './place-search';
import { PinColourBy } from './pin-palette';
import { ViewTab, applyStoredView, snapshotView } from './directory-view-state';
import { DirectoryViewStateSync } from './directory-view-state-sync';
import { SearchProfileActions } from './search-profile-actions';
import { SearchProfileMenuComponent } from './search-profile-menu.component';
import { SearchProfileService } from './search-profile.service';
import { TournamentCalendarComponent } from './tournament-calendar.component';
import { TournamentCardComponent } from './tournament-card.component';
import {
  TournamentCardDialogComponent, TournamentCardDialogData,
} from './tournament-card-dialog.component';
import { TournamentDirectoryService } from './tournament-directory.service';
import { MapView, TournamentMapComponent } from './tournament-map.component';
import {
  DEFAULT_RADIUS_KM, DIRECTORY_RADII,
  DIRECTORY_RANGE_PRESETS, DirectoryEntry, DirectoryFilter,
  DirectoryRangePreset, EMPTY_FILTER, GeoPlaceSuggestion, SearchProfile, TournamentAgeGroup,
  TournamentGender, TournamentKind, TournamentSpeed, directoryPageSize, rangeFor, groupSameName, sameNameSiblings } from './tournament-directory.model';

/**
 * Turnierkalender: Liste, Karte und Monatsansicht auf DENSELBEN Filterzustand.
 *
 * <p>Der Filter liegt bewusst in dieser Huelle und nicht in den drei Ansichten — sonst zeigt die
 * Karte etwas anderes als die Liste darueber, obwohl dieselbe Filterleiste daruebersteht.</p>
 *
 * <p><b>Alles, was aus einer HTTP-Antwort kommt, liegt in Signalen.</b> Das ist kein Stilwunsch:
 * Angular 22 laesst `provideHttpClient()` ueber `fetch` laufen, und zone.js traegt die
 * Angular-Zone NICHT durch den Antwort-Strom. Der Abonnent laeuft also AUSSERHALB der Zone, und
 * eine schlichte Feldzuweisung dort loest keine Aenderungserkennung aus — der Zustand ist
 * richtig, die Ansicht bleibt alt, und erst eine fremde Interaktion (Reiterwechsel) holt sie
 * nach. Genau so gemeldet: „wenn ich suche, sehe ich das Ergebnis erst, wenn ich von Karte auf
 * Liste wechsle". Ein Signal benachrichtigt seine Leser selbst und braucht die Zone nicht.
 * `NgZone.run` in einem Interceptor und ein `ApplicationRef.tick()` je Antwort wurden probiert
 * und haben es nachweislich NICHT behoben (siehe TODO.md).</p>
 *
 * <p>Der FILTER dagegen bleibt ein einfaches Objekt: er wird ausschliesslich durch Eingaben des
 * Nutzers geaendert, und die laufen ohnehin in der Zone.</p>
 */
@Component({
  selector: 'app-tournament-directory',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatDialogModule, MatFormFieldModule,
    MatIconModule, MatSelectModule, MatTabsModule, TranslatePipe,
    LoadingSpinnerComponent, LoadErrorComponent, HelpHintComponent, TournamentCalendarComponent, TournamentCardComponent,
    TournamentMapComponent, PlaceFieldComponent, SearchProfileMenuComponent, DirectoryFilterPanelComponent,
  ],
  templateUrl: './tournament-directory.component.html',
  styleUrls: ['./tournament-directory.component.scss'],
})
export class TournamentDirectoryComponent implements OnInit {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly profileService = inject(SearchProfileService);
  private readonly geolocation = inject(GeolocationService);
  private readonly dialog = inject(MatDialog);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  // takeUntilDestroyed() ohne Argument verlangt einen Injection-Context. In ngOnInit gibt es
  // keinen (NG0203) — deshalb die DestroyRef als Feld holen und explizit durchreichen.
  private readonly destroyRef = inject(DestroyRef);
  private readonly viewStates = inject(ViewStateService);
  private readonly auth = inject(AuthService);

  readonly speeds: TournamentSpeed[] = ['Standard', 'Rapid', 'Blitz'];
  readonly rangePresets = DIRECTORY_RANGE_PRESETS;
  readonly radii = DIRECTORY_RADII;
  readonly pageSize = directoryPageSize();

  /** Vorgabe: die naechsten drei Monate (siehe rangeFor). */
  rangePreset: DirectoryRangePreset = 'quarter';
  /** Die Zusatzfilter stehen eingeklappt — die Leiste war sonst die halbe Seite. */
  filtersOpen = false;

  tab: ViewTab = 'list';
  readonly split = wideScreen(() => this.reload()); // ab 2400 px Liste + Karte unter EINEM Reiter (u1-t-kalender-karte)
  filter: DirectoryFilter = { ...EMPTY_FILTER };

  readonly tilesFailed = signal(false);
  readonly profiles = signal<SearchProfile[]>([]);
  /**
   * Die Profile sind wirklich da. Ohne diese Marke stand bei einem gescheiterten Abruf der
   * Erstnutzer-Hinweis „Noch kein Suchprofil" — auch bei Nutzern, die Profile haben (UX-040).
   */
  readonly profilesLoaded = signal(false);

  /** Die Liste (seitenweise, mit Zaehler gegen ueberholte Antworten — `DirectoryListLoader`). */
  private readonly list = new DirectoryListLoader(
    (filter, page, size) => this.directory.search(filter, page, size), this.pageSize,
    () => this.snackbar.warn(this.translate.instant('tournamentDirectory.loadError')));
  readonly entries = this.list.entries;
  readonly total = this.list.total;
  readonly truncated = this.list.truncated;
  readonly loading = this.list.loading;
  readonly listFailed = this.list.failed;
  readonly listError = this.list.error;
  get page(): number { return this.list.page; }
  set page(value: number) { this.list.page = value; }

  /**
   * Die Liste in zwei Bloecken: erst was im Zeitraum BEGINNT, dann was schon laeuft. Der Server
   * liefert sie in genau dieser Reihenfolge (Codereview UX-039) — vorher standen auf DEV 583
   * laufende oder unplausible Eintraege vor dem ersten kommenden Turnier. Der zweite Block
   * bekommt eine eigene Ueberschrift, damit die Saisonligen nicht wie Termine der naechsten
   * Wochen aussehen. Ohne `from` kennt der Server kein „laeuft bereits", dann bleibt es ein Block.
   */
  readonly upcomingEntries = computed(() => groupSameName(this.entries().filter(e => !e.ongoing)));
  readonly ongoingEntries = computed(() => groupSameName(this.entries().filter(e => e.ongoing)));
  /** Gleichnamige Turniere am selben Tag als getrennte Karten (r2-t-dup-cards), direkt hintereinander und
   *  durchgezählt „1 von 2" (z2-t-cal-gleichnamig). */
  readonly sameNameIds = computed(() => sameNameSiblings(this.entries()));

  /** Die Punkte des Kartenausschnitts (`DirectoryPinsLoader`). */
  private readonly pinsLoader = new DirectoryPinsLoader((filter, bounds) => this.directory.map(filter, bounds));
  readonly pins = this.pinsLoader.pins;
  readonly mapTruncated = this.pinsLoader.truncated;
  readonly mapLoading = this.pinsLoader.loading;
  readonly mapFailed = this.pinsLoader.failed;
  readonly mapError = this.pinsLoader.error;
  private lastBounds: string | null = null;
  /**
   * Der zuletzt gezeigte Kartenausschnitt (Mitte + Zoom). Die Karte steht in `@if (tab === 'map')`
   * und wird bei jedem Reiterwechsel neu gebaut, nach „Turnier oeffnen -> zurueck" sogar die ganze
   * Seite — sie begann dann jedes Mal wieder bei Oesterreich (Codereview 2026-09-29, F6-018).
   * Reist im gemerkten Anzeigezustand mit.
   */
  mapView: MapView | null = null;

  /** Die Tage des gezeigten Monats (`DirectoryCalendarLoader`). */
  private readonly calendar = new DirectoryCalendarLoader(
    (filter, year, month) => this.directory.calendar(filter, year, month));
  readonly calendarDays = this.calendar.days;
  calendarYear = new Date().getFullYear();
  calendarMonth = new Date().getMonth() + 1;
  readonly calendarLoading = this.calendar.loading;
  readonly calendarFailed = this.calendar.failed;
  readonly calendarError = this.calendar.error;

  /** Stamm des geraetelokalen Schluessels (siehe `DirectoryViewStateSync.ViewKey`). */
  static readonly ViewKey = DirectoryViewStateSync.ViewKey;
  /** Der geraetelokale Schluessel dieses Nutzers bzw. des Gasts (`DirectoryViewStateSync.viewKeyFor`). */
  static viewKeyFor(userId: number | null | undefined): string {
    return DirectoryViewStateSync.viewKeyFor(userId);
  }
  /** Dieselbe Ansicht beim NUTZER (Server) — die Kennung muss in `ViewStateService.AllowedKeys` stehen. */
  static readonly StateKey = DirectoryViewStateSync.StateKey;

  /** Der gemerkte Anzeigezustand: geraetelokal, beim Nutzer, und in welcher Reihenfolge. */
  private readonly viewSync = new DirectoryViewStateSync(this.auth, this.viewStates, this.destroyRef, {
    snapshot: () => this.viewState(),
    apply: stored => this.applyStoredView(stored),
    ready: () => this.ready(),
    reload: () => this.reload(),
  });

  /**
   * Bis die Suchprofile da sind und die Deep-Links ausgewertet sind, wird NICHT geladen: der
   * mat-tab-group meldet seinen Startindex sofort, und ohne diese Sperre liefe die erste
   * Abfrage zweimal — einmal mit dem halb aufgebauten Filter.
   *
   * <p>Auch die Karte wartet darauf (Template): gebaut, bevor die Profile da waren, stand ihr
   * Umkreis noch nicht fest. Der gemerkte Ausschnitt galt dann als „ohne Umkreis", und als das
   * Profil eintraf, passte die Karte auf den ganzen Kreis ein — gerade bei Nutzern MIT Suchprofil
   * war der Ausschnitt nach „Turnier oeffnen -> zurueck" weg (Codereview 2026-09-29, F6-018).
   * Deshalb ein Signal: es kippt in einer HTTP-Antwort, und ein schlichtes Feld zeichnet die
   * Karte unter Angular 22 nicht verlaesslich herbei.</p>
   */
  readonly ready = signal(false);

  /** Angemeldet? Ohne Konto ist der Kalender lesbar, aber Suchprofile, Ausblenden und Melden gibt es nicht. */
  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  ngOnInit(): void {
    this.applyRangePreset('quarter', false);
    this.viewSync.restore();
    this.placeSearch.start();
    this.viewSync.start();

    // Ohne Anmeldung (seit 0.643.0): kein Server-Stand und keine Suchprofile — beide gehoeren einem Konto, und die
    // Abrufe endeten nur in 401. Der Gast-Stand liegt geraetelokal (viewKeyFor).
    if (!this.auth.isLoggedIn) {
      this.applyQueryParams();
      return;
    }
    this.viewSync.syncRemote();

    this.profileService.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: profiles => {
        this.profiles.set(profiles);
        this.profilesLoaded.set(true);
        this.applyQueryParams();
      },
      error: () => this.applyQueryParams(),
    });
  }

  // ----- Ort + Umkreis ----------------------------------------------------

  /** Was im Ortsfeld steht — Beschriftung eines Gazetteer-Treffers oder Profilname. */
  placeLabel = '';
  /** Die Ortssuche (Vorschlaege, Sanduhr, „kein Ort gefunden", Fehler — Codereview F6-007). */
  private readonly placeSearch = new PlaceSearch(term => this.directory.places(term), this.destroyRef);
  readonly placeSuggestions = this.placeSearch.suggestions;
  readonly placeSearching = this.placeSearch.searching;
  readonly placeNoMatch = this.placeSearch.noMatch;
  readonly placeSearchFailed = this.placeSearch.failed;
  readonly locating = signal(false);
  readonly locationError = signal<GeolocationFailure | null>(null);

  /** Die Beschriftung, zu der die aktuell gehaltenen Koordinaten gehoeren. */
  private chosenPlaceLabel: string | null = null;

  onPlaceInput(value: string): void {
    this.placeLabel = value;
    this.locationError.set(null);

    // Weicht der Text von der zuletzt GEWAEHLTEN Beschriftung ab, gelten die Koordinaten nicht
    // mehr. Sonst sucht die Seite im Umkreis von Wien weiter, waehrend „Berlin" im Feld steht.
    if (value.trim() !== (this.chosenPlaceLabel ?? '').trim()) this.clearCentre();

    this.placeSearch.input(value);
  }

  choosePlace(place: GeoPlaceSuggestion): void {
    this.placeLabel = place.label;
    this.chosenPlaceLabel = place.label;
    this.filter.lat = place.lat;
    this.filter.lon = place.lon;
    this.filter.radiusKm ??= DEFAULT_RADIUS_KM;
    // Ein selbst gewaehlter Ort ERSETZT das Suchprofil: liefe beides mit, gewaenne serverseitig
    // das Profil und das Feld behauptete etwas anderes als die Liste zeigt.
    this.filter.profileId = null;
    this.placeSearch.clear();
    this.reload();
  }

  clearPlace(): void {
    this.placeLabel = '';
    this.placeSearch.clear();
    this.locationError.set(null);
    this.clearCentre();
    this.filter.profileId = null;
    this.reload();
  }

  private clearCentre(): void {
    this.filter.lat = null;
    this.filter.lon = null;
    this.chosenPlaceLabel = null;
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
        this.filter.lat = fix.lat;
        this.filter.lon = fix.lon;
        this.filter.radiusKm ??= DEFAULT_RADIUS_KM;
        this.filter.profileId = null;

        const fallback = `${fix.lat.toFixed(3)}, ${fix.lon.toFixed(3)}`;
        this.placeLabel = fallback;
        this.chosenPlaceLabel = fallback;
        // Ein stehengebliebenes „Kein Ort gefunden" widerspraeche dem Ort, der jetzt im Feld steht.
        this.placeSearch.clear();
        this.reload();

        // Der Name ist Beiwerk: die Suche laeuft schon mit den Koordinaten, und ein Fehlschlag
        // hier darf sie nicht anhalten.
        this.directory.nearestPlace(fix.lat, fix.lon)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: place => {
              if (!place) return;
              this.placeLabel = place.label;
              this.chosenPlaceLabel = place.label;
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

  onRadiusChange(radiusKm: number | null): void {
    // Ein selbst gewaehlter Radius ist eine Abweichung vom Profil: bei gesetztem `profileId`
    // nimmt der Server DESSEN Radius, das Feld zeigte dann 50 km und die Liste 100. Also den
    // Mittelpunkt des Profils uebernehmen und ab hier selbst fuehren.
    if (this.filter.profileId !== null) this.adoptProfileCentre();
    // 0 = „Überall" aus der Auswahl (r2-t-radius-empty); im Filter bleibt das null.
    this.filter.radiusKm = radiusKm || null;
    this.reload();
  }

  /**
   * Das Profil aufloesen und als selbst gesetzten Mittelpunkt weiterfuehren. Gebraucht, sobald
   * der Nutzer Ort oder Radius anfasst: ab da beschreibt die Leiste, was gilt, und nicht mehr
   * das Profil.
   */
  private adoptProfileCentre(): void {
    const profile = this.activeProfile;
    if (profile) {
      this.filter.lat = profile.lat;
      this.filter.lon = profile.lon;
      this.chosenPlaceLabel = this.placeLabel || null;
    }
    this.filter.profileId = null;
  }

  // ----- Filter -----------------------------------------------------------

  get activeProfile(): SearchProfile | null {
    return this.profiles().find(p => p.id === this.filter.profileId) ?? null;
  }

  /**
   * Der Mittelpunkt fuer die Karte — als STABILES Objekt. Ein Getter, der jedes Mal ein neues
   * Objektliteral zurueckgibt, laesst unter Default-Change-Detection in JEDEM Zyklus ein
   * ngOnChanges der Karte feuern; die hat daraufhin ihre Ansicht neu eingepasst, und Zoomen war
   * nicht moeglich.
   */
  private centreCache: { lat: number; lon: number; radiusKm: number } | null = null;
  private centreKey = '';

  get mapCentre(): { lat: number; lon: number; radiusKm: number } | null {
    const profile = this.activeProfile;
    const lat = profile?.lat ?? this.filter.lat;
    const lon = profile?.lon ?? this.filter.lon;
    const radiusKm = profile?.radiusKm ?? this.filter.radiusKm;
    const key = lat != null && lon != null && radiusKm ? `${lat}|${lon}|${radiusKm}` : '';

    if (key !== this.centreKey) {
      this.centreKey = key;
      this.centreCache = key ? { lat: lat!, lon: lon!, radiusKm: radiusKm! } : null;
    }
    return this.centreCache;
  }

  /** Setter statt Getter-Filter: unter Default-Change-Detection liefe ein Getter jeden Zyklus. */
  set searchText(value: string) {
    this.filter.text = value.trim() || null;
  }
  get searchText(): string {
    return this.filter.text ?? '';
  }

  onProfileChange(profileId: number | null): void {
    this.filter.profileId = profileId;
    this.clearCentre();

    // Das Ortsfeld zeigt IMMER den geltenden Mittelpunkt — auch den aus einem Profil. Ohne das
    // stuende dort der zuletzt getippte Ort, waehrend die Liste den Umkreis des Profils zeigt.
    const profile = this.activeProfile;
    this.placeLabel = profile?.placeQuery ?? profile?.name ?? '';
    this.chosenPlaceLabel = this.placeLabel || null;
    this.filter.radiusKm = profile?.radiusKm ?? null;
    this.placeSearch.clear();

    this.reload();
  }

  /** Felder, die das Filterfeld direkt setzt (Datum, Ligen, Wochenende, Mindestzahl) — wie frueher `[(ngModel)]`. */
  patchFilter(patch: Partial<DirectoryFilter>): void {
    Object.assign(this.filter, patch);
  }

  onTilesFailed(): void {
    this.tilesFailed.set(true);
  }

  onSpeedChange(speed: TournamentSpeed | null): void {
    this.filter.speed = speed ?? null;
    this.reload();
  }

  onKindsChange(kinds: TournamentKind[] | null): void {
    this.filter.kinds = kinds ?? [];
    this.reload();
  }

  onAgeGroupsChange(groups: TournamentAgeGroup[] | null): void {
    this.filter.ageGroups = groups ?? [];
    // Eine gewaehlte Jugendklasse und „nur Erwachsene" schliessen sich aus — beides gesetzt
    // ergibt zwingend eine leere Liste, und niemand meint das.
    if (this.filter.ageGroups.length > 0) this.filter.adultsOnly = false;
    this.reload();
  }

  onGendersChange(genders: TournamentGender[] | null): void {
    this.filter.genders = genders ?? [];
    this.reload();
  }

  onAdultsOnlyChange(adultsOnly: boolean): void {
    this.filter.adultsOnly = adultsOnly;
    if (adultsOnly) this.filter.ageGroups = [];
    this.reload();
  }

  applyRangePreset(preset: DirectoryRangePreset, reload = true): void {
    this.rangePreset = preset;
    if (preset !== 'custom') {
      const range = rangeFor(preset);
      this.filter.from = range.from;
      this.filter.to = range.to;
    }
    if (reload) this.reload();
  }

  /** Wie viele Zusatzfilter aktiv sind — steht als Zahl am eingeklappten „Filter"-Knopf. */
  get activeExtraFilters(): number {
    return (this.filter.speed ? 1 : 0)
      + (this.filter.weekendOnly ? 1 : 0)
      + (this.filter.minPlayers ? 1 : 0)
      + (this.filter.text ? 1 : 0)
      + (this.filter.kinds.length > 0 ? 1 : 0)
      + (this.filter.ageGroups.length > 0 ? 1 : 0)
      + (this.filter.genders.length > 0 ? 1 : 0)
      + (this.filter.adultsOnly ? 1 : 0)
      + (this.filter.hideLeagues ? 1 : 0)
      + (this.filter.includeIgnored ? 1 : 0)
      + (this.filter.includeImplausible ? 1 : 0);
  }

  resetFilter(): void {
    this.filter = { ...EMPTY_FILTER };
    this.placeLabel = '';
    this.chosenPlaceLabel = null;
    this.placeSearch.clear();
    this.locationError.set(null);
    this.applyRangePreset('quarter');
  }

  onTabChange(index: number): void {
    this.tab = ((this.split() ? ['list', 'calendar'] : ['list', 'map', 'calendar']) as ViewTab[])[index] ?? 'list';
    this.reload();
  }

  reload(): void {
    if (!this.ready()) return;
    this.page = 1;
    this.storeView();
    if (this.tab === 'list' || this.split() && this.tab === 'map') this.loadList();
    if ((this.tab === 'map' || this.split() && this.tab === 'list') && this.lastBounds) this.loadPins(this.lastBounds);
    if (this.tab === 'calendar') this.loadCalendar();
  }

  // ----- Liste ------------------------------------------------------------

  loadList(): void {
    this.list.load(this.filter);
  }

  loadMore(): void {
    this.page++;
    this.loadList();
  }

  // ----- Karte ------------------------------------------------------------

  onBoundsChanged(bounds: string): void {
    this.lastBounds = bounds;
    this.loadPins(bounds);
  }

  onMapViewChanged(view: MapView): void {
    this.mapView = view;
    // Kein reload(): `boundsChanged` laedt die Punkte schon. Nur merken.
    this.storeView();
  }

  loadPins(bounds: string): void {
    this.pinsLoader.load(this.filter, bounds);
  }

  // ----- Kalender ---------------------------------------------------------

  onMonthChanged(event: { year: number; month: number }): void {
    this.calendarYear = event.year;
    this.calendarMonth = event.month;
    this.storeView();
    this.loadCalendar();
  }

  private loadCalendar(): void {
    this.calendar.load(this.filter, this.calendarYear, this.calendarMonth);
  }

  get calendarLocale(): string {
    // ngx-translate 18: currentLang() ist Signal<string|null> → leer auf die Fallback-Sprache.
    return this.translate.currentLang() || 'en';
  }

  // ----- Detail + Aktionen -------------------------------------------------

  /**
   * Liste, Karte und Kalender fuehren alle hierher: das Turnier bekommt eine eigene Seite mit
   * eigener Adresse. Die aufklappende Karte darunter war weder teilbar noch als Lesezeichen zu
   * sichern, und der Zurueck-Knopf des Browsers fuehrte aus dem Kalender heraus statt aus dem
   * Detail. Die Filterleiste ueberlebt den Weg (siehe storeView).
   */
  select(entry: DirectoryEntry): void {
    this.storeView();
    this.router.navigate(['/tournaments/calendar', entry.id]);
  }

  /**
   * Ein Turnier wurde aus- oder wieder eingeblendet. Zeigt der Filter Ausgeblendete NICHT mit,
   * faellt die Zeile sofort heraus — sonst stuende sie bis zum naechsten Laden da und
   * verschwaende dann ohne erkennbaren Anlass. Die Gesamtzahl wandert mit, sonst behauptet die
   * Zeile darueber „12 von 40", waehrend elf zu sehen sind.
   */
  onIgnoredChanged(event: { entry: DirectoryEntry; ignored: boolean }): void {
    if (!event.ignored || this.filter.includeIgnored) return;

    this.entries.update(list => list.filter(e => e.id !== event.entry.id));
    this.total.update(total => Math.max(0, total - 1));
  }

  /**
   * Im KALENDER fuehrt ein Klick nicht direkt auf die Detailseite, sondern oeffnet die
   * Kurzansicht als Fenster: im Monatsraster ist ein Tag ein paar Zeilen hoch, die Kurzansicht
   * passt dort nicht hinein, und wer den Kalender verlaesst, verliert beim Vergleichen den
   * Monat. Erst der Klick auf den NAMEN im Fenster fuehrt weiter.
   */
  openFromCalendar(entry: DirectoryEntry): void {
    const data: TournamentCardDialogData = { entry };
    const ref = this.dialog.open(TournamentCardDialogComponent, { data, width: '340px' });

    ref.afterClosed().subscribe(chosen => {
      // Gemerkt oder aufgehoben: in den Kalendereintrag zurueckschreiben (wie die Karte in
      // `applySubscribed`), sonst oeffnete das Fenster beim naechsten Mal mit dem alten Zustand.
      // Ein Neuladen braucht es nicht: der Kalender zeigt „gemerkt" nicht an, und ein
      // mehrtaegiges Turnier ist an allen Tagen DASSELBE Objekt (siehe DirectoryCalendarResponse).
      const subscribed = ref.componentInstance?.subscribed ?? null;
      if (subscribed !== null) entry.subscribed = subscribed;

      if (chosen) {
        this.select(chosen);
        return;
      }
      // Nichts gewaehlt, aber vielleicht aus-/eingeblendet: der Kalender kann keine einzelne
      // Zeile herausnehmen, ein Turnier steht an mehreren Tagen. Also neu laden.
      if (ref.componentInstance?.ignored ?? false) this.reload();
    });
  }


  /**
   * Aus der KARTE heraus aus-/eingeblendet. Anders als in der Liste laesst sich hier kein
   * einzelner Punkt entfernen, ohne den Ausschnitt neu zu holen — und ein Turnier kann mehrere
   * Punkte haben.
   */
  onIgnoredFromMap(): void {
    if (this.lastBounds) this.loadPins(this.lastBounds);
  }

  onIncludeIgnoredChange(includeIgnored: boolean): void {
    this.filter.includeIgnored = includeIgnored;
    this.reload();
  }

  /** Gilt fuer Liste, Karte und Kalender gleich — der Server blendet sie ueberall aus. */
  onIncludeImplausibleChange(includeImplausible: boolean): void {
    this.filter.includeImplausible = includeImplausible;
    this.reload();
  }

  trackById = (_: number, entry: DirectoryEntry) => entry.id;

  /**
   * „Mein Turnier fehlt". Steht unter allen drei Ansichten, weil die Luecke in jeder gleich
   * unsichtbar ist: das Verzeichnis speist sich aus chess-results, und wer dort nicht
   * ausschreibt, kommt hier nicht vor — von innen ist das nicht zu sehen.
   */
  reportMissing(): void {
    // Die Meldung geht als Nachricht an das Admin-Team und braucht einen Absender.
    if (!requireAccount(this.auth, this.router)) return;
    this.dialog.open(MissingTournamentDialogComponent, { width: '520px' });
  }

  // ----- Suchprofile -------------------------------------------------------

  newProfile(): void {
    if (!requireAccount(this.auth, this.router)) return;
    this.profileActions.open(null);
  }

  editProfile(profile: SearchProfile): void {
    this.profileActions.open(profile);
  }

  /** Loeschen mit 6 s Rueckgaengig (F6-014, `SearchProfileActions.remove`). */
  deleteProfile(profile: SearchProfile): void {
    this.profileActions.remove(profile);
  }

  /** Anlegen, Bearbeiten, Loeschen samt Rueckgaengig (`SearchProfileActions`). */
  private readonly profileActions = new SearchProfileActions({
    dialog: this.dialog, snackbar: this.snackbar, translate: this.translate,
    service: this.profileService, profiles: this.profiles,
    activeId: () => this.filter.profileId,
    select: id => this.onProfileChange(id),
  });

  // ----- Deep-Links --------------------------------------------------------

  /**
   * Die Benachrichtigungen verlinken hierher: `?profile=` aus der Umkreis-Meldung, `?t=` aus einer
   * Termin-, Orts- oder Absagemeldung. Ohne das landet man auf einer ungefilterten Liste und darf
   * das gemeinte Turnier selbst suchen.
   */
  private applyQueryParams(): void {
    const params = this.route.snapshot.queryParamMap;
    const profiles = this.profiles();

    const profileId = Number(params.get('profile'));
    const storedIsValid = this.storedProfileId != null
      && profiles.some(p => p.id === this.storedProfileId);
    if (profileId && profiles.some(p => p.id === profileId)) {
      this.filter.profileId = profileId;
    } else if (storedIsValid) {
      this.filter.profileId = this.storedProfileId;
    } else if (this.hasStoredProfile && this.storedProfileId === null) {
      this.filter.profileId = null;        // „kein Umkreis" war eine WAHL, keine fehlende Angabe
    } else if (profiles.length > 0) {
      this.filter.profileId = profiles[0].id;
    }

    // Das Ortsfeld zeigt den geltenden Mittelpunkt. Kommt er aus einem Profil, steht dessen Ort
    // darin; ein gemerkter selbst gewaehlter Ort behaelt seine Beschriftung.
    const profile = this.activeProfile;
    if (profile) {
      this.placeLabel = profile.placeQuery ?? profile.name;
      this.chosenPlaceLabel = this.placeLabel;
      this.filter.radiusKm = profile.radiusKm;
      // Die Koordinaten kommen serverseitig aus dem Profil; ein zusaetzlich mitgeschicktes
      // lat/lon waere eine zweite Wahrheit fuer denselben Mittelpunkt.
      this.filter.lat = null;
      this.filter.lon = null;
    }

    this.ready.set(true);
    // Scheiterte der Abruf schon vorher, ist erst JETZT der Stand da, an dem eine Aenderung des
    // Nutzers zu messen ist (siehe `DirectoryViewSync.stateAtSyncFailure`).
    this.viewSync.captureBaselineIfFailed();

    const tournamentId = params.get('t');
    if (tournamentId) {
      // Ein gemeldetes Turnier kann ausserhalb des aktuellen Umkreises oder abgesagt sein — es in
      // der gefilterten Liste zu suchen ginge also fehl. Die Detailseite holt es einzeln.
      this.router.navigate(['/tournaments/calendar', tournamentId]);
      return;
    }

    this.reload();
  }

  // ----- Gemerkte Ansicht ---------------------------------------------------

  /** Was die Filterleiste zeigt — genug, um nach einem Seitenwechsel dasselbe Bild aufzubauen. */
  private viewState(): Record<string, unknown> {
    return snapshotView(this);
  }

  /** Zustand sichern — geraetelokal sofort, beim Nutzer gedrosselt (`DirectoryViewStateSync.store`). */
  private storeView(): void {
    this.viewSync.store();
  }

  /**
   * Wonach die Karten-Pins eingefaerbt werden. Liegt HIER und nicht in der Karte, damit die Wahl
   * im gespeicherten Anzeigezustand mitreist — sonst waere sie nach jedem Turnierbesuch und auf
   * jedem zweiten Geraet wieder die Vorgabe.
   */
  mapColourBy: PinColourBy = 'speed';

  onColourByChange(colourBy: PinColourBy): void {
    this.mapColourBy = colourBy;
    // Kein reload(): das Merkmal aendert nur die FARBE der Punkte, nicht die Auswahl.
    this.storeView();
  }

  private applyStoredView(stored: Record<string, unknown> | null): void {
    const applied = applyStoredView(this, stored);
    if (!applied) return;
    if (applied.placeRestored) this.chosenPlaceLabel = this.placeLabel || null;
    this.storedProfileId = applied.storedProfileId;
    this.hasStoredProfile = applied.hasStoredProfile;
  }

  private storedProfileId: number | null = null;
  private hasStoredProfile = false;
}
