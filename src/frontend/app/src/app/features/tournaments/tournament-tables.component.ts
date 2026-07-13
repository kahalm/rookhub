import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSortModule, Sort } from '@angular/material/sort';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { TournamentPlayer, TournamentTeam, DisplayPairing } from '../../core/models';

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
  imports: [CommonModule, FormsModule, MatTabsModule, MatTableModule, MatFormFieldModule, MatSelectModule, MatIconModule, MatSlideToggleModule, MatSortModule, TranslatePipe, LoadingSpinnerComponent],
  templateUrl: './tournament-tables.component.html',
  styleUrls: ['./tournament-tables.component.scss'],
})
export class TournamentTablesComponent {
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

  isFavorite(player: TournamentPlayer): boolean {
    return this.favoriteSnrs.has(player.snr);
  }

  isTeamFavorite(team: TournamentTeam): boolean {
    return this.favoriteTeamSnrs.has(team.snr);
  }
}
