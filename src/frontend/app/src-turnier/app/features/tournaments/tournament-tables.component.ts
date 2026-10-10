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
  imports: [CommonModule, FormsModule, MatTabsModule, MatTableModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule, MatButtonModule, MatTooltipModule, MatSlideToggleModule, MatSortModule, TranslatePipe, LoadingSpinnerComponent],
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

  /** Beschriftung einer Handy-Sortierung — die Vereinsspalte ist in Mannschaftsturnieren die Mannschaft. */
  mobileSortLabel(key: MobileSortKey): string {
    if (key === 'team') return this.hasTeamPairings ? 'tournaments.players.team' : 'tournaments.players.club';
    return `tournaments.players.${key}`;
  }

  pickMobileSort(key: MobileSortKey): void {
    this.mobileSort = key;
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
