import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '@rh/shared/help-hint/help-hint.component';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentListService } from '../../core/tournament-list.service';
import { SearchProfileDialogComponent, SearchProfileDialogData } from './search-profile-dialog.component';
import { SearchProfileService } from './search-profile.service';
import { TournamentCalendarComponent } from './tournament-calendar.component';
import { TournamentDirectoryService } from './tournament-directory.service';
import { TournamentMapComponent } from './tournament-map.component';
import {
  DIRECTORY_RANGE_PRESETS, DirectoryCalendarDay, DirectoryEntry, DirectoryFilter, DirectoryRangePreset,
  EMPTY_FILTER, SearchProfile, TournamentSpeed, rangeFor,
} from './tournament-directory.model';

type ViewTab = 'list' | 'map' | 'calendar';

/**
 * Turnierkalender: Liste, Karte und Monatsansicht auf DENSELBEN Filterzustand.
 *
 * Der Filter liegt bewusst in dieser Huelle und nicht in den drei Ansichten — sonst zeigt die
 * Karte etwas anderes als die Liste darueber, obwohl dieselbe Filterleiste daruebersteht. Der
 * Mittelpunkt der Umkreissuche kommt aus einem gespeicherten Suchprofil; dasselbe Profil steuert
 * nachts die Benachrichtigung, damit „was mir gemeldet wird" und „was ich hier sehe" nicht
 * auseinanderlaufen.
 */
@Component({
  selector: 'app-tournament-directory',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatChipsModule, MatDialogModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatSelectModule,
    MatSlideToggleModule, MatTabsModule, MatTooltipModule, TranslatePipe,
    LoadingSpinnerComponent, HelpHintComponent, TournamentCalendarComponent, TournamentMapComponent,
  ],
  templateUrl: './tournament-directory.component.html',
  styleUrls: ['./tournament-directory.component.scss'],
})
export class TournamentDirectoryComponent implements OnInit {
  private readonly directory = inject(TournamentDirectoryService);
  private readonly profileService = inject(SearchProfileService);
  private readonly tournaments = inject(TournamentListService);
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
  readonly pageSize = 50;

  /** Vorgabe: das kommende Quartal (siehe rangeFor). */
  rangePreset: DirectoryRangePreset = 'quarter';
  /** Die Zusatzfilter stehen eingeklappt — die Leiste war sonst die halbe Seite. */
  filtersOpen = false;
  tilesFailed = false;

  tab: ViewTab = 'list';
  filter: DirectoryFilter = { ...EMPTY_FILTER };
  profiles: SearchProfile[] = [];

  entries: DirectoryEntry[] = [];
  total = 0;
  truncated = false;
  page = 1;
  loading = false;

  pins: DirectoryEntry[] = [];
  mapLoading = false;
  private lastBounds: string | null = null;

  calendarDays: DirectoryCalendarDay[] = [];
  calendarYear = new Date().getFullYear();
  calendarMonth = new Date().getMonth() + 1;
  calendarLoading = false;

  /**
   * Schluessel der gemerkten Ansicht. Ohne sie faellt der Weg „Turnier oeffnen → zurueck" auf die
   * Vorgabefilter zurueck — man muesste Zeitraum, Reiter und Monat jedes Mal neu einstellen.
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

    this.profileService.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: profiles => {
        this.profiles = profiles;
        this.applyQueryParams();
      },
      error: () => this.applyQueryParams(),
    });
  }

  // ----- Filter -----------------------------------------------------------

  get activeProfile(): SearchProfile | null {
    return this.profiles.find(p => p.id === this.filter.profileId) ?? null;
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
    const key = profile ? `${profile.lat}|${profile.lon}|${profile.radiusKm}` : '';
    if (key !== this.centreKey) {
      this.centreKey = key;
      this.centreCache = profile
        ? { lat: profile.lat, lon: profile.lon, radiusKm: profile.radiusKm }
        : null;
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
    this.reload();
  }

  onTilesFailed(): void {
    this.tilesFailed = true;
  }

  onSpeedChange(speed: TournamentSpeed | null): void {
    this.filter.speed = speed ?? null;
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
      + (this.filter.minPlayers ? 1 : 0);
  }

  resetFilter(): void {
    this.filter = { ...EMPTY_FILTER };
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
    this.loading = true;
    const generation = ++this.listGeneration;
    const requestedPage = this.page;
    this.directory.search(this.filter, requestedPage, this.pageSize).subscribe({
      next: page => {
        if (generation !== this.listGeneration) return;
        this.entries = requestedPage === 1 ? page.items : [...this.entries, ...page.items];
        this.total = page.total;
        this.truncated = page.truncated;
        this.loading = false;
      },
      error: () => {
        if (generation !== this.listGeneration) return;
        this.loading = false;
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

  private loadPins(bounds: string): void {
    this.mapLoading = true;
    // Beim erneuten Betreten des Karten-Reiters laufen zwei Abfragen gegeneinander: `reload()`
    // fragt mit dem GEMERKTEN Ausschnitt, die frisch aufgebaute Karte meldet direkt danach ihren
    // eigenen. Ohne Zaehler gewinnt die zufaellig spaetere Antwort.
    const generation = ++this.pinsGeneration;
    this.directory.map(this.filter, bounds).subscribe({
      next: pins => {
        if (generation !== this.pinsGeneration) return;
        this.pins = pins;
        this.mapLoading = false;
      },
      error: () => { if (generation === this.pinsGeneration) this.mapLoading = false; },
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
    this.calendarLoading = true;
    this.directory.calendar(this.filter, this.calendarYear, this.calendarMonth).subscribe({
      next: days => { this.calendarDays = days; this.calendarLoading = false; },
      error: () => {
        this.calendarLoading = false;
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
   * „Merken" legt ein ganz normales Turnier-Abo an. Zwei Wirkungen: Termin- und Ortsaenderungen
   * werden gemeldet, und der Abo-Refresh holt die Teilnehmer- und Rundendaten zum Spielbeginn von
   * selbst nach — man muss das Turnier nicht von Hand importieren.
   */
  bookmark(entry: DirectoryEntry): void {
    this.tournaments.subscribe(entry.chessResultsId, entry.name).subscribe({
      next: () => {
        entry.subscribed = true;
        this.snackbar.success(this.translate.instant('tournamentDirectory.bookmarked'));
      },
      error: () => this.snackbar.warn(this.translate.instant('tournamentDirectory.bookmarkError')),
    });
  }

  trackById = (_: number, entry: DirectoryEntry) => entry.chessResultsId;

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
        this.profiles = this.profiles.filter(p => p.id !== profile.id);
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
            this.profiles = profile
              ? this.profiles.map(p => (p.id === saved.id ? saved : p))
              : [...this.profiles, saved];
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

    const profileId = Number(params.get('profile'));
    const storedIsValid = this.storedProfileId != null
      && this.profiles.some(p => p.id === this.storedProfileId);
    if (profileId && this.profiles.some(p => p.id === profileId)) {
      this.filter.profileId = profileId;
    } else if (storedIsValid) {
      this.filter.profileId = this.storedProfileId;
    } else if (this.hasStoredProfile && this.storedProfileId === null) {
      this.filter.profileId = null;        // „kein Umkreis" war eine WAHL, keine fehlende Angabe
    } else if (this.profiles.length > 0) {
      this.filter.profileId = this.profiles[0].id;
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

    const speed = stored['speed'];
    if (typeof speed === 'string' && this.speeds.includes(speed as TournamentSpeed)) {
      this.filter.speed = speed as TournamentSpeed;
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
