import { Component, ChangeDetectionStrategy, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { switchMap, take } from 'rxjs/operators';
import { AuthService } from '@rh/core/auth.service';
import { CrawlerPairing, TournamentPlayer } from '@rh/core/models';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { LoadErrorComponent } from '@rh/shared/load-error/load-error.component';
import { requireAccount } from '../../core/require-account';
import { TournamentDetailService } from './tournament-detail.service';
import { LocalScorePipe, chessTitle } from './tournament-table.util';
import { EncounterRow, PlayerRoundResult, buildEncounters } from './player-encounters.util';

export interface PlayerEncountersData {
  /** Kennung wie in der Adresse (`/tournaments/:id`, `/t/:id`). */
  tournamentId: string;
  /** Fuer „Einzelergebnisse holen" (Crawl der Spielerseite); ohne sie fehlt der Knopf. */
  chessResultsId: string | null;
  player: TournamentPlayer;
  /** Alle Spieler des Turniers — Gegner werden darueber klickbar und bekommen ihre Wertung. */
  players: TournamentPlayer[];
}

/** Oeffnet die Begegnungen eines Spielers (Inhalts-Dialog, DESIGN 5.6: bis 760 px, hier 560 px). */
export function openPlayerEncounters(dialog: MatDialog, data: PlayerEncountersData): void {
  dialog.open(PlayerEncountersDialogComponent, { data, width: '560px', maxWidth: '94vw', autoFocus: 'dialog' });
}

/** So oft (alle 3 s) wird nach „Einzelergebnisse holen" nachgesehen, ob die Spielerseite schon da ist. */
const POLL_TRIES = 15;
const POLL_MS = 3000;

/**
 * Die Begegnungen eines Spielers in diesem Turnier: je Runde Gegner, Farbe, Ergebnis — und die ausgeloste, noch offene
 * naechste Runde (Wunsch 2026-11-22). Im Mannschaftsturnier je Runde der Mannschaftskampf und, sobald die Spielerseite
 * geholt ist, Brett/Gegner/Ergebnis. Ein Gegner ist klickbar und wechselt die Ansicht auf ihn.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-player-encounters-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe, LoadingSpinnerComponent, LoadErrorComponent, LocalScorePipe],
  template: `
    <h2 mat-dialog-title class="enc-title">
      @if (chessTitle(player().title); as t) { <span class="enc-ptitle">{{ t }}</span> }
      {{ player().name }}
    </h2>
    <mat-dialog-content>
      <p class="enc-sub">
        @if (player().elo) { <span>{{ player().elo }}</span> }
        @if (player().teamName || player().club) { <span>{{ player().teamName || player().club }}</span> }
        @if (encounters(); as e) {
          @if (e.points !== null && e.played > 0) {
            <span class="enc-points">{{ 'tournaments.encounters.points' | translate: { points: pointsText(e.points), games: e.played } }}</span>
          }
        }
      </p>
      @if (loading()) {
        <app-loading-spinner />
      } @else if (loadError()) {
        <app-load-error [error]="loadError()" (retry)="load()" />
      } @else if (encounters(); as e) {
        @if (e.rows.length === 0) {
          <p class="empty-state">{{ 'tournaments.encounters.none' | translate }}</p>
        } @else {
          <ol class="enc-list">
            @for (row of e.rows; track row.round) {
              <li class="enc-row" [class.enc-next]="row.next" [class.enc-pending]="row.pending">
                <span class="enc-round">{{ 'tournaments.encounters.roundShort' | translate: { round: row.round } }}</span>
                <div class="enc-body">
                  @if (row.match; as m) {
                    <div class="enc-match">
                      <span class="enc-ha">{{ (m.home ? 'tournaments.encounters.homeVs' : 'tournaments.encounters.awayAt') | translate }}</span>
                      <span class="enc-team">{{ m.opponentTeam }}</span>
                      @if (m.score) { <span class="enc-score">{{ m.score | localScore }}</span> }
                    </div>
                  }
                  @if (row.opponent) {
                    <div class="enc-game">
                      @if (row.colour) {
                        <span class="enc-colour" [class.enc-white]="row.colour === 'white'" [class.enc-black]="row.colour === 'black'"
                              role="img" [attr.aria-label]="(row.colour === 'white' ? 'tournaments.pairings.white' : 'tournaments.pairings.black') | translate"
                              [attr.title]="(row.colour === 'white' ? 'tournaments.pairings.white' : 'tournaments.pairings.black') | translate"></span>
                      }
                      @if (row.board) { <span class="enc-board">{{ 'tournaments.players.boardShort' | translate }} {{ row.board }}</span> }
                      @if (opponentOf(row); as o) {
                        <button type="button" class="enc-opp team-link" (click)="switchTo(o)">{{ row.opponent }}</button>
                      } @else {
                        <span class="enc-opp">{{ row.opponent }}</span>
                      }
                      @if (eloOf(row); as elo) { <span class="enc-elo">{{ elo }}</span> }
                    </div>
                  } @else if (row.bye) {
                    <div class="enc-game enc-muted">{{ 'tournaments.encounters.bye' | translate }}</div>
                  } @else if (row.notPlayed) {
                    <div class="enc-game enc-muted">{{ 'tournaments.encounters.notPlayed' | translate }}</div>
                  }
                </div>
                <span class="enc-result">
                  @if (row.result !== null) {
                    {{ row.result }}
                  } @else if (row.next) {
                    <span class="enc-chip">{{ 'tournaments.encounters.next' | translate }}</span>
                  } @else if (row.pending) {
                    <span class="enc-muted">{{ 'tournaments.encounters.planned' | translate }}</span>
                  }
                </span>
              </li>
            }
          </ol>
        }
        @if (e.teamTournament) {
          <!-- Mannschaftsturnier: der Crawler kennt die Brettpaarungen nur aus der Spielerseite (chess-results art=9). -->
          @if (!e.hasIndividual) {
            <p class="enc-hint">{{ 'tournaments.encounters.teamOnlyHint' | translate }}</p>
          }
          @if (data.chessResultsId) {
            <button mat-stroked-button type="button" class="enc-fetch" (click)="fetchDetails()" [disabled]="fetching()">
              <mat-icon>{{ fetching() ? 'hourglass_top' : 'download' }}</mat-icon>
              {{ (fetching() ? 'tournaments.encounters.fetching' : e.hasIndividual ? 'tournaments.encounters.refetch' : 'tournaments.encounters.fetch') | translate }}
            </button>
            @if (fetchState() === 'timeout') { <p class="enc-hint">{{ 'tournaments.encounters.fetchSlow' | translate }}</p> }
            @if (fetchState() === 'failed') { <p class="enc-hint enc-error" role="alert">{{ 'tournaments.encounters.fetchFailed' | translate }}</p> }
          }
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .enc-title { overflow-wrap: anywhere; }
    .enc-ptitle { font-weight: 700; margin-right: 0.25rem; }
    .enc-sub { display: flex; flex-wrap: wrap; gap: 0.25rem 0.75rem; margin: 0 0 0.5rem; color: var(--mat-sys-on-surface-variant); }
    .enc-points { font-weight: 500; color: var(--mat-sys-on-surface); }
    .enc-list { list-style: none; margin: 0; padding: 0; }
    .enc-row {
      display: grid; grid-template-columns: 3rem 1fr auto; align-items: center; gap: 0.5rem;
      padding: 0.5rem 0.25rem; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent);
    }
    .enc-next { background: color-mix(in srgb, var(--rh-accent) 10%, transparent); border-radius: 6px; }
    .enc-round { font-variant-numeric: tabular-nums; color: var(--mat-sys-on-surface-variant); }
    .enc-body { min-width: 0; display: flex; flex-direction: column; gap: 0.15rem; }
    .enc-match, .enc-game { display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem 0.5rem; min-width: 0; }
    .enc-ha, .enc-board, .enc-elo { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; }
    .enc-team { font-weight: 500; overflow-wrap: anywhere; }
    .enc-score { font-variant-numeric: tabular-nums; }
    .enc-opp { overflow-wrap: anywhere; }
    button.enc-opp { background: none; border: 0; padding: 0; font: inherit; text-align: left; cursor: pointer; }
    .team-link { color: var(--rh-accent); text-decoration: underline; }
    .enc-colour { display: inline-block; width: 0.85rem; height: 0.85rem; border: 1px solid currentColor; border-radius: 2px; flex: none; }
    /* Steinfarbe, kein Zustand: absolut, in hell und dunkel gleich (umrandet, damit Weiß auf heller Fläche sichtbar bleibt). */
    .enc-white { background: white; }
    .enc-black { background: black; }
    .enc-result { font-weight: 600; font-variant-numeric: tabular-nums; text-align: right; min-width: 2rem; }
    .enc-chip {
      display: inline-block; padding: 0.1rem 0.5rem; border-radius: 999px; font-size: 0.75rem; font-weight: 500; white-space: nowrap;
      color: var(--rh-accent); border: 1px solid var(--rh-accent);
    }
    .enc-muted { color: var(--mat-sys-on-surface-variant); font-weight: 400; font-size: 0.875rem; }
    .enc-hint { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; margin: 0.75rem 0 0.5rem; }
    .enc-error { color: var(--rh-error); }
    .enc-fetch { margin-top: 0.25rem; }
    .empty-state { color: var(--mat-sys-on-surface-variant); }
  `]
})
export class PlayerEncountersDialogComponent {
  readonly chessTitle = chessTitle;
  readonly data = inject<PlayerEncountersData>(MAT_DIALOG_DATA);
  private readonly api = inject(TournamentDetailService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly player = signal<TournamentPlayer>(this.data.player);
  readonly loading = signal(true);
  readonly loadError = signal<unknown>(null);
  readonly fetching = signal(false);
  readonly fetchState = signal<'idle' | 'timeout' | 'failed'>('idle');
  private readonly pairings = signal<CrawlerPairing[] | null>(null);
  private readonly results = signal<PlayerRoundResult[]>([]);
  private pollSub?: Subscription;

  private readonly byName = new Map<string, TournamentPlayer>();
  private readonly bySnr = new Map<number, TournamentPlayer>();

  readonly encounters = computed(() => {
    const pairings = this.pairings();
    if (!pairings) return null;
    const p = this.player();
    return buildEncounters(p.name, p.teamName, pairings, this.results());
  });

  constructor() {
    for (const p of this.data.players ?? []) {
      this.byName.set(p.name.trim().toLowerCase(), p);
      this.bySnr.set(p.snr, p);
    }
    this.load();
  }

  /** Alle Runden einmal laden; im Mannschaftsturnier danach die Spielerseite. */
  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    const cached = this.pairings();
    if (cached) { this.loadResults(cached); return; }
    this.api.getAllPairings(this.data.tournamentId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (pairings) => {
        this.pairings.set(pairings ?? []);
        this.loadResults(pairings ?? []);
      },
      error: (err) => { this.loading.set(false); this.loadError.set(err ?? true); },
    });
  }

  private loadResults(pairings: CrawlerPairing[]): void {
    const team = pairings.length > 0 && 'homeTeam' in pairings[0];
    if (!team) { this.results.set([]); this.loading.set(false); return; }
    this.api.getPlayerResults(this.data.tournamentId, this.player().snr).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (r) => { this.results.set(r ?? []); this.loading.set(false); },
      // Die Spielerseite ist eine Zugabe: ohne sie stehen trotzdem die Mannschaftskaempfe da.
      error: () => { this.results.set([]); this.loading.set(false); },
    });
  }

  /** Ein Gegner aus der Startliste — dann ist er klickbar. */
  opponentOf(row: EncounterRow): TournamentPlayer | null {
    if (row.opponentSnr != null && this.bySnr.has(row.opponentSnr)) {
      const bySnr = this.bySnr.get(row.opponentSnr)!;
      // Die SNr der Spielerseite ist die Startnummer; nur nehmen, wenn der Name passt (Mannschafts-SNr weichen ab).
      if (row.opponent && bySnr.name.trim().toLowerCase() === row.opponent.trim().toLowerCase()) return bySnr;
    }
    return row.opponent ? this.byName.get(row.opponent.trim().toLowerCase()) ?? null : null;
  }

  eloOf(row: EncounterRow): number | null {
    return row.opponentElo ?? this.opponentOf(row)?.elo ?? null;
  }

  switchTo(p: TournamentPlayer): void {
    this.stopPolling();
    this.player.set(p);
    this.results.set([]);
    this.load();
  }

  pointsText(points: number): string {
    const whole = Math.floor(points);
    const half = points - whole >= 0.5;
    return half ? (whole === 0 ? '½' : `${whole}½`) : String(whole);
  }

  /**
   * Spielerseite holen lassen (Crawler, im Hintergrund) und dann bis zu 45 s nachsehen. Braucht ein Konto — Gaeste
   * gehen ueber die Anmeldung (wie jeder andere Knopf, der den Crawler beauftragt).
   */
  fetchDetails(): void {
    if (!this.data.chessResultsId || this.fetching()) return;
    if (!requireAccount(this.auth, this.router)) return;
    const snr = this.player().snr;
    const before = JSON.stringify(this.results());
    this.fetching.set(true);
    this.fetchState.set('idle');
    this.api.crawlPlayerDetails(this.data.chessResultsId, [snr]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.poll(snr, before),
      error: () => { this.fetching.set(false); this.fetchState.set('failed'); },
    });
  }

  private poll(snr: number, before: string): void {
    let tries = 0;
    this.pollSub = timer(POLL_MS, POLL_MS).pipe(
      take(POLL_TRIES),
      switchMap(() => this.api.getPlayerResults(this.data.tournamentId, snr)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (r) => {
        tries++;
        const rows = r ?? [];
        // Fertig, sobald sich die Spielerseite geaendert hat. Beim Aktualisieren ohne Aenderung endet es still.
        if (rows.length > 0 && JSON.stringify(rows) !== before) {
          this.results.set(rows);
          this.stopPolling();
        } else if (tries >= POLL_TRIES) {
          this.fetching.set(false);
          if (rows.length === 0) this.fetchState.set('timeout');
        }
      },
      error: () => { this.fetching.set(false); this.fetchState.set('failed'); },
    });
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
    this.fetching.set(false);
  }
}
