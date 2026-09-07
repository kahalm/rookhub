import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { SnackbarService } from '@rh/core/snackbar.service';
import { GeolocationFailure, GeolocationService } from '../../core/geolocation.service';
import { MissingTournamentDialogComponent } from './missing-tournament-dialog.component';
import { SearchProfileDialogComponent, SearchProfileDialogData } from './search-profile-dialog.component';
import { SearchProfileService } from './search-profile.service';
import { TournamentCalendarComponent } from './tournament-calendar.component';
import { TournamentCardComponent } from './tournament-card.component';
import {
  TournamentCardDialogComponent, TournamentCardDialogData,
} from './tournament-card-dialog.component';
import { TournamentDirectoryService } from './tournament-directory.service';
import { TournamentMapComponent } from './tournament-map.component';
import {
  DEFAULT_RADIUS_KM, DIRECTORY_AGE_GROUPS, DIRECTORY_GENDERS, DIRECTORY_KINDS, DIRECTORY_RADII,
  DIRECTORY_RANGE_PRESETS, DirectoryCalendarDay, DirectoryEntry, DirectoryFilter,
  DirectoryRangePreset, EMPTY_FILTER, GeoPlaceSuggestion, SearchProfile, TournamentAgeGroup,
  TournamentGender, TournamentKind, TournamentSpeed, rangeFor,
} from './tournament-directory.model';

type ViewTab = 'list' | 'map' | 'calendar';

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
    CommonModule, FormsModule, MatAutocompleteModule, MatButtonModule, MatCardModule,
    MatChipsModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatMenuModule, MatProgressSpinnerModule, MatSelectModule, MatSlideToggleModule, MatTabsModule,
    MatTooltipModule, TranslatePipe,
    LoadingSpinnerComponent, HelpHintComponent, TournamentCalendarComponent, TournamentCardComponent,
    TournamentMapComponent,
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

  readonly speeds: TournamentSpeed[] = ['Standard', 'Rapid', 'Blitz'];
  readonly rangePresets = DIRECTORY_RANGE_PRESETS;
  readonly radii = DIRECTORY_RADII;
  readonly ageGroups = DIRECTORY_AGE_GROUPS;
  readonly genders = DIRECTORY_GENDERS;
  readonly kinds = DIRECTORY_KINDS;
  readonly pageSize = 50;

  /** Vorgabe: das kommende Quartal (siehe rangeFor). */
  rangePreset: DirectoryRangePreset = 'quarter';
  /** Die Zusatzfilter stehen eingeklappt — die Leiste war sonst die halbe Seite. */
  filtersOpen = false;

  tab: ViewTab = 'list';
  filter: DirectoryFilter = { ...EMPTY_FILTER };

  readonly tilesFailed = signal(false);
  readonly profiles = signal<SearchProfile[]>([]);

  readonly entries = signal<DirectoryEntry[]>([]);
  readonly total = signal(0);
  readonly truncated = signal(false);
  readonly loading = signal(false);
  page = 1;

  readonly pins = signal<DirectoryEntry[]>([]);
  readonly mapLoading = signal(false);
  private lastBounds: string | null = null;

  readonly calendarDays = signal<DirectoryCalendarDay[]>([]);
  calendarYear = new Date().getFullYear();
  calendarMonth = new Date().getMonth() + 1;
  readonly calendarLoading = signal(false);

  /**
   * Schluessel der gemerkten Ansicht. Ohne sie faellt der Weg „Turnier oeffnen → zurueck" auf die
   * Vorgabefilter zurueck — man muesste Zeitraum, Reiter, Ort und Monat jedes Mal neu einstellen.
   */
  static readonly ViewKey = 'rh.turnier.directoryView';

  /**
   * Bis die Suchprofile da sind und die Deep-Links ausgewertet sind, wird NICHT geladen: der
   * mat-tab-group meldet seinen Startindex sofort, und ohne diese Sperre liefe die erste
   * Abfrage zweimal — einmal mit dem halb aufgebauten Filter.
   */
  private ready = false;

  ngOnInit(): void {
    this.applyRangePreset('quarter', false);
    this.restoreView();
    this.watchPlaceInput();

    this.profileService.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: profiles => {
        this.profiles.set(profiles);
        this.applyQueryParams();
      },
      error: () => this.applyQueryParams(),
    });
  }

  // ----- Ort + Umkreis ----------------------------------------------------

  /** Was im Ortsfeld steht — Beschriftung eines Gazetteer-Treffers oder Profilname. */
  placeLabel = '';
  readonly placeSuggestions = signal<GeoPlaceSuggestion[]>([]);
  readonly placeSearching = signal(false);
  readonly locating = signal(false);
  readonly locationError = signal<GeolocationFailure | null>(null);

  private readonly placeTerm$ = new Subject<string>();
  /** Die Beschriftung, zu der die aktuell gehaltenen Koordinaten gehoeren. */
  private chosenPlaceLabel: string | null = null;

  private watchPlaceInput(): void {
    // switchMap: bei schnellem Tippen darf nicht die Antwort einer aelteren Anfrage die neuere
    // ueberschreiben.
    this.placeTerm$.pipe(
      debounceTime(250),
      // Der Vergleich muss die ANGEZEIGTEN Vorschlaege einbeziehen: „ab" tippen, auf „a" loeschen
      // (Liste geleert), wieder „b" — derselbe Begriff, aber die Liste ist leer und die Sanduhr
      // steht. Ohne diese Bedingung verwirft distinctUntilChanged die Anfrage und die Sanduhr
      // bleibt fuer immer.
      distinctUntilChanged((a, b) => a === b && this.placeSuggestions().length > 0),
      switchMap(term => this.directory.places(term)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: results => { this.placeSuggestions.set(results); this.placeSearching.set(false); },
      error: () => { this.placeSuggestions.set([]); this.placeSearching.set(false); },
    });
  }

  onPlaceInput(value: string): void {
    this.placeLabel = value;
    this.locationError.set(null);

    // Weicht der Text von der zuletzt GEWAEHLTEN Beschriftung ab, gelten die Koordinaten nicht
    // mehr. Sonst sucht die Seite im Umkreis von Wien weiter, waehrend „Berlin" im Feld steht.
    if (value.trim() !== (this.chosenPlaceLabel ?? '').trim()) this.clearCentre();

    const term = value.trim();
    if (term.length < 2) {
      this.placeSuggestions.set([]);
      this.placeSearching.set(false);
      return;
    }
    this.placeSearching.set(true);
    this.placeTerm$.next(term);
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
    this.placeSuggestions.set([]);
    this.reload();
  }

  clearPlace(): void {
    this.placeLabel = '';
    this.placeSuggestions.set([]);
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
    this.filter.radiusKm = radiusKm;
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

    this.reload();
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
      + (this.filter.includeIgnored ? 1 : 0);
  }

  resetFilter(): void {
    this.filter = { ...EMPTY_FILTER };
    this.placeLabel = '';
    this.chosenPlaceLabel = null;
    this.placeSuggestions.set([]);
    this.locationError.set(null);
    this.applyRangePreset('quarter');
  }

  onTabChange(index: number): void {
    this.tab = (['list', 'map', 'calendar'] as ViewTab[])[index] ?? 'list';
    this.reload();
  }

  reload(): void {
    if (!this.ready) return;
    this.page = 1;
    this.storeView();
    if (this.tab === 'list') this.loadList();
    if (this.tab === 'map' && this.lastBounds) this.loadPins(this.lastBounds);
    if (this.tab === 'calendar') this.loadCalendar();
  }

  // ----- Liste ------------------------------------------------------------

  /**
   * Zaehler gegen ueberholte Antworten. Ohne ihn liest der Antwort-Handler `this.page` zum
   * ANTWORTZEITPUNKT: „Mehr laden" (Seite 2) anstossen, sofort den Filter wechseln — die
   * Antwort auf Seite 2 des ALTEN Filters trifft ein, sieht `page === 1` und ERSETZT die Liste
   * damit. Der falsche Stand bleibt dann stehen, samt Gesamtzahl.
   */
  private listGeneration = 0;
  private pinsGeneration = 0;

  loadList(): void {
    this.loading.set(true);
    const generation = ++this.listGeneration;
    const requestedPage = this.page;
    this.directory.search(this.filter, requestedPage, this.pageSize).subscribe({
      next: page => {
        if (generation !== this.listGeneration) return;
        this.entries.set(requestedPage === 1 ? page.items : [...this.entries(), ...page.items]);
        this.total.set(page.total);
        this.truncated.set(page.truncated);
        this.loading.set(false);
      },
      error: () => {
        if (generation !== this.listGeneration) return;
        this.loading.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.loadError'));
      },
    });
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

  loadPins(bounds: string): void {
    this.mapLoading.set(true);
    // Beim erneuten Betreten des Karten-Reiters laufen zwei Abfragen gegeneinander: `reload()`
    // fragt mit dem GEMERKTEN Ausschnitt, die frisch aufgebaute Karte meldet direkt danach ihren
    // eigenen. Ohne Zaehler gewinnt die zufaellig spaetere Antwort.
    const generation = ++this.pinsGeneration;
    this.directory.map(this.filter, bounds).subscribe({
      next: pins => {
        if (generation !== this.pinsGeneration) return;
        this.pins.set(pins);
        this.mapLoading.set(false);
      },
      error: () => { if (generation === this.pinsGeneration) this.mapLoading.set(false); },
    });
  }

  // ----- Kalender ---------------------------------------------------------

  onMonthChanged(event: { year: number; month: number }): void {
    this.calendarYear = event.year;
    this.calendarMonth = event.month;
    this.storeView();
    this.loadCalendar();
  }

  private loadCalendar(): void {
    this.calendarLoading.set(true);
    this.directory.calendar(this.filter, this.calendarYear, this.calendarMonth).subscribe({
      next: days => { this.calendarDays.set(days); this.calendarLoading.set(false); },
      error: () => {
        this.calendarLoading.set(false);
        this.snackbar.warn(this.translate.instant('tournamentDirectory.loadError'));
      },
    });
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
    this.router.navigate(['/tournaments/calendar', entry.chessResultsId]);
  }

  /**
   * Ein Turnier wurde aus- oder wieder eingeblendet. Zeigt der Filter Ausgeblendete NICHT mit,
   * faellt die Zeile sofort heraus — sonst stuende sie bis zum naechsten Laden da und
   * verschwaende dann ohne erkennbaren Anlass. Die Gesamtzahl wandert mit, sonst behauptet die
   * Zeile darueber „12 von 40", waehrend elf zu sehen sind.
   */
  onIgnoredChanged(event: { entry: DirectoryEntry; ignored: boolean }): void {
    if (!event.ignored || this.filter.includeIgnored) return;

    this.entries.update(list => list.filter(e => e.chessResultsId !== event.entry.chessResultsId));
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

  trackById = (_: number, entry: DirectoryEntry) => entry.chessResultsId;

  /**
   * „Mein Turnier fehlt". Steht unter allen drei Ansichten, weil die Luecke in jeder gleich
   * unsichtbar ist: das Verzeichnis speist sich aus chess-results, und wer dort nicht
   * ausschreibt, kommt hier nicht vor — von innen ist das nicht zu sehen.
   */
  reportMissing(): void {
    this.dialog.open(MissingTournamentDialogComponent, { width: '520px' });
  }

  // ----- Suchprofile -------------------------------------------------------

  newProfile(): void {
    this.openProfileDialog(null);
  }

  editProfile(profile: SearchProfile): void {
    this.openProfileDialog(profile);
  }

  deleteProfile(profile: SearchProfile): void {
    this.profileService.remove(profile.id).subscribe({
      next: () => {
        this.profiles.update(list => list.filter(p => p.id !== profile.id));
        if (this.filter.profileId === profile.id) this.onProfileChange(null);
      },
      error: () => this.snackbar.warn(this.translate.instant('tournamentDirectory.profile.saveError')),
    });
  }

  private openProfileDialog(profile: SearchProfile | null): void {
    const data: SearchProfileDialogData = { profile };
    this.dialog.open(SearchProfileDialogComponent, { data, width: '460px' })
      .afterClosed().subscribe(input => {
        if (!input) return;
        const request = profile
          ? this.profileService.update(profile.id, input)
          : this.profileService.create(input);

        request.subscribe({
          next: saved => {
            this.profiles.update(list => (profile
              ? list.map(p => (p.id === saved.id ? saved : p))
              : [...list, saved]));
            this.onProfileChange(saved.id);
          },
          error: () => this.snackbar.warn(this.translate.instant('tournamentDirectory.profile.saveError')),
        });
      });
  }

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

    this.ready = true;

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
  private storeView(): void {
    try {
      localStorage.setItem(TournamentDirectoryComponent.ViewKey, JSON.stringify({
        tab: this.tab,
        rangePreset: this.rangePreset,
        from: this.filter.from,
        to: this.filter.to,
        federation: this.filter.federation,
        speed: this.filter.speed,
        text: this.filter.text,
        weekendOnly: this.filter.weekendOnly,
        minPlayers: this.filter.minPlayers,
        profileId: this.filter.profileId,
        lat: this.filter.lat,
        lon: this.filter.lon,
        radiusKm: this.filter.radiusKm,
        placeLabel: this.placeLabel,
        kinds: this.filter.kinds,
        ageGroups: this.filter.ageGroups,
        genders: this.filter.genders,
        adultsOnly: this.filter.adultsOnly,
        hideLeagues: this.filter.hideLeagues,
        includeIgnored: this.filter.includeIgnored,
        calendarYear: this.calendarYear,
        calendarMonth: this.calendarMonth,
      }));
    } catch {
      // Gesperrter oder voller Speicher (Privatmodus) ist kein Grund, die Seite scheitern zu
      // lassen — dann faengt man eben wieder bei der Vorgabe an.
    }
  }

  private restoreView(): void {
    let stored: Record<string, unknown> | null = null;
    try {
      const raw = localStorage.getItem(TournamentDirectoryComponent.ViewKey);
      stored = raw ? JSON.parse(raw) : null;
    } catch {
      stored = null;                       // unlesbar/kaputt: Vorgabe bleibt stehen
    }
    if (!stored || typeof stored !== 'object') return;

    const tab = stored['tab'];
    if (tab === 'list' || tab === 'map' || tab === 'calendar') this.tab = tab;

    const preset = stored['rangePreset'];
    if (typeof preset === 'string' && (DIRECTORY_RANGE_PRESETS as string[]).includes(preset)) {
      this.applyRangePreset(preset as DirectoryRangePreset, false);
      // Ein selbst gewaehlter Zeitraum steht nicht in rangeFor — der kommt aus dem Speicher.
      if (preset === 'custom') {
        this.filter.from = str(stored['from']);
        this.filter.to = str(stored['to']);
      }
    }

    this.filter.federation = str(stored['federation']);
    this.filter.text = str(stored['text']);
    this.filter.weekendOnly = stored['weekendOnly'] === true;
    this.filter.minPlayers = typeof stored['minPlayers'] === 'number' ? stored['minPlayers'] : null;
    this.filter.adultsOnly = stored['adultsOnly'] === true;
    this.filter.hideLeagues = stored['hideLeagues'] === true;
    this.filter.includeIgnored = stored['includeIgnored'] === true;

    const speed = stored['speed'];
    if (typeof speed === 'string' && this.speeds.includes(speed as TournamentSpeed)) {
      this.filter.speed = speed as TournamentSpeed;
    }

    this.filter.kinds = pick(stored['kinds'], DIRECTORY_KINDS);
    this.filter.ageGroups = pick(stored['ageGroups'], DIRECTORY_AGE_GROUPS);
    this.filter.genders = pick(stored['genders'], DIRECTORY_GENDERS);

    // Ein selbst gesetzter Mittelpunkt gehoert zur Ansicht wie der Zeitraum — ihn nach jedem
    // Turnierbesuch neu zu suchen war der Grund, dass es diesen Speicher ueberhaupt gibt.
    const lat = stored['lat'];
    const lon = stored['lon'];
    const radiusKm = stored['radiusKm'];
    if (typeof radiusKm === 'number') this.filter.radiusKm = radiusKm;
    if (typeof lat === 'number' && typeof lon === 'number') {
      this.filter.lat = lat;
      this.filter.lon = lon;
      this.placeLabel = str(stored['placeLabel']) ?? '';
      this.chosenPlaceLabel = this.placeLabel || null;
    }

    // Das Profil wird erst uebernommen, wenn es die Liste noch kennt (applyQueryParams) —
    // ein geloeschtes Profil darf die Umkreissuche nicht auf tote Koordinaten stellen.
    this.storedProfileId = typeof stored['profileId'] === 'number' ? stored['profileId'] : null;
    this.hasStoredProfile = 'profileId' in stored;

    const year = stored['calendarYear'];
    const month = stored['calendarMonth'];
    if (typeof year === 'number' && typeof month === 'number' && month >= 1 && month <= 12) {
      this.calendarYear = year;
      this.calendarMonth = month;
    }
  }

  private storedProfileId: number | null = null;
  private hasStoredProfile = false;
}

/** Aus dem gemerkten Zustand: ein nicht leerer String oder `null`. Alles andere ist Muell. */
function str(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}

/**
 * Aus dem gemerkten Zustand: die Werte einer Liste, die es noch GIBT. Ein Wert, den eine spaetere
 * Fassung nicht mehr kennt, wuerde sonst als Filter weiterlaufen und die Liste unerklaerlich leer
 * halten — und der Server wiese ihn mit 400 ab.
 */
function pick<T extends string>(value: unknown, allowed: readonly T[]): T[] {
  return Array.isArray(value) ? value.filter((v): v is T => allowed.includes(v as T)) : [];
}
