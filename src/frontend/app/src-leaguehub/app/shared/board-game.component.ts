import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GameReplayComponent } from '@rh/shared/player-card/game-replay.component';
import { HandoffService } from '@rh/core/handoff.service';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { LineupGame, LineupsApiService, formatMoves } from '../core/lineups';

/**
 * Die vorhandene Partie an einem Brett der Aufstellungen (0.724.0, Wunsch 2026-10-08: „wenn ich das hab, dann sollt er nicht
 * Züge eingeben lassen, sondern die Partie ausweisen"): „Partie vorhanden · 81 Halbzüge · 1.e4 c5 2.Sf3 …" mit „Nachspielen"
 * (klappt `lh-game-replay` auf; das PGN kommt erst dann — Vereinspartie über `club/games/{id}` samt Bewertungen, Spielerkarte
 * über `…/round/{r}/games?team=` wie in `lh-fixture`), an einer Vereinspartie „Analyse" (RookHub per Einmal-Code) und mit
 * `canEdit` „Bearbeiten"/„Korrigieren" wie in `lh-fixture`. Alle vier als `.btn-link` (0.727.2: gleiche Farbe und Grundlinie).
 */
@Component({
  selector: 'lh-board-game',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GameReplayComponent, RouterLink],
  template: `
    @let g = game();
    <span class="bg">
      <span class="bg-what">Partie vorhanden · {{ g.plies }} Halbzüge@if (g.firstMoves.length) { · <span class="bg-moves">{{ shown() }}</span>}</span>
      <span class="bg-actions">
        <button type="button" class="btn-link bg-replay" [attr.aria-expanded]="open()" (click)="toggle()"
                [attr.aria-label]="(open() ? 'Nachspielen schließen' : 'Nachspielen') + ', ' + title()">{{ open() ? 'Nachspielen schließen' : 'Nachspielen' }}</button>
        @if (g.source === 'club' && g.clubGameId) {
          @if (rookHub) {
            <button type="button" class="btn-link bg-analysis" (click)="analyse(g.clubGameId)"
                    title="Analyse — RookHubs Partieseite mit Bewertungskurve, Fehlern und Zug-Klassen">Analyse</button>
          }
          @if (g.canEdit) {
            <a class="btn-link bg-edit" [routerLink]="['/verein']" [queryParams]="{ bearbeiten: g.clubGameId }">Bearbeiten</a>
            <a class="btn-link bg-fix" [routerLink]="['/verein/partie', g.clubGameId, 'korrigieren']">Korrigieren</a>
          }
        }
      </span>
    </span>
    @if (open()) {
      @if (pgn(); as p) {
        <div class="bg-replay-box"><lh-game-replay [pgn]="p" [flipped]="flipped()" [evalsUrl]="evalsUrl()" /></div>
      } @else if (error()) {
        <p class="err">{{ error() }}</p>
      } @else {
        <p class="muted">Lade Partie …</p>
      }
    }
  `,
  styles: [`
    .bg { display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 10px; font-size: 14px; }
    .bg-what { overflow-wrap: anywhere; }
    .bg-moves { font-family: var(--body); }
    /* 0.727.2: alle vier Aktionen dieselbe Art (.btn-link) — bis dahin waren „Bearbeiten"/„Korrigieren" nackte <a> (Browser-
       Linkfarbe, ohne das Polster der Knöpfe) und standen höher als „Nachspielen"/„Analyse". Grundlinie statt Oberkante. */
    .bg-actions { display: inline-flex; flex-wrap: wrap; align-items: baseline; gap: 2px 10px; white-space: nowrap; }
    .bg-replay-box { margin: 8px 0 4px; }
  `],
})
export class BoardGameComponent {
  private readonly api = inject(LineupsApiService);
  private readonly handoff = inject(HandoffService);

  readonly game = input.required<LineupGame>();
  readonly tnr = input.required<number>();
  readonly round = input.required<number>();
  /** Eine Mannschaft der Begegnung — für das PGN einer Spielerkarten-Partie (`…/games?team=`). */
  readonly team = input.required<string>();
  readonly board = input.required<number>();
  readonly title = input('');
  readonly flipped = input(false);

  readonly open = signal(false);
  readonly pgn = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  private readonly hasEvals = signal(false);
  readonly rookHub = rookHubUrlForLeagueHub();

  readonly shown = computed(() => {
    const g = this.game();
    return formatMoves(g.firstMoves) + (g.plies > g.firstMoves.length ? ' …' : '');
  });
  readonly evalsUrl = computed(() => {
    const g = this.game();
    return g.source === 'club' && g.clubGameId && this.hasEvals() ? `/api/league/club/games/${g.clubGameId}/evals` : null;
  });

  async toggle(): Promise<void> {
    if (this.open()) { this.open.set(false); return; }
    this.open.set(true);
    if (this.pgn()) return;
    this.error.set(null);
    const g = this.game();
    try {
      if (g.source === 'club' && g.clubGameId) {
        const d = await this.api.clubGame(g.clubGameId);
        this.hasEvals.set(!!d.analysis);
        this.pgn.set(d.pgn);
      } else {
        const p = await this.api.fixturePgn(this.tnr(), this.round(), this.team(), this.board());
        if (p) this.pgn.set(p); else this.error.set('Die Partie ist nicht mehr da — bitte neu laden.');
      }
    } catch {
      this.error.set('Die Partie konnte nicht geladen werden.');
    }
  }

  analyse(id: number): void {
    void this.handoff.jumpToRookHub(`club-games/${id}`);
  }
}
