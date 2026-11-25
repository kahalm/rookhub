import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { EinkService } from '@rh/core/eink.service';
import { apiErrorText } from '@rh/core/api-error';
import { SnackbarService } from '@rh/core/snackbar.service';
import { IconLabelDirective } from '@rh/shared/icon-label/icon-label.directive';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { LoadErrorComponent } from '@rh/shared/load-error/load-error.component';
import { requireAccount } from '../../../core/require-account';
import { LocalScorePipe } from '../tournament-table.util';
import { LiveBoardCardComponent } from './live-board-card.component';
import { LiveBoardSlot, LiveGame, LiveMatchSlot, LiveRoundLayout, LiveState, LiveStatus } from './live-boards.models';
import { LiveBoardsService } from './live-boards.service';
import { points, readLiveEval, scoreText, writeLiveEval } from './live-boards.util';

/** Ein Brett, wie es gezeichnet wird. */
interface ShownBoard { key: string; board: number; game: LiveGame; flipped: boolean; }
/** Eine Begegnung mit Live-Stand. */
interface ShownMatch { match: LiveMatchSlot; home: string; away: string; boards: ShownBoard[]; }

/**
 * Live-Bretter `/tournaments/:id/live` (1.014.0): die Partien einer Runde aus der verknüpften Lichess-Übertragung,
 * live über Server-Sent Events. Mannschaftsturnier: je Begegnung die Bretter (mit `?team=` nur die Begegnung dieser
 * Mannschaft, Bretter aus ihrer Sicht), oben der Mannschaftsstand. Einzelturnier: alle Bretter der Runde, mit
 * `?player=` nur das Brett dieses Spielers. Runde über `?round=`. Ohne Verknüpfung ein leiser Hinweis und — mit Konto —
 * das Feld zum Verknüpfen.
 */
@Component({
  selector: 'app-live-boards',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatIconModule, MatSelectModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule,
    TranslatePipe, IconLabelDirective, LoadingSpinnerComponent, LoadErrorComponent, LiveBoardCardComponent, LocalScorePipe],
  template: `
    <div class="live-page">
      <div class="live-head">
        <a mat-icon-button [routerLink]="['/tournaments', id]" [appIconLabel]="'tournaments.live.back' | translate">
          <mat-icon>arrow_back</mat-icon>
        </a>
        <div class="live-head-text">
          <h1 class="live-title">{{ 'tournaments.live.title' | translate }}</h1>
          @if (status()?.tourName; as name) {
            <a class="live-source" [href]="status()!.url" target="_blank" rel="noopener noreferrer">
              {{ name }} · lichess.org<mat-icon class="inline-icon" aria-hidden="true">open_in_new</mat-icon>
            </a>
          }
        </div>
      </div>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (loadFailed()) {
        <app-load-error (retry)="load()" />
      } @else if (!status()?.linked) {
        <section class="live-hint" role="note">
          <mat-icon aria-hidden="true">live_tv</mat-icon>
          <div class="live-hint-body">
            <p>{{ 'tournaments.live.notLinked' | translate }}</p>
            @if (loggedIn) {
              <form class="link-form" (submit)="$event.preventDefault(); link()">
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="link-field">
                  <mat-label>{{ 'tournaments.live.linkLabel' | translate }}</mat-label>
                  <input matInput type="url" [value]="linkUrl()" (input)="linkUrl.set($any($event.target).value)"
                         placeholder="https://lichess.org/broadcast/…" autocomplete="off" />
                  <mat-hint>{{ 'tournaments.live.linkHint' | translate }}</mat-hint>
                </mat-form-field>
                <button mat-flat-button type="submit" [disabled]="linking() || !linkUrl().trim()">
                  {{ 'tournaments.live.link' | translate }}
                </button>
              </form>
            } @else {
              <button mat-stroked-button type="button" (click)="loginToLink()">{{ 'tournaments.live.loginToLink' | translate }}</button>
            }
          </div>
        </section>
      } @else {
        <div class="live-toolbar">
          @if (status()!.rounds.length > 0) {
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="round-field">
              <mat-label>{{ 'tournaments.pairings.round' | translate }}</mat-label>
              <mat-select [value]="round()" (selectionChange)="setRound($event.value)">
                @for (r of status()!.rounds; track r.round) {
                  <mat-option [value]="r.round">
                    {{ 'tournaments.pairings.roundLabel' | translate:{ round: r.round } }}@if (r.ongoing) { · {{ 'tournaments.live.ongoing' | translate }} }
                  </mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
          @if (layout()?.team && teams().length > 0) {
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="team-field">
              <mat-label>{{ 'tournaments.live.team' | translate }}</mat-label>
              <mat-select [value]="team() ?? ''" (selectionChange)="setFilter('team', $event.value || null)">
                <mat-option value="">{{ 'tournaments.live.allMatches' | translate }}</mat-option>
                @for (t of teams(); track t) { <mat-option [value]="t">{{ t }}</mat-option> }
              </mat-select>
            </mat-form-field>
          }
          <mat-slide-toggle [checked]="evalOn()" (change)="setEval($event.checked)">{{ 'tournaments.live.engineBar' | translate }}</mat-slide-toggle>
          <span class="live-state" [class.is-live]="state() === 'live'" role="status">
            <mat-icon aria-hidden="true">{{ stateIcon() }}</mat-icon>{{ ('tournaments.live.state.' + state()) | translate }}
          </span>
        </div>
        @if (player(); as p) {
          <p class="filter-line">
            {{ 'tournaments.live.onlyPlayer' | translate:{ name: p } }}
            <button mat-button type="button" (click)="setFilter('player', null)">{{ 'tournaments.live.showAll' | translate }}</button>
          </p>
        }

        @if (!layout()) {
          <app-loading-spinner />
        } @else if (layout()!.team) {
          @for (m of matches(); track m.match.matchNumber) {
            <section class="match">
              <h2 class="match-head">
                <span class="mh-team">{{ m.home }}</span>
                <span class="mh-score">
                  @if (m.boards.length > 0 || m.match.homeScore !== null) {
                    {{ scoreOf(m, true) | localScore }} : {{ scoreOf(m, false) | localScore }}
                  } @else { – }
                </span>
                <span class="mh-team away">{{ m.away }}</span>
              </h2>
              @if (m.boards.length > 0) {
                <div class="board-grid">
                  @for (b of m.boards; track b.key) {
                    <app-live-board-card [game]="b.game" [board]="b.board" [flipped]="b.flipped" [showEval]="evalOn()"
                                         [receivedAt]="received()[b.game.id] ?? 0" [now]="now()" />
                  }
                </div>
              } @else {
                <p class="muted">{{ 'tournaments.live.noBoards' | translate }}</p>
              }
            </section>
          } @empty {
            <p class="muted">{{ 'tournaments.live.noGames' | translate }}</p>
          }
        } @else {
          @if (singleBoards().length > 0) {
            <div class="board-grid">
              @for (b of singleBoards(); track b.key) {
                <app-live-board-card [game]="b.game" [board]="b.board" [flipped]="b.flipped" [showEval]="evalOn()"
                                     [receivedAt]="received()[b.game.id] ?? 0" [now]="now()" />
              }
            </div>
          } @else {
            <p class="muted">{{ 'tournaments.live.noGames' | translate }}</p>
          }
          @if (missing() > 0 && !player()) {
            <p class="muted">{{ 'tournaments.live.notBroadcast' | translate:{ count: missing() } }}</p>
          }
        }
        @if (others().length > 0 && !team() && !player()) {
          <section class="match">
            <h2 class="match-head"><span class="mh-team">{{ 'tournaments.live.otherGames' | translate }}</span></h2>
            <div class="board-grid">
              @for (b of others(); track b.key) {
                <app-live-board-card [game]="b.game" [board]="b.board" [showEval]="evalOn()"
                                     [receivedAt]="received()[b.game.id] ?? 0" [now]="now()" />
              }
            </div>
          </section>
        }
      }
    </div>
  `,
  styles: [`
    .live-page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 8px 16px 24px; box-sizing: border-box; }
    @media (min-width: 2400px) { .live-page { max-width: min(var(--page-max-width-wide, 1880px), 96vw); } }
    .live-head { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .live-head-text { min-width: 0; }
    .live-title { margin: 0; font-size: 1.4rem; font-weight: 500; }
    .live-source { display: inline-flex; align-items: center; gap: 2px; font-size: 0.9rem; color: var(--rh-accent);
      overflow-wrap: anywhere; }
    .inline-icon { font-size: 16px; width: 16px; height: 16px; }
    .live-hint { display: flex; gap: 12px; align-items: flex-start; padding: 12px 16px; border-radius: 8px;
      border: 1px solid color-mix(in srgb, currentColor 14%, transparent); }
    .live-hint > mat-icon { color: var(--mat-sys-on-surface-variant); flex: 0 0 auto; }
    .live-hint-body { flex: 1 1 auto; min-width: 0; }
    .live-hint p { margin: 0 0 12px; }
    .link-form { display: flex; flex-wrap: wrap; gap: 8px; align-items: flex-start; }
    .link-field { flex: 1 1 280px; min-width: 0; }
    .link-form button { margin-top: 8px; }
    .live-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; margin-bottom: 12px; }
    .round-field { width: 12rem; }
    .team-field { flex: 0 1 18rem; min-width: 10rem; }
    .live-state { display: inline-flex; align-items: center; gap: 4px; color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; }
    .live-state mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .live-state.is-live { color: var(--rh-success); font-weight: 500; }
    .filter-line { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; margin: 0 0 8px; }
    .match { margin-bottom: 24px; }
    .match-head { display: grid; grid-template-columns: 1fr auto 1fr; align-items: baseline; gap: 12px; margin: 0 0 8px;
      font-size: 1.1rem; font-weight: 500; }
    .mh-team { min-width: 0; overflow-wrap: anywhere; }
    .mh-team.away { text-align: right; }
    .mh-score { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .board-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
    @media (max-width: 599px) { .board-grid { grid-template-columns: minmax(0, 1fr); } }
    .muted { color: var(--mat-sys-on-surface-variant); }
  `],
})
export class LiveBoardsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(LiveBoardsService);
  private readonly auth = inject(AuthService);
  private readonly eink = inject(EinkService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  id = '';
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly status = signal<LiveStatus | null>(null);
  readonly round = signal<number | null>(null);
  readonly team = signal<string | null>(null);
  readonly player = signal<string | null>(null);
  readonly evalOn = signal(readLiveEval());
  readonly state = signal<LiveState>('connecting');
  readonly layout = signal<LiveRoundLayout | null>(null);
  readonly games = signal<Record<string, LiveGame>>({});
  /** Wann jede Partie zuletzt kam (für die laufende Uhr). */
  readonly received = signal<Record<string, number>>({});
  readonly now = signal(Date.now());
  readonly linkUrl = signal('');
  readonly linking = signal(false);

  private streamSub: Subscription | null = null;
  private streamKey = '';
  private ticker: ReturnType<typeof setInterval> | null = null;

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  readonly teams = computed(() => {
    const l = this.layout();
    if (!l?.team) return [];
    return [...new Set(l.matches.flatMap(m => [m.home, m.away]))].sort((a, b) => a.localeCompare(b));
  });

  readonly matches = computed<ShownMatch[]>(() => {
    const l = this.layout();
    const games = this.games();
    const team = this.team();
    if (!l?.team) return [];
    return l.matches
      .filter(m => !team || m.home === team || m.away === team)
      .map(m => ({
        match: m, home: m.home, away: m.away,
        boards: m.boards.filter(b => b.gameId && games[b.gameId]).map(b => ({
          key: `${m.matchNumber}-${b.board}-${b.gameId}`, board: b.board, game: games[b.gameId!],
          // Aus Sicht der gewählten Mannschaft (ohne Wahl: der Heimmannschaft) — sie steht unten.
          flipped: (team === m.away) ? b.homeIsWhite === true : b.homeIsWhite === false,
        })),
      }));
  });

  readonly singleBoards = computed<ShownBoard[]>(() => {
    const l = this.layout();
    const games = this.games();
    const player = this.player();
    if (!l || l.team) return [];
    return l.boards
      .filter((b): b is LiveBoardSlot & { gameId: string } => !!b.gameId && !!games[b.gameId])
      .filter(b => !player || samePlayer(b.white, player) || samePlayer(b.black, player))
      .map(b => ({
        key: `${b.board}-${b.gameId}`, board: b.board, game: games[b.gameId],
        flipped: !!player && samePlayer(b.black, player),
      }));
  });

  /** Bretter der Paarung ohne Übertragung (Einzelturnier). */
  readonly missing = computed(() => (this.layout()?.boards ?? []).filter(b => !b.gameId || !this.games()[b.gameId]).length);

  readonly others = computed<ShownBoard[]>(() => {
    const l = this.layout();
    const games = this.games();
    return (l?.unassigned ?? []).filter(id => games[id]).map((id, i) => ({ key: 'u-' + id, board: i + 1, game: games[id], flipped: false }));
  });

  readonly stateIcon = computed(() => ({
    live: 'sensors', connecting: 'sync', reconnecting: 'sync_problem', finished: 'flag', offline: 'cloud_off',
  } as Record<LiveState, string>)[this.state()] ?? 'sync');

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => this.stopStream());
    this.route.paramMap.subscribe(p => {
      const id = p.get('id') ?? '';
      if (id !== this.id) { this.id = id; this.load(); }
    });
    this.route.queryParamMap.subscribe(q => {
      const r = Number(q.get('round'));
      this.team.set(q.get('team'));
      this.player.set(q.get('player'));
      if (Number.isInteger(r) && r > 0) this.round.set(r);
      else if (this.status()) this.round.set(this.status()!.currentRound);
      this.connect();
    });
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api.status(this.id).subscribe({
      next: s => {
        this.status.set(s);
        if (this.round() === null) this.round.set(s.currentRound);
        this.loading.set(false);
        this.connect();
      },
      error: () => { this.loading.set(false); this.loadFailed.set(true); },
    });
  }

  /** (Neu) verbinden, wenn sich Runde oder Balken geändert haben. */
  private connect(): void {
    const s = this.status();
    const round = this.round();
    if (!s?.linked || round === null) { this.stopStream(); return; }
    const key = `${round}|${this.evalOn()}`;
    if (key === this.streamKey && this.streamSub) return;
    const sameRound = this.streamKey.startsWith(`${round}|`);
    this.stopStream();
    this.streamKey = key;
    if (!sameRound) { this.layout.set(null); this.games.set({}); }
    this.state.set('connecting');
    this.streamSub = this.api.stream(this.id, round, this.evalOn()).subscribe(e => {
      const now = Date.now();
      switch (e.type) {
        case 'snapshot': {
          const map: Record<string, LiveGame> = {};
          const rec: Record<string, number> = {};
          for (const g of e.games) { map[g.id] = g; rec[g.id] = now; }
          this.games.set(map);
          this.received.set(rec);
          this.layout.set(e.layout);
          this.state.set(e.state);
          break;
        }
        case 'layout': this.layout.set(e.layout); break;
        case 'game':
          this.games.update(m => ({ ...m, [e.game.id]: e.game }));
          this.received.update(m => ({ ...m, [e.game.id]: now }));
          break;
        case 'state': this.state.set(e.state); break;
      }
    });
    this.startTicker();
  }

  private stopStream(): void {
    this.streamSub?.unsubscribe();
    this.streamSub = null;
    this.streamKey = '';
    if (this.ticker) { clearInterval(this.ticker); this.ticker = null; }
  }

  /** Die Uhr der Seite am Zug läuft sekündlich — nicht im E-Ink-Modus (jede Sekunde ein Teilauffrischen, DESIGN 3.6). */
  private startTicker(): void {
    if (this.ticker || this.eink.enabled()) return;
    this.ticker = setInterval(() => this.now.set(Date.now()), 1000);
  }

  setRound(r: number): void { void this.router.navigate([], { relativeTo: this.route, queryParams: { round: r }, queryParamsHandling: 'merge' }); }

  setFilter(name: 'team' | 'player', value: string | null): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { [name]: value }, queryParamsHandling: 'merge' });
  }

  setEval(on: boolean): void {
    this.evalOn.set(on);
    writeLiveEval(on);
    this.connect();
  }

  /** Mannschaftsstand: Lichess-Ergebnisse der übertragenen Bretter; ist die Runde bei chess-results eingetragen, gilt das. */
  scoreOf(m: ShownMatch, home: boolean): string {
    const cr = home ? m.match.homeScore : m.match.awayScore;
    if (cr !== null && cr !== undefined) return scoreText(cr);
    let sum = 0;
    for (const b of m.boards) {
      const [w, bl] = points(b.game.result);
      const homeWhite = m.match.boards.find(x => x.gameId === b.game.id)?.homeIsWhite !== false;
      sum += home === homeWhite ? w : bl;
    }
    return scoreText(sum);
  }

  loginToLink(): void { requireAccount(this.auth, this.router); }

  link(): void {
    if (!requireAccount(this.auth, this.router)) return;
    this.linking.set(true);
    this.api.link(this.id, this.linkUrl().trim()).subscribe({
      next: s => {
        this.linking.set(false);
        this.status.set(s);
        this.round.set(s.currentRound);
        this.snackbar.quick(this.translate.instant('tournaments.live.linked'));
        this.connect();
      },
      error: (err: HttpErrorResponse) => {
        this.linking.set(false);
        this.snackbar.warn(apiErrorText(err, this.translate, 'tournaments.live.linkFailed'));
      },
    });
  }
}

/** „Mittal, Aditya" = „Aditya Mittal": Wörter sortiert, ohne Komma, klein. */
export function samePlayer(a: string, b: string): boolean {
  const key = (s: string) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').split(/[\s,]+/).filter(Boolean).sort().join(' ');
  return key(a) === key(b);
}
