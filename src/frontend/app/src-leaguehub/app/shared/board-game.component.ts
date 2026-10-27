import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GameReplayComponent, ReplayMark } from '@rh/shared/player-card/game-replay.component';
import { HandoffService } from '@rh/core/handoff.service';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { LineupGame, LineupsApiService, ReplayStart, formatMoves } from '../core/lineups';
import { GameBadgesComponent } from './game-badges.component';

/**
 * Die vorhandene Partie an einem Brett der Aufstellungen (0.724.0, Wunsch 2026-10-08: „wenn ich das hab, dann sollt er nicht
 * Züge eingeben lassen, sondern die Partie ausweisen"): „Partie vorhanden · 81 Halbzüge · 1.e4 c5 2.Sf3 …" mit „Nachspielen"
 * (klappt `lh-game-replay` auf; das PGN kommt erst dann — Vereinspartie über `club/games/{id}` samt Bewertungen, Spielerkarte
 * über `…/round/{r}/games?team=` wie in `lh-fixture`), an einer Vereinspartie „Analyse" (RookHub per Einmal-Code) und mit
 * `canEdit` „Bearbeiten"/„Korrigieren" wie in `lh-fixture`. Alle vier als `.btn-link` (0.727.2: gleiche Farbe und Grundlinie).
 * Über einen Teilen-Link (0.838.0, `shareToken`): „Nachspielen" nur an der geteilten Begegnung (`shared`, PGN über
 * `/s/{token}/games`), keine Analyse, kein Bearbeiten — ohne Anmeldung gibt es dafür keine Wege.
 * Seit 0.848.0 (Vereinsübersicht, Wunsch 2026-10-26): `markCount` zeigt einen Marker „◆ n interessante Stellungen", und beim
 * Nachspielen einer Vereinspartie (angemeldet) kommen deren markierte Stellungen als Sprungmarken mit. Seit 0.849.0
 * (Wunsch 2026-10-27: „zeige auch !! an") daneben die brillanten Züge; seit 0.852.0 beides als Abzeichen „+n"/„!!n"
 * (`lh-game-badges`), ein Klick klappt das Nachspielen auf und springt hin.
 */
@Component({
  selector: 'lh-board-game',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GameReplayComponent, RouterLink, GameBadgesComponent],
  template: `
    @let g = game();
    <span class="bg">
      <span class="bg-what">Partie vorhanden@if (!brief()) { · {{ g.plies }} Halbzüge@if (g.firstMoves.length) { · <span class="bg-moves">{{ shown() }}</span>}}</span>
      <lh-game-badges [marks]="markCount()" [brilliant]="brilliantPlies().length" (pick)="openAt($event)" />
      <span class="bg-actions">
        @if (!shareToken() || shared()) {
          <button type="button" class="btn-link bg-replay" [attr.aria-expanded]="open()" (click)="toggle()"
                  [attr.aria-label]="(open() ? 'Nachspielen schließen' : 'Nachspielen') + ', ' + title()">{{ open() ? 'Nachspielen schließen' : 'Nachspielen' }}</button>
        }
        @if (g.source === 'club' && g.clubGameId && (!shareToken() || shared())) {
          @if (rookHub) {
            <button type="button" class="btn-link bg-analysis" (click)="analyse(g.clubGameId)"
                    title="Analyse — RookHubs Partieseite mit Bewertungskurve, Fehlern und Zug-Klassen">Analyse</button>
          }
          @if (g.canEdit && !shareToken()) {
            <a class="btn-link bg-edit" [routerLink]="['/verein']" [queryParams]="{ bearbeiten: g.clubGameId }">Bearbeiten</a>
            <a class="btn-link bg-fix" [routerLink]="['/verein/partie', g.clubGameId, 'korrigieren']">Korrigieren</a>
          }
        }
      </span>
    </span>
    @if (open()) {
      @if (pgn(); as p) {
        <div class="bg-replay-box"><lh-game-replay [pgn]="p" [flipped]="flipped()" [evalsUrl]="evalsUrl()" [marks]="marks()" [startAt]="start()" /></div>
      } @else if (error()) {
        <p class="err" role="alert">{{ error() }}</p>
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
  /** 0.838.0: über einen Teilen-Link (ohne Anmeldung) — PGN nur über `/s/{token}/games`, keine Analyse, kein Bearbeiten. */
  readonly shareToken = input<string | null>(null);
  /** 0.838.0: das Brett gehört zur GETEILTEN Begegnung — nur deren Partien kennt der Link. */
  readonly shared = input(false);

  readonly open = signal(false);
  readonly pgn = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  private readonly hasEvals = signal(false);
  /** 0.848.0: so viele interessante Stellungen hat die Partie (Vereinsübersicht) — 0 = kein Marker. */
  /** Nur „Partie vorhanden“, ohne Halbzüge und erste Züge (Vereinsübersicht, 0.851.1, Wunsch 2026-10-27). */
  readonly brief = input(false);
  readonly markCount = input(0);
  /** Die brillanten Halbzüge („!!“) laut Rückblick der Analyse (0.849.0 als Zahl, seit 0.852.0 die Halbzüge für den Sprung). */
  readonly brilliantPlies = input<readonly number[]>([]);
  /** Klick auf ein Abzeichen (0.852.0): wohin das Nachspielen springt. */
  readonly start = signal<ReplayStart | null>(null);
  /** Die Sprungmarken der Vereinspartie — erst beim Nachspielen geladen; still leer, wenn das scheitert. */
  readonly marks = signal<ReplayMark[]>([]);
  readonly rookHub = rookHubUrlForLeagueHub();

  readonly shown = computed(() => {
    const g = this.game();
    return formatMoves(g.firstMoves) + (g.plies > g.firstMoves.length ? ' …' : '');
  });
  readonly evalsUrl = computed(() => {
    const g = this.game();
    return g.source === 'club' && g.clubGameId && this.hasEvals() && !this.shareToken() ? `/api/league/club/games/${g.clubGameId}/evals` : null;
  });

  /** Abzeichen angeklickt: Nachspielen aufklappen (falls zu) und zur ersten markierten Stellung bzw. zum ersten „!!“ springen. */
  openAt(kind: 'mark' | 'brilliant'): void {
    const ply = this.brilliantPlies()[0];
    this.start.set(kind === 'mark' ? { kind: 'mark' } : { kind: 'ply', ply: ply ?? 0 });
    if (!this.open()) void this.toggle();
  }

  async toggle(): Promise<void> {
    if (this.open()) { this.open.set(false); return; }
    this.open.set(true);
    if (this.pgn()) return;
    this.error.set(null);
    const g = this.game(), token = this.shareToken();
    try {
      if (token) {
        const p = await this.api.sharedPgn(token, this.board());
        if (p) this.pgn.set(p); else this.error.set('Die Partie ist nicht mehr da — bitte neu laden.');
      } else if (g.source === 'club' && g.clubGameId) {
        const d = await this.api.clubGame(g.clubGameId);
        this.loadMarks(g.clubGameId);
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

  /** Angemeldet RookHubs `club-games/{id}` (Einmal-Code), über den Teilen-Link `s/{token}/club-games/{id}` ohne Anmeldung (0.838.3). */
  /** Sprungmarken nachladen — ein Fehler (oder eine Partie ohne Marken) lässt das Nachspielen unberührt. */
  private loadMarks(id: number): void {
    Promise.resolve(this.api.clubGameMarks(id)).then(m => this.marks.set(m ?? []), () => this.marks.set([]));
  }

  analyse(id: number): void {
    const token = this.shareToken();
    // `?ask=1` (0.855.1): drüben fragt die Partieseite „mit/ohne Engine" — über den Teilen-Link nicht (ohne Konto kein „ohne Engine").
    void this.handoff.jumpToRookHub(token ? `s/${encodeURIComponent(token)}/club-games/${id}` : `club-games/${id}?ask=1`);
  }
}
