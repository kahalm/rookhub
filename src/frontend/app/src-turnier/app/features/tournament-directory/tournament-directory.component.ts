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

  selected: DirectoryEntry | null = null;

  ngOnInit(): void {
    this.applyRangePreset('quarter', false);

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

  get mapCentre(): { lat: number; lon: number; radiusKm: number } | null {
    const profile = this.activeProfile;
    return profile ? { lat: profile.lat, lon: profile.lon, radiusKm: profile.radiusKm } : null;
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
    this.page = 1;
    this.selected = null;
    if (this.tab === 'list') this.loadList();
    if (this.tab === 'map' && this.lastBounds) this.loadPins(this.lastBounds);
    if (this.tab === 'calendar') this.loadCalendar();
  }

  // ----- Liste ------------------------------------------------------------

  loadList(): void {
    this.loading = true;
    this.directory.search(this.filter, this.page, this.pageSize).subscribe({
      next: page => {
        this.entries = this.page === 1 ? page.items : [...this.entries, ...page.items];
        this.total = page.total;
        this.truncated = page.truncated;
        this.loading = false;
      },
      error: () => {
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
    this.directory.map(this.filter, bounds).subscribe({
      next: pins => { this.pins = pins; this.mapLoading = false; },
      error: () => { this.mapLoading = false; },
    });
  }

  // ----- Kalender ---------------------------------------------------------

  onMonthChanged(event: { year: number; month: number }): void {
    this.calendarYear = event.year;
    this.calendarMonth = event.month;
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

  select(entry: DirectoryEntry): void {
    this.selected = entry;
  }

  closeDetail(): void {
    this.selected = null;
  }

  chessResultsUrl(entry: DirectoryEntry): string {
    return `https://chess-results.com/tnr${entry.chessResultsId}.aspx?lan=1`;
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
    if (profileId && this.profiles.some(p => p.id === profileId)) {
      this.filter.profileId = profileId;
    } else if (this.profiles.length > 0) {
      this.filter.profileId = this.profiles[0].id;
    }

    const tournamentId = params.get('t');
    if (tournamentId) {
      // Ein gemeldetes Turnier kann ausserhalb des aktuellen Umkreises oder abgesagt sein —
      // deshalb einzeln nachladen statt in der gefilterten Liste zu suchen.
      this.directory.get(tournamentId).subscribe({
        next: entry => (this.selected = entry),
        error: () => this.snackbar.warn(this.translate.instant('tournamentDirectory.unknownTournament')),
      });
    }

    this.loadList();
  }

  clearDeepLink(): void {
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }
}
