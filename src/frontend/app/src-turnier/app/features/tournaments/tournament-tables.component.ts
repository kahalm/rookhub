import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatMenuModule } from '@angular/material/menu';
import { IconLabelDirective } from '@rh/shared/icon-label/icon-label.directive';
import { MatSortModule, Sort } from '@angular/material/sort';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { TournamentPlayer, TournamentTeam, DisplayPairing } from '@rh/core/models';
import { chessTitle } from './tournament-table.util';

/**
 * Rein präsentationale Darstellung der Turnier-Tabs (Spieler/Teams/Paarungen)
 * mit Desktop-Tabellen, Mobil-Karten, Favoriten-Sternen und Runden-Auswahl.
 *
 * Enthält KEINE Datenquelle/Logik — beide Container (tournament-detail mit
 * Server-Favoriten/Monitor, public-tournament mit localStorage) reichen die
 * Daten als @Input() herein und behandeln Interaktionen über die @Output().
 * Die tournament-detail-spezifische Action-Bar (subscribe/refresh/monitor)
 * bleibt bewusst im Container.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-tournament-tables',
  standalone: true,
  imports: [CommonModule, FormsModule, MatTabsModule, MatTableModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule, MatButtonModule, MatTooltipModule, MatSlideToggleModule, MatSortModule, MatMenuModule, IconLabelDirective, TranslatePipe, LoadingSpinnerComponent],
  templateUrl: './tournament-tables.component.html',
  styleUrls: ['./tournament-tables.component.scss'],
})
export class TournamentTablesComponent {
  /** Nur echte Titel anzeigen (t-title-col). */
  readonly chessTitle = chessTitle;

  // --- Daten ---
  @Input() players: TournamentPlayer[] = [];
  @Input() teams: TournamentTeam[] = [];
  @Input() displayedPlayers: TournamentPlayer[] = [];
  @Input() displayedTeams: TournamentTeam[] = [];
  @Input() displayedPairings: DisplayPairing[] = [];

  // --- Spaltenkonfiguration ---
  @Input() playerColumns: string[] = [];
  @Input() teamColumns: string[] = [];
  @Input() pairingColumns: string[] = [];

  /** Gemerkt je Eingabe: ein neues Array je Durchlauf liesse mat-table die Kopfzeile neu zeichnen. */
  private columnsCache?: { columns: string[]; players: TournamentPlayer[]; result: string[] };

  /**
   * Die Spieler-Spalten der Desktop-Tabelle. Die Spalte „Titel" entfaellt, wenn KEIN Spieler einen echten
   * Titel traegt — eine durchgehend leere Spalte („—" bei allen 222 Spielern der ECo-C Extraliga) kostet
   * nur Breite (UI-Sweep 2026-10-11, r2-t-title-empty). Gilt fuer /tournaments/:id und /t/:id.
   */
  get shownPlayerColumns(): string[] {
    const cache = this.columnsCache;
    if (cache && cache.columns === this.playerColumns && cache.players === this.players) return cache.result;
    const noTitles = this.players.length > 0 && this.players.every(p => !chessTitle(p.title));
    const result = noTitles ? this.playerColumns.filter(c => c !== 'title') : this.playerColumns;
    this.columnsCache = { columns: this.playerColumns, players: this.players, result };
    return result;
  }

  // --- Lade-Zustand ---
  @Input() playersLoading = false;
  @Input() teamsLoading = false;
  @Input() pairingsLoading = false;

  // --- Favoriten / Filter ---
  @Input() hasFavorites = false;
  @Input() showFavoritesOnly = false;
  @Input() favoriteSnrs: Set<number> = new Set();
  @Input() favoriteTeamSnrs: Set<number> = new Set();

  // --- Struktur / Runden / Tabs ---
  @Input() hasTeamPairings = false;
  @Input() rounds: number[] = [];
  @Input() selectedRound = 1;
  @Input() selectedTabIndex = 0;

  // --- Interaktionen ---
  @Output() tabChange = new EventEmitter<{ index: number }>();
  @Output() favoritesToggle = new EventEmitter<boolean>();
  @Output() playerSort = new EventEmitter<Sort>();
  @Output() teamSort = new EventEmitter<Sort>();
  @Output() pairingSort = new EventEmitter<Sort>();
  @Output() roundChange = new EventEmitter<number>();
  @Output() toggleFavorite = new EventEmitter<TournamentPlayer>();
  @Output() toggleTeamFavorite = new EventEmitter<TournamentTeam>();
  @Output() showTeamPlayers = new EventEmitter<string>();

  /** Laeuft „Vereine nachtragen" gerade? Sperrt den Knopf und zeigt es an. */
  @Input() clubsBusy = false;
  @Output() fillClubs = new EventEmitter<void>();

  /**
   * Spieler, bei denen gar kein Verein steht (weder aus der Startliste noch uebernommen) und die
   * sich suchen lassen (FIDE-ID — ohne sie waere jeder Namensvetter ein Kandidat). Nur in
   * Einzelturnieren: dort ist die Spalte der Verein, in Mannschaftsturnieren die Mannschaft.
   */
  get playersWithoutClub(): number {
    if (this.hasTeamPairings) return 0;
    return this.players.filter(p => !p.teamName && !p.club && !!p.fideId && p.fideId !== '0').length;
  }

  // --- Spielersuche und Handy-Sortierung (Codereview UX-080) ---
  // Eine Landesliga hat 178 Spieler: ohne Suche fand man den eigenen Namen (oder die Vereins-
  // kollegen, die man mit Stern markieren will) nur durch Scrollen, am Handy — Karten statt
  // Tabelle — auch ohne Sortierung. Reiner Anzeigezustand dieser Komponente: beide Container
  // reichen die vollstaendige Liste herein, gefiltert wird hier, sortiert weiter dort.

  /** Suchtext ueber Name, Mannschaft und Verein. */
  playerQuery = '';

  /** Gewaehlte Sortierung im Handy-Menue (die Desktop-Tabelle sortiert ueber ihre Spaltenkoepfe). */
  mobileSort: MobileSortKey = 'snr';
  readonly mobileSortKeys: readonly MobileSortKey[] = ['snr', 'name', 'elo', 'team'];

  /** Gemerkt je Eingabe: ein neues Array je Durchlauf liesse mat-table jedes Mal neu zeichnen. */
  private shownCache?: { source: TournamentPlayer[]; query: string; result: TournamentPlayer[] };

  /** Die angezeigten Spieler nach der Suche; jedes Suchwort muss in Name, Mannschaft oder Verein stehen. */
  get shownPlayers(): TournamentPlayer[] {
    const cache = this.shownCache;
    if (cache && cache.source === this.displayedPlayers && cache.query === this.playerQuery) return cache.result;
    const words = fold(this.playerQuery).split(/[\s,]+/).filter(Boolean);
    const result = words.length === 0 ? this.displayedPlayers : this.displayedPlayers.filter(p => {
      const haystack = fold([p.name, p.teamName, p.club].filter(Boolean).join(' '));
      return words.every(word => haystack.includes(word));
    });
    this.shownCache = { source: this.displayedPlayers, query: this.playerQuery, result };
    return result;
  }

  // --- Portionen der Spielerliste (d2-t-players-paging) ---

  /** So viele Spieler zeigt die Liste je Portion; „Weitere 50 anzeigen" haengt die naechste an. */
  static readonly PLAYER_PAGE = 50;
  playerLimit = TournamentTablesComponent.PLAYER_PAGE;

  /** Bei Suche oder „Nur Favoriten" ist die Liste ohnehin kurz und gewollt — dann keine Grenze. */
  private get playerLimitActive(): boolean {
    return !this.playerQuery.trim() && !this.showFavoritesOnly;
  }

  private visibleCache?: { source: TournamentPlayer[]; limit: number; result: TournamentPlayer[] };

  /** Was Tabelle und Karten wirklich zeigen: die ersten `playerLimit` der gesuchten/sortierten Liste. */
  get visiblePlayers(): TournamentPlayer[] {
    const source = this.shownPlayers;
    const limit = this.playerLimitActive ? this.playerLimit : Number.POSITIVE_INFINITY;
    const cache = this.visibleCache;
    if (cache && cache.source === source && cache.limit === limit) return cache.result;
    const result = source.length > limit ? source.slice(0, limit) : source;
    this.visibleCache = { source, limit, result };
    return result;
  }

  get hiddenPlayerCount(): number {
    return this.shownPlayers.length - this.visiblePlayers.length;
  }

  get nextPlayerPortion(): number {
    return Math.min(TournamentTablesComponent.PLAYER_PAGE, this.hiddenPlayerCount);
  }

  showMorePlayers(): void {
    this.playerLimit += TournamentTablesComponent.PLAYER_PAGE;
  }

  /** Neue Suche oder Sortierung: wieder bei der ersten Portion anfangen. */
  private resetPlayerLimit(): void {
    this.playerLimit = TournamentTablesComponent.PLAYER_PAGE;
  }

  onPlayerQuery(query: string): void {
    this.playerQuery = query ?? '';
    this.resetPlayerLimit();
  }

  onDesktopPlayerSort(sort: Sort): void {
    this.resetPlayerLimit();
    this.playerSort.emit(sort);
  }

  /** Beschriftung einer Handy-Sortierung — die Vereinsspalte ist in Mannschaftsturnieren die Mannschaft. */
  mobileSortLabel(key: MobileSortKey): string {
    if (key === 'team') return this.hasTeamPairings ? 'tournaments.players.team' : 'tournaments.players.club';
    return `tournaments.players.${key}`;
  }

  pickMobileSort(key: MobileSortKey): void {
    this.mobileSort = key;
    this.resetPlayerLimit();
    // Elo absteigend: wer nach Wertung sortiert, sucht die Staerksten oben.
    this.playerSort.emit({ active: key, direction: key === 'elo' ? 'desc' : 'asc' });
  }

  isFavorite(player: TournamentPlayer): boolean {
    return this.favoriteSnrs.has(player.snr);
  }

  isTeamFavorite(team: TournamentTeam): boolean {
    return this.favoriteTeamSnrs.has(team.snr);
  }
}

export type MobileSortKey = 'snr' | 'name' | 'elo' | 'team';

/** Fuer die Suche: ohne Gross/klein und ohne Akzente — „sasa" findet „Saša", „sk" findet „ŠK". */
function fold(text: string): string {
  return text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
}
