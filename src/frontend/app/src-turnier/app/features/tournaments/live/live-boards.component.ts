import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
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
import { LIVE_LOST_GRACE_MS, joinParam, parseAddParam, parseExtParam, points, readLiveEval, samePlayer, scoreText, writeLiveEval } from './live-boards.util';
import { AddBoardDialogData, AddBoardDialogResult, AddBoardRow, LiveAddBoardDialogComponent } from './live-add-board-dialog.component';

/** Ein Brett, wie es gezeichnet wird. `addKey` = Schlüssel in `?add=`/`?ext=` (nur hinzugefügte Bretter), `caption` = Zusatzzeile. */
interface ShownBoard { key: string; board: number; game: LiveGame; flipped: boolean; addKey?: string; caption?: string | null; }
/** Eine fremde Runde im Strom (1.025.0). */
interface ExtRound { tourName: string; roundName: string; url: string; state: LiveState; games: Record<string, LiveGame>; error: string | null; }
/** Fremde Runde, wie sie gezeichnet wird: Bretter, die schon da sind, und gewählte, die noch fehlen. */
interface ShownExtRound { roundId: string; title: string | null; url: string | null; error: string | null; boards: ShownBoard[]; pending: string[]; }
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
    MatTooltipModule, TranslatePipe, IconLabelDirective, LoadingSpinnerComponent, LoadErrorComponent, LiveBoardCardComponent, LocalScorePipe],
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
        <!-- Teilen (Wunsch 2026-11-26): der Link trägt die ganze Ansicht (Runde, Mannschaft/Spieler, Balken) und läuft ohne Konto -->
        <button mat-stroked-button type="button" class="live-share" (click)="share()"
                [attr.aria-label]="'tournaments.live.share' | translate">
          <mat-icon>share</mat-icon><span class="live-share-label">{{ 'tournaments.live.share' | translate }}</span>
        </button>
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
          <!-- Weitere Bretter (1.025.0): aus dieser Runde (nur mit Mannschafts-/Spielerfilter) oder aus einer anderen Übertragung -->
          <button mat-stroked-button type="button" class="live-add" (click)="openAdd()">
            <mat-icon>add</mat-icon>{{ 'tournaments.live.addBoard' | translate }}
          </button>
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
                <span class="mh-team">{{ m.home }}@if (m.match.homeElo; as e) {
                  <span class="mh-elo" [matTooltip]="'tournaments.live.eloAvgTip' | translate:{ count: m.match.homeEloBoards }">Ø {{ e }}</span>
                }</span>
                <span class="mh-score">
                  @if (m.boards.length > 0 || m.match.homeScore !== null) {
                    {{ scoreOf(m, true) | localScore }} : {{ scoreOf(m, false) | localScore }}
                  } @else { – }
                </span>
                <span class="mh-team away">{{ m.away }}@if (m.match.awayElo; as e) {
                  <span class="mh-elo" [matTooltip]="'tournaments.live.eloAvgTip' | translate:{ count: m.match.awayEloBoards }">Ø {{ e }}</span>
                }</span>
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
        @if (addedBoards().length > 0) {
          <section class="match added">
            <h2 class="match-head"><span class="mh-team">{{ 'tournaments.live.added' | translate }}</span></h2>
            <div class="board-grid">
              @for (b of addedBoards(); track b.key) {
                <app-live-board-card [game]="b.game" [board]="b.board" [flipped]="b.flipped" [showEval]="evalOn()" [caption]="b.caption ?? null"
                                     [removable]="true" (remove)="removeAdded(b.addKey!)"
                                     [receivedAt]="received()[b.game.id] ?? 0" [now]="now()" />
              }
            </div>
          </section>
        }
        @for (x of extRounds(); track x.roundId) {
          <section class="match added">
            <h2 class="match-head ext-head">
              <span class="mh-team">
                @if (x.url) {
                  <a class="ext-link" [href]="x.url" target="_blank" rel="noopener noreferrer">{{ x.title ?? ('tournaments.live.extLoading' | translate) }}<mat-icon class="inline-icon" aria-hidden="true">open_in_new</mat-icon></a>
                } @else { {{ x.title ?? ('tournaments.live.extLoading' | translate) }} }
              </span>
            </h2>
            @if (x.error) {
              <p class="muted ext-line">{{ x.error }}
                <button mat-button type="button" (click)="removeExtRound(x.roundId)">{{ 'tournaments.live.removeBoard' | translate }}</button>
              </p>
            }
            @if (x.boards.length > 0) {
              <div class="board-grid">
                @for (b of x.boards; track b.key) {
                  <app-live-board-card [game]="b.game" [board]="b.board" [showEval]="evalOn()" [removable]="true" (remove)="removeExt(b.addKey!)"
                                       [receivedAt]="received()[b.game.id] ?? 0" [now]="now()" />
                }
              </div>
            }
            @if (!x.error && x.pending.length > 0) {
              <p class="muted ext-line">{{ 'tournaments.live.extPending' | translate:{ count: x.pending.length } }}</p>
            }
          </section>
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
    .live-head-text { min-width: 0; flex: 1 1 auto; }
    .live-share { flex: 0 0 auto; }
    /* Handy: nur das Symbol, damit der Titel Platz behält (Name bleibt per aria-label) */
    @media (max-width: 479px) { .live-share { min-width: 48px; padding: 0 12px; } .live-share .live-share-label { display: none; } .live-share mat-icon { margin: 0; } }
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
    .mh-elo { display: block; font-size: 0.8rem; font-weight: 400; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    .live-add mat-icon { margin-right: 4px; }
    .ext-link { display: inline-flex; align-items: center; gap: 2px; color: var(--rh-accent); overflow-wrap: anywhere; }
    .ext-line { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; margin: 0 0 8px; }
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
  private readonly dialog = inject(MatDialog);

  id = '';
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly status = signal<LiveStatus | null>(null);
  readonly round = signal<number | null>(null);
  readonly team = signal<string | null>(null);
  readonly player = signal<string | null>(null);
  readonly evalOn = signal(readLiveEval());
  /** `?add=` — hinzugefügte Bretter dieser Runde (Schlüssel siehe `parseAddParam`). */
  readonly added = signal<string[]>([]);
  /** `?ext=` — Bretter aus fremden Übertragungen (`roundId.gameId`). */
  readonly ext = signal<string[]>([]);
  /** Stand der fremden Runden aus dem Strom (je Lichess-Runde). */
  readonly extState = signal<Record<string, ExtRound>>({});
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
  /** Zustand, wie er wirklich ist — angezeigt wird {@link state}, bei kurzen Abrissen verzögert. */
  private rawState: LiveState = 'connecting';
  private lostTimer: ReturnType<typeof setTimeout> | null = null;

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

  /** Mit Mannschafts- oder Spielerfilter — nur dann gibt es Bretter dieser Runde, die nicht zu sehen sind. */
  readonly filtered = computed(() => !!this.team() || !!this.player());

  /** Hinzugefügte Bretter DIESER Runde, die nicht ohnehin gezeigt werden (Reihenfolge wie in `?add=`). */
  readonly addedBoards = computed<ShownBoard[]>(() => {
    const l = this.layout();
    const games = this.games();
    if (!l || !this.filtered()) return [];
    const byKey = new Map(this.addRows().map(r => [r.key, r]));
    const out: ShownBoard[] = [];
    for (const key of this.added()) {
      const row = byKey.get(key);
      const slot = row ? this.slotOf(key) : null;
      const gameId = slot?.gameId ?? (row && /^[A-Za-z0-9]{8}$/.test(key) ? key : null);
      const game = gameId ? games[gameId] : undefined;
      if (!row || !game) continue;
      out.push({ key: 'a-' + key + '-' + game.id, board: row.board, game, flipped: slot?.homeIsWhite === false, addKey: key, caption: row.match });
    }
    return out;
  });

  /** Alle Bretter dieser Runde, die gerade NICHT gezeigt werden — die Auswahl im Dialog. */
  readonly addRows = computed<AddBoardRow[]>(() => {
    const l = this.layout();
    const team = this.team();
    const player = this.player();
    if (!l || !this.filtered()) return [];
    const rows: AddBoardRow[] = [];
    if (l.team) {
      for (const m of l.matches) {
        if (team && (m.home === team || m.away === team)) continue;
        for (const b of m.boards) rows.push({ key: `${m.matchNumber}.${b.board}`, board: b.board, white: b.white, black: b.black, match: `${m.home} – ${m.away}` });
      }
    } else {
      for (const b of l.boards) {
        if (player && (samePlayer(b.white, player) || samePlayer(b.black, player))) continue;
        rows.push({ key: String(b.board), board: b.board, white: b.white, black: b.black, match: null });
      }
    }
    const games = this.games();
    l.unassigned.forEach((id, i) => {
      const g = games[id];
      if (g) rows.push({ key: id, board: i + 1, white: g.white.name, black: g.black.name, match: this.translate.instant('tournaments.live.otherGames') });
    });
    return rows;
  });

  /** Fremde Runden in der Reihenfolge von `?ext=`, je Runde die gewählten Bretter (die noch fehlenden als Zahl). */
  readonly extRounds = computed<ShownExtRound[]>(() => {
    const state = this.extState();
    const order: string[] = [];
    const wanted = new Map<string, string[]>();
    for (const k of this.ext()) {
      const [r, g] = k.split('.');
      if (!wanted.has(r)) { wanted.set(r, []); order.push(r); }
      wanted.get(r)!.push(g);
    }
    return order.map(r => {
      const st = state[r];
      const ids = wanted.get(r)!;
      const boards = ids.filter(g => st?.games[g]).map((g, i) => ({
        key: `x-${r}.${g}`, board: i + 1, game: st!.games[g], flipped: false, addKey: `${r}.${g}`,
      }));
      return {
        roundId: r, title: st?.tourName ? `${st.tourName} · ${st.roundName}` : null, url: st?.url ?? null,
        error: st?.error ?? null, boards, pending: ids.filter(g => !st?.games[g]),
      };
    });
  });

  readonly stateIcon = computed(() => ({
    live: 'sensors', connecting: 'sync', reconnecting: 'sync_problem', finished: 'flag', offline: 'cloud_off',
  } as Record<LiveState, string>)[this.state()] ?? 'sync');

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => { this.stopStream(); this.clearLostTimer(); });
    this.route.paramMap.subscribe(p => {
      const id = p.get('id') ?? '';
      if (id !== this.id) { this.id = id; this.load(); }
    });
    this.route.queryParamMap.subscribe(q => {
      const r = Number(q.get('round'));
      this.team.set(q.get('team'));
      this.player.set(q.get('player'));
      this.added.set(parseAddParam(q.get('add')));
      this.ext.set(parseExtParam(q.get('ext')));
      // Engine-Balken aus der Adresse (geteilter Link) geht der Gerätevorliebe vor, ohne sie zu überschreiben.
      const ev = q.get('eval');
      if (ev === '1' || ev === '0') this.evalOn.set(ev === '1');
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
    const key = `${round}|${this.evalOn()}|${this.ext().join(',')}`;
    if (key === this.streamKey && this.streamSub) return;
    const sameRound = this.streamKey.startsWith(`${round}|`);
    this.stopStream();
    this.streamKey = key;
    if (!sameRound) { this.layout.set(null); this.games.set({}); }
    this.setState('connecting');
    this.streamSub = this.api.stream(this.id, round, this.evalOn(), this.ext()).subscribe(e => {
      const now = Date.now();
      switch (e.type) {
        case 'snapshot': {
          const map: Record<string, LiveGame> = {};
          const rec: Record<string, number> = {};
          for (const g of e.games) { map[g.id] = g; rec[g.id] = now; }
          this.games.set(map);
          this.received.set(rec);
          this.layout.set(e.layout);
          this.setState(e.state);
          break;
        }
        case 'layout': this.layout.set(e.layout); break;
        case 'game':
          this.games.update(m => ({ ...m, [e.game.id]: e.game }));
          this.received.update(m => ({ ...m, [e.game.id]: now }));
          break;
        case 'state': this.setState(e.state); break;
        case 'ext-snapshot': {
          const map: Record<string, LiveGame> = {};
          for (const g of e.games) map[g.id] = g;
          this.extState.update(m => ({ ...m, [e.round]: { tourName: e.tourName, roundName: e.roundName, url: e.url, state: e.state, games: map, error: null } }));
          this.received.update(m => ({ ...m, ...Object.fromEntries(e.games.map(g => [g.id, now])) }));
          break;
        }
        case 'ext-game':
          this.extState.update(m => m[e.round] ? { ...m, [e.round]: { ...m[e.round], games: { ...m[e.round].games, [e.game.id]: e.game } } } : m);
          this.received.update(m => ({ ...m, [e.game.id]: now }));
          break;
        case 'ext-state':
          this.extState.update(m => m[e.round] ? { ...m, [e.round]: { ...m[e.round], state: e.state } } : m);
          break;
        case 'ext-error': {
          const text = this.translate.instant(`tournaments.live.extError.${e.code === 'live_ext_limit' ? 'limit' : 'notFound'}`);
          const empty: ExtRound = { tourName: '', roundName: '', url: '', state: 'offline', games: {}, error: null };
          this.extState.update(m => ({ ...m, [e.round]: { ...(m[e.round] ?? empty), error: text } }));
          break;
        }
      }
    });
    this.startTicker();
  }

  /**
   * Zustand übernehmen. Ein Abriss (`connecting`/`reconnecting`/`offline`), während schon Bretter da sind, wird erst nach
   * {@link LIVE_LOST_GRACE_MS} angezeigt — bis dahin bleibt der alte Zustand („Live“) stehen; kommt die Verbindung
   * vorher zurück, merkt man nichts.
   */
  private setState(s: LiveState): void {
    this.rawState = s;
    const interrupted = s === 'connecting' || s === 'reconnecting' || s === 'offline';
    if (!interrupted || !this.layout()) {
      if (this.lostTimer) { clearTimeout(this.lostTimer); this.lostTimer = null; }
      this.state.set(s);
      return;
    }
    this.lostTimer ??= setTimeout(() => { this.lostTimer = null; this.state.set(this.rawState); }, LIVE_LOST_GRACE_MS);
  }

  private stopStream(): void {
    this.streamSub?.unsubscribe();
    this.streamSub = null;
    this.streamKey = '';
    if (this.ticker) { clearInterval(this.ticker); this.ticker = null; }
  }

  private clearLostTimer(): void {
    if (this.lostTimer) { clearTimeout(this.lostTimer); this.lostTimer = null; }
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

  /** Der Dialog „Brett hinzufügen“ (DESIGN 5.6: Auswahl-Dialog 560 px); „Fertig“ schreibt `add`/`ext` in die Adresse. */
  openAdd(): void {
    const data: AddBoardDialogData = { rows: this.addRows(), added: this.added(), ext: this.ext() };
    this.dialog.open<LiveAddBoardDialogComponent, AddBoardDialogData, AddBoardDialogResult>(LiveAddBoardDialogComponent,
      { data, width: '560px', maxWidth: '94vw', autoFocus: 'dialog' })
      .afterClosed().subscribe(r => { if (r) this.setAdded(r.added, r.ext); });
  }

  /** Auswahl in die Adresse — der geteilte Link zeigt genau diese Bretter, auch für Gäste. */
  setAdded(added: string[], ext: string[]): void {
    void this.router.navigate([], { relativeTo: this.route, queryParamsHandling: 'merge',
      queryParams: { add: joinParam(parseAddParam(added.join(','))), ext: joinParam(parseExtParam(ext.join(','))) } });
  }

  removeAdded(key: string): void { this.setAdded(this.added().filter(k => k !== key), this.ext()); }
  removeExt(key: string): void { this.setAdded(this.added(), this.ext().filter(k => k !== key)); }
  removeExtRound(roundId: string): void { this.setAdded(this.added(), this.ext().filter(k => !k.startsWith(roundId + '.'))); }

  /** Brett der eigenen Runde zu einem `?add=`-Schlüssel (Mannschaft `m.b`, Einzel `b`). */
  private slotOf(key: string): LiveBoardSlot | null {
    const l = this.layout();
    if (!l) return null;
    const [a, b] = key.split('.');
    if (l.team && b !== undefined) return l.matches.find(m => m.matchNumber === +a)?.boards.find(x => x.board === +b) ?? null;
    if (!l.team && b === undefined && /^\d+$/.test(a)) return l.boards.find(x => x.board === +a) ?? null;
    return null;
  }

  /** Balken an/aus: Gerätevorliebe UND Adresse (`?eval=1|0`), damit ein kopierter Link dieselbe Ansicht zeigt. */
  setEval(on: boolean): void {
    this.evalOn.set(on);
    writeLiveEval(on);
    this.connect();
    void this.router.navigate([], { relativeTo: this.route, queryParams: { eval: on ? 1 : 0 }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  /** Link dieser Ansicht: Runde immer ausdrücklich (sonst zeigte er morgen die nächste), Mannschaft/Spieler, Balken nur wenn an. */
  shareUrl(): string {
    const tree = this.router.createUrlTree(['/tournaments', this.id, 'live'], {
      queryParams: { round: this.round(), team: this.team(), player: this.player(), add: joinParam(this.added()), ext: joinParam(this.ext()),
        eval: this.evalOn() ? 1 : null },
    });
    return window.location.origin + this.router.serializeUrl(tree);
  }

  /** Am Handy das Teilen-Blatt, sonst Zwischenablage mit Snackbar (DESIGN 5.x „Link teilen“, wie die Partie-Links). */
  share(): void {
    const url = this.shareUrl();
    const copy = () => void (navigator.clipboard?.writeText(url) ?? Promise.reject(new Error('no clipboard'))).then(
      () => this.snackbar.copy(this.translate.instant('tournaments.live.shareCopied')),
      () => this.snackbar.warn(url),
    );
    if (typeof navigator.share !== 'function' || !window.matchMedia?.('(pointer: coarse)').matches) { copy(); return; }
    navigator.share({ url }).catch((e: unknown) => { if ((e as { name?: string } | null)?.name !== 'AbortError') copy(); });
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

/** Weiter hier erreichbar (Specs, Aufrufer); die Regel steht in `live-boards.util.ts`. */
export { samePlayer } from './live-boards.util';
