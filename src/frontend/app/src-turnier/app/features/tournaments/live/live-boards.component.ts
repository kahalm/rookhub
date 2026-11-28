import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggle, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { EinkService } from '@rh/core/eink.service';
import { apiErrorText } from '@rh/core/api-error';
import { SnackbarService } from '@rh/core/snackbar.service';
import { partnerSiteUrl } from '@rh/core/partner-site';
import { IconLabelDirective } from '@rh/shared/icon-label/icon-label.directive';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { LoadErrorComponent } from '@rh/shared/load-error/load-error.component';
import { requireAccount } from '../../../core/require-account';
import { LocalScorePipe } from '../tournament-table.util';
import { LiveBoardCardComponent } from './live-board-card.component';
import { LiveBoardSlot, LiveGame, LiveMatchSlot, LiveRoundLayout, LiveState, LiveStatus, LiveStreamEvent, LiveViewState } from './live-boards.models';
import { LiveBoardsService } from './live-boards.service';
import { broadcastHost, broadcastRoundUrl, isLiveGameIdFor, isLiveRoundId, LIVE_LOST_GRACE_MS, LIVE_VIEW_SAVE_MS, joinParam, parseAddParam, parseExtParam, points, readLiveBest, readTurned, readLiveEval, readViewKey, samePlayer, scoreText, writeLiveEval,
  writeLiveBest, writeTurned, writeViewKey } from './live-boards.util';
import { LiveAlertEvent, LiveAlertTracker, VIBRATE_CONFIRM, VIBRATE_RESULT, VIBRATE_SWING, canVibrate, readLiveVibrate, vibrate,
  writeLiveVibrate } from './live-vibrate';
import { LIVE_FLASH_MS, TabAttention, notifySupported, pageAttended, pawnsText, readLiveFlash, readLiveNotify, requestNotifyPermission,
  resultShort, showLiveNotification, writeLiveFlash, writeLiveNotify } from './live-attention';
import { AddBoardDialogData, AddBoardDialogResult, AddBoardRow, LiveAddBoardDialogComponent } from './live-add-board-dialog.component';

/** Ein Brett, wie es gezeichnet wird. `addKey` = Schlüssel in `?add=`/`?ext=` (nur hinzugefügte Bretter), `caption` = Zusatzzeile. */
interface ShownBoard {
  key: string; board: number; game: LiveGame; flipped: boolean; addKey?: string; caption?: string | null;
  /** Übersetzungsschlüssel der Zusatzzeile, wenn es keinen eigenen Text gibt. */
  captionKey?: string;
  /** Lichess-Runde (für „Analysieren“). */
  roundId?: string | null;
  /** ✕ an der Karte: aus `?add=` bzw. `?ext=` nehmen. */
  removeKind?: 'add' | 'ext';
}
/** Eine fremde Runde im Strom (1.025.0). */
interface ExtRound { tourName: string; roundName: string; url: string; state: LiveState; games: Record<string, LiveGame>; error: string | null; }
/** Fremde Runde, wie sie gezeichnet wird: Bretter, die schon da sind, und gewählte, die noch fehlen. */
interface ShownExtRound { roundId: string; title: string | null; url: string | null; error: string | null; boards: ShownBoard[]; pending: string[]; }
/** Eine Begegnung mit Live-Stand. */
interface ShownMatch { match: LiveMatchSlot; home: string; away: string; boards: ShownBoard[]; }

/**
 * Live-Bretter `/tournaments/:id/live` (1.014.0): die Partien einer Runde aus der verknüpften Lichess-Übertragung,
 * live über Server-Sent Events, alle Bretter in EINEM Raster (seit 1.028.0). Mannschaftsturnier: je Begegnung die Bretter (mit `?team=` nur die Begegnung dieser
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
        </div>
        <!-- Teilen (Wunsch 2026-11-26): der Link trägt die ganze Ansicht (Runde, Mannschaft/Spieler, Balken) und läuft ohne Konto -->
        <button mat-stroked-button type="button" class="live-share" (click)="share()"
                [attr.aria-label]="'tournaments.live.share' | translate">
          <mat-icon>share</mat-icon><span class="live-share-label">{{ 'tournaments.live.share' | translate }}</span>
        </button>
      </div>

      @if (viewMissing()) {
        <section class="live-hint" role="note">
          <mat-icon aria-hidden="true">link_off</mat-icon>
          <div class="live-hint-body"><p>{{ 'tournaments.live.viewNotFound' | translate }}</p></div>
        </section>
      } @else if (loading()) {
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
                         placeholder="lichess.org/broadcast/… · chess.com/events/…" autocomplete="off" />
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
          <!-- Besten Zug zeigen (1.030.0): je Gerät, nicht in Adresse/geteilter Ansicht; braucht den Balken (die Bewertung liefert den Zug) -->
          <mat-slide-toggle class="live-best" [checked]="bestOn()" [disabled]="!evalOn()" (change)="setBest($event.checked)">{{ 'tournaments.live.bestMove' | translate }}</mat-slide-toggle>
          <!-- Vibration (1.035.0): je Gerät; nur wo der Browser vibrieren kann (iOS Safari nicht) -->
          @if (vibrateSupported) {
            <mat-slide-toggle class="live-vibrate" [checked]="vibrateOn()" (change)="setVibrate($event.checked)"
                              [matTooltip]="'tournaments.live.vibrateTip' | translate">{{ 'tournaments.live.vibrate' | translate }}</mat-slide-toggle>
          }
          <!-- Aufblitzen + Benachrichtigung (1.042.0): je Gerät, dieselben Ereignisse wie das Vibrieren; Benachrichtigung nur mit Notification API -->
          <mat-slide-toggle class="live-flash" [checked]="flashOn()" (change)="setFlash($event.checked)"
                            [matTooltip]="'tournaments.live.flashTip' | translate">{{ 'tournaments.live.flash' | translate }}</mat-slide-toggle>
          @if (notifyAvailable) {
            <mat-slide-toggle class="live-notify" [checked]="notifyOn()" (change)="setNotify($event.checked, $event.source)"
                              [matTooltip]="'tournaments.live.notifyTip' | translate">{{ 'tournaments.live.notify' | translate }}</mat-slide-toggle>
          }
          <span class="live-state" [class.is-live]="state() === 'live'" role="status">
            <mat-icon aria-hidden="true">{{ stateIcon() }}</mat-icon>{{ ('tournaments.live.state.' + state()) | translate }}
          </span>
        </div>
        @if (viewToken()) {
          <!-- Geteilte Ansicht (1.029.0): Besitzer weiß, dass alle mitsehen; Zuschauer, dass eine Änderung seine eigene Ansicht wird -->
          <p class="view-note" role="note">
            <mat-icon aria-hidden="true">{{ canEdit() ? 'group' : 'visibility' }}</mat-icon>
            {{ (canEdit() ? 'tournaments.live.sharedOwner' : 'tournaments.live.sharedViewer') | translate }}
          </p>
        }
        @if (player(); as p) {
          <p class="filter-line">
            {{ 'tournaments.live.onlyPlayer' | translate:{ name: p } }}
            <button mat-button type="button" (click)="setFilter('player', null)">{{ 'tournaments.live.showAll' | translate }}</button>
          </p>
        }

        @if (!layout()) {
          <app-loading-spinner />
        } @else {
          <!-- Übertragungen gesammelt (Wunsch 2026-11-26 „sammel alle Links … und schreib die untereinander“): je Zeile eine
               Quelle — das eigene Turnier, je Begegnung Stand + Ø-Elo (Link zur Lichess-Runde), je fremde Runde „Turnier · Runde“.
               Kleine Schrift, Umbruch statt „…“; die Herkunft steht zusätzlich an jeder Karte. -->
          <ul class="sources" [attr.aria-label]="'tournaments.live.sources' | translate">
            <li class="sources-label" aria-hidden="true">{{ 'tournaments.live.sources' | translate }}</li>
            @if (status()?.tourName; as name) {
              <li><a class="live-source" [href]="status()!.url" target="_blank" rel="noopener noreferrer">{{ name }} · {{ sourceHost() }}<mat-icon class="inline-icon" aria-hidden="true">open_in_new</mat-icon></a></li>
            }
            @for (m of matches(); track m.match.matchNumber) {
              <li>
                <a class="match-head" [attr.href]="ownRoundUrl()" target="_blank" rel="noopener noreferrer">
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
                </a>
              </li>
            }
            @for (x of extRounds(); track x.roundId) {
              <li class="ext-line">
                <a class="ext-link" [href]="x.url || roundUrl(x.roundId)" target="_blank" rel="noopener noreferrer">{{ x.title ?? ('tournaments.live.extLoading' | translate) }} · {{ roundHost(x.roundId) }}<mat-icon class="inline-icon" aria-hidden="true">open_in_new</mat-icon></a>
                @if (x.error) {
                  <span class="muted">{{ x.error }}</span>
                  <button mat-button type="button" (click)="removeExtRound(x.roundId)">{{ 'tournaments.live.removeBoard' | translate }}</button>
                } @else if (x.pending.length > 0) {
                  <span class="muted">{{ 'tournaments.live.extPending' | translate:{ count: x.pending.length } }}</span>
                }
              </li>
            }
          </ul>
          @if (layout()!.team && matches().length === 1 && matches()[0].boards.length === 0) {
            <p class="muted">{{ 'tournaments.live.noBoards' | translate }}</p>
          } @else if (ownCount() === 0) {
            <p class="muted">{{ 'tournaments.live.noGames' | translate }}</p>
          }
          @if (!layout()!.team && missing() > 0 && !player()) {
            <p class="muted">{{ 'tournaments.live.notBroadcast' | translate:{ count: missing() } }}</p>
          }
          @if (allBoards().length > 0) {
            <!-- EIN Raster für alle Bretter: Mannschaft/Runde, hinzugefügte, fremde, weitere — keine Abschnittsgrenzen -->
            <div class="board-grid">
              @for (b of allBoards(); track b.key) {
                <app-live-board-card [game]="b.game" [board]="b.board" [flipped]="b.flipped !== turnedIds().has(b.game.id)"
                                     [turned]="turnedIds().has(b.game.id)" (turn)="toggleTurn(b.game.id)" [showEval]="evalOn()" [showBest]="bestOn()"
                                     [analysisUrl]="analysisUrl(b.roundId ?? null, b.game.id)" [roundId]="b.roundId ?? null"
                                     [caption]="b.caption ?? (b.captionKey ? (b.captionKey | translate) : null)"
                                     [removable]="!!b.removeKind" (remove)="removeBoard(b)"
                                     [receivedAt]="received()[b.game.id] ?? 0" [now]="now()"
                                     [flash]="flashes()[b.game.id] ?? null" [attr.data-game]="b.game.id" />
              }
            </div>
          }
        }
      }
    </div>
  `,
  styles: [`
    /* Bretterwand (Wunsch 2026-11-26): breiter als die Lese-Seiten — bei 1920 px sieben Karten nebeneinander, acht
       Bretter samt zweiter Reihe ohne Scrollen; ab 2400 px nicht noch breiter (das Raster nimmt dann mehr Spalten). */
    .live-page { max-width: 1880px; margin: 0 auto; padding: 8px 16px 24px; box-sizing: border-box; }
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
    .view-note { display: flex; align-items: center; gap: 6px; margin: 0 0 8px; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .view-note mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .filter-line { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; margin: 0 0 8px; }
    /* Kompakte Kopfzeilen über dem Raster: je Begegnung eine Zeile „Heim Ø  2,5 : 3,5  Gast Ø“, umbrechend nebeneinander */
    /* Übertragungen: eine Quelle je Zeile, 0,85 rem, Umbruch statt „…“ (auch am Handy) */
    .sources { list-style: none; margin: 0 0 8px; padding: 0; display: flex; flex-direction: column; gap: 2px; font-size: 0.85rem;
      overflow-wrap: anywhere; }
    .sources-label { font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); }
    .sources .live-source { font-size: inherit; }
    .match-head { display: inline-flex; flex-wrap: wrap; align-items: baseline; gap: 2px 10px; min-width: 0; font-weight: 500;
      color: inherit; text-decoration: none; }
    a.match-head[href]:hover .mh-team { text-decoration: underline; }
    .mh-team { min-width: 0; overflow-wrap: anywhere; }
    .mh-score { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
    /* Karten ≥ 240 px (Brett ≈ 216 px): 1920 px → 7 Spalten, 1280 px → 4, unter ~520 px eine; min(100%, …) verhindert
       Seiten-Scroll auf ganz schmalen Geräten. */
    .board-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 240px), 1fr)); gap: 12px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .mh-elo { margin-left: 0.4em; font-size: 0.8rem; font-weight: 400; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    .live-add mat-icon { margin-right: 4px; }
    .ext-link { display: inline-flex; align-items: center; gap: 2px; color: var(--rh-accent); overflow-wrap: anywhere; }
    .ext-line { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; margin: 0; }
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  id = '';
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly status = signal<LiveStatus | null>(null);
  readonly round = signal<number | null>(null);
  readonly team = signal<string | null>(null);
  readonly player = signal<string | null>(null);
  /** Engine-Balken — Gerätevorliebe (Standard an), jeder Zuschauer selbst; auch in der geteilten Ansicht NICHT geteilt (1.039.0). */
  readonly evalOn = signal(readLiveEval());
  /** „Besten Zug zeigen“ — Gerätevorliebe, jeder Zuschauer selbst (1.030.0). */
  readonly bestOn = signal(readLiveBest());
  /** Vibration bei Ergebnis und Bewertungssprung (1.035.0) — Gerätevorliebe; der Schalter fehlt, wo `navigator.vibrate` fehlt. */
  readonly vibrateSupported = canVibrate();
  readonly vibrateOn = signal(this.vibrateSupported && readLiveVibrate());
  /** Erkennung je Partie; Snapshots setzen nur den Anfangsstand (kein Vibrieren beim Laden/Wiederverbinden). */
  private readonly alerts = new LiveAlertTracker();
  private lastAlert = 0;
  /** Aufblitzen (1.042.0): Karte blinkt bzw. — Tab im Hintergrund — Titel und Favicon wechseln. Gerätevorliebe, Standard aus. */
  readonly flashOn = signal(readLiveFlash());
  /** Benachrichtigung (1.042.0): nur mit Notification API und Erlaubnis; die Vorliebe allein schaltet sie nicht ein. */
  readonly notifyAvailable = notifySupported();
  readonly notifyOn = signal(this.notifyAvailable && readLiveNotify() && Notification.permission === 'granted');
  /** Bretter, die gerade aufblitzen (Lichess-Kennung → Art). */
  readonly flashes = signal<Partial<Record<string, 'result' | 'swing'>>>({});
  private readonly flashTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly tab = new TabAttention(document);
  /** Von Hand gedrehte Bretter (Lichess-Kennung der Partie) — nur dieser Zuschauer, diese Sitzung; nicht in Adresse/geteilter Ansicht. */
  readonly turnedIds = signal<ReadonlySet<string>>(readTurned());
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
  /** Geteilte Ansicht (`/live/:token`, 1.029.0): Kennung, darf dieser Browser/dieses Konto ändern, gibt es sie (noch)? */
  readonly viewToken = signal<string | null>(null);
  readonly canEdit = signal(false);
  readonly viewMissing = signal(false);
  /** Steht `?eval=1|0` ausdrücklich in der langen Adresse? */
  private urlEval = false;
  /** Besitzer: Änderung wartet aufs Speichern — `view`-Ereignisse überschreiben sie bis dahin nicht. */
  private savePending = false;
  private saveTimer: ReturnType<typeof setTimeout> | null = null;
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

  /** Lichess-Seite der gewählten Runde (Begegnungszeilen der Übertragungsliste); ohne Runde kein Link. */
  readonly ownRoundUrl = computed(() => this.ownRoundId() ? broadcastRoundUrl(this.ownRoundId()!) : null);
  /** Quelle der eigenen Übertragung für die Liste „Übertragungen“ (lichess.org / chess.com, 1.040.0). */
  readonly sourceHost = computed(() => broadcastHost(this.status()?.url));
  /** Link einer fremden Runde, solange der Strom noch keinen gemeldet hat. */
  roundUrl(roundId: string): string { return broadcastRoundUrl(roundId); }
  /** Herkunft einer fremden Runde (lichess.org / chess.com). */
  roundHost(roundId: string): string { return broadcastHost(broadcastRoundUrl(roundId)); }

  /** Lichess-Runde der gewählten chess-results-Runde (für „Analysieren“). */
  readonly ownRoundId = computed(() => this.status()?.rounds.find(r => r.round === this.round())?.lichessRoundId ?? null);
  /** RookHub zu dieser Turnierseite (Prod ↔ Prod, Dev ↔ Dev); ohne Partner (localhost) kein Analyse-Knopf. */
  private readonly rookhub = partnerSiteUrl();

  /** Link aufs Analysebrett: `/analysis?live=roundId.gameId` — RookHub holt das PGN (alle Züge, Kopf, Uhren) vom Server, auch für Gäste. */
  analysisUrl(roundId: string | null, gameId: string): string | null {
    if (!this.rookhub || !roundId || !isLiveRoundId(roundId) || !isLiveGameIdFor(roundId, gameId)) return null;
    return `${this.rookhub}/analysis?live=${roundId}.${gameId}`;
  }

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

  /** Bretter der eigenen Auswahl (Begegnung(en) bzw. Runde) — ohne hinzugefügte und fremde. */
  readonly ownCount = computed(() => this.layout()?.team ? this.matches().reduce((n, m) => n + m.boards.length, 0) : this.singleBoards().length);

  /**
   * ALLE gezeigten Bretter in EINER Folge für das eine Raster (Wunsch 2026-11-26): Begegnung(en) bzw. Runde, dann die
   * hinzugefügten, die fremden und die übrigen übertragenen Partien. Woher ein Brett kommt, sagt seine Zusatzzeile —
   * bei mehreren Begegnungen „Heim – Gast“, hinzugefügte ihre Begegnung, fremde „Turnier · Runde“.
   */
  readonly allBoards = computed<ShownBoard[]>(() => {
    const own = this.ownRoundId();
    const matches = this.matches();
    const many = matches.length > 1;
    const out: ShownBoard[] = [];
    for (const m of matches) {
      for (const b of m.boards) out.push({ ...b, roundId: own, caption: many ? `${m.home} – ${m.away}` : null });
    }
    for (const b of this.singleBoards()) out.push({ ...b, roundId: own });
    for (const b of this.addedBoards()) out.push({ ...b, roundId: own, removeKind: 'add', captionKey: b.caption ? undefined : 'tournaments.live.added' });
    for (const x of this.extRounds()) {
      for (const b of x.boards) out.push({ ...b, roundId: x.roundId, removeKind: 'ext', caption: x.title, captionKey: 'tournaments.live.extLoading' });
    }
    if (!this.team() && !this.player()) {
      for (const b of this.others()) out.push({ ...b, roundId: own, captionKey: 'tournaments.live.otherGames' });
    }
    return out;
  });

  readonly stateIcon = computed(() => ({
    live: 'sensors', connecting: 'sync', reconnecting: 'sync_problem', finished: 'flag', offline: 'cloud_off',
  } as Record<LiveState, string>)[this.state()] ?? 'sync');

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => {
      this.stopStream(); this.clearLostTimer(); if (this.saveTimer) clearTimeout(this.saveTimer);
      // Titel und Favicon zurück, offene Blitze weg (Verlassen der Seite, 1.042.0)
      this.tab.stop();
      for (const t of this.flashTimers.values()) clearTimeout(t);
      this.flashTimers.clear();
    });
    this.route.paramMap.subscribe(p => {
      // Kurzer, lebender Link /live/:token (1.029.0): die Ansicht kommt vom Server, nicht aus der Adresse.
      const token = p.get('token');
      if (token) { if (token !== this.viewToken()) this.loadView(token); return; }
      const id = p.get('id') ?? '';
      if (id !== this.id) { this.id = id; this.load(); }
    });
    this.route.queryParamMap.subscribe(q => {
      if (this.viewToken()) return;
      const r = Number(q.get('round'));
      this.team.set(q.get('team'));
      this.player.set(q.get('player'));
      this.added.set(parseAddParam(q.get('add')));
      this.ext.set(parseExtParam(q.get('ext')));
      // Engine-Balken: ausdrücklich in der langen Adresse (`?eval=1|0`) geht der Gerätevorliebe vor, ohne sie zu überschreiben;
      // ohne `eval` gilt die Vorliebe (Standard an).
      const ev = q.get('eval');
      this.urlEval = ev === '1' || ev === '0';
      if (this.urlEval) this.evalOn.set(ev === '1');
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

  /** Geteilte Ansicht laden: Kennung, Bearbeitungsrecht (Schlüssel dieses Browsers bzw. Konto), dann das Turnier. */
  private loadView(token: string): void {
    this.viewToken.set(token);
    this.loading.set(true);
    this.loadFailed.set(false);
    this.viewMissing.set(false);
    this.api.getView(token, readViewKey(token)).subscribe({
      next: r => {
        this.canEdit.set(r.canEdit);
        this.id = r.tournamentId;
        this.applyView(r.view);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        if (err.status === 404) this.viewMissing.set(true); else this.loadFailed.set(true);
      },
    });
  }

  /** Die Ansicht, wie sie gerade eingestellt ist (für Anlegen/Ändern der geteilten Ansicht). */
  currentView(): LiveViewState {
    return { round: this.round(), team: this.team(), player: this.player(), add: this.added(), ext: this.ext() };
  }

  /** Stand einer geteilten Ansicht übernehmen (vom Server oder als eigene Änderung des Besitzers). Neue Runde → Bretter leeren. */
  private applyView(v: LiveViewState): void {
    const round = v.round ?? this.status()?.currentRound ?? null;
    if (round !== this.round()) { this.layout.set(null); this.games.set({}); }
    this.round.set(round);
    this.team.set(v.team);
    this.player.set(v.player);
    this.added.set(parseAddParam(v.add.join(',')));
    this.ext.set(parseExtParam(v.ext.join(',')));
    // Balken/bester Zug gehören nicht zur Ansicht (1.039.0): jeder Zuschauer behält seine eigene Wahl.
  }

  /**
   * Änderung an einer geteilten Ansicht. Besitzer: sofort hier zeigen und entprellt speichern — alle Zuschauer folgen über
   * ihren Strom. Zuschauer: die Änderung macht daraus seine EIGENE Ansicht (lange Adresse), die geteilte bleibt, wie sie ist.
   */
  private changeView(patch: Partial<LiveViewState>): void {
    const next = { ...this.currentView(), ...patch };
    if (!this.canEdit()) {
      const tree = this.router.createUrlTree(['/tournaments', this.id, 'live'], { queryParams: this.longParams(next) });
      void this.router.navigateByUrl(tree);
      return;
    }
    this.applyView(next);
    this.savePending = true;
    if (this.saveTimer) clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => {
      this.saveTimer = null;
      const token = this.viewToken()!;
      this.api.updateView(token, this.currentView(), readViewKey(token)).subscribe({
        next: () => { if (!this.saveTimer) this.savePending = false; },
        error: () => {
          if (!this.saveTimer) this.savePending = false;
          this.snackbar.warn(this.translate.instant('tournaments.live.viewSaveFailed'));
        },
      });
    }, LIVE_VIEW_SAVE_MS);
  }

  /** (Neu) verbinden, wenn sich Runde oder Balken geändert haben. */
  private connect(): void {
    const s = this.status();
    if (this.viewToken()) { this.connectView(); return; }
    const round = this.round();
    if (!s?.linked || round === null) { this.stopStream(); return; }
    // Filter der Ansicht gehört zum Strom (der Server bewertet nur Sichtbares, 1.027.0) — ein Wechsel verbindet neu, still.
    const view = { team: this.team(), player: this.player(), add: this.filtered() ? this.added() : [] };
    const key = `${round}|${this.evalOn()}|${this.ext().join(',')}|${view.team ?? ''}|${view.player ?? ''}|${view.add.join(',')}`;
    if (key === this.streamKey && this.streamSub) return;
    const sameRound = this.streamKey.startsWith(`${round}|`);
    this.stopStream();
    this.streamKey = key;
    if (!sameRound) { this.layout.set(null); this.games.set({}); }
    this.setState('connecting');
    this.streamSub = this.api.stream(this.id, round, this.evalOn(), this.ext(), view).subscribe(e => this.onEvent(e));
    this.startTicker();
  }

  /** Strom einer geteilten Ansicht: EINE Verbindung, der Server hängt bei Änderungen selbst um. Neu nur beim eigenen Balken-Schalter. */
  private connectView(): void {
    if (!this.status()?.linked) { this.stopStream(); return; }
    const key = `view|${this.viewToken()}|${this.evalOn()}`;
    if (key === this.streamKey && this.streamSub) return;
    this.stopStream();
    this.streamKey = key;
    this.setState('connecting');
    this.streamSub = this.api.viewStream(this.viewToken()!, this.evalOn()).subscribe(e => this.onEvent(e));
    this.startTicker();
  }

  private onEvent(e: LiveStreamEvent): void {
    const now = Date.now();
    switch (e.type) {
      case 'view':
        // Eigene, noch nicht gespeicherte Änderung des Besitzers geht vor (sonst sprängen die Bretter kurz zurück).
        if (!this.savePending) this.applyView(e.view);
        break;
      case 'view-gone':
        this.viewMissing.set(true);
        this.stopStream();
        break;
      case 'snapshot': {
        const map: Record<string, LiveGame> = {};
        const rec: Record<string, number> = {};
        for (const g of e.games) { map[g.id] = g; rec[g.id] = now; this.alerts.seed(g); }
        this.games.set(map);
        this.received.set(rec);
        this.layout.set(e.layout);
        this.setState(e.state);
        break;
      }
      case 'layout': this.layout.set(e.layout); break;
      case 'game':
        this.alert(e.game);
        this.games.update(m => ({ ...m, [e.game.id]: e.game }));
        this.received.update(m => ({ ...m, [e.game.id]: now }));
        break;
      case 'state': this.setState(e.state); break;
      case 'ext-snapshot': {
        const map: Record<string, LiveGame> = {};
        for (const g of e.games) { map[g.id] = g; this.alerts.seed(g); }
        this.extState.update(m => ({ ...m, [e.round]: { tourName: e.tourName, roundName: e.roundName, url: e.url, state: e.state, games: map, error: null } }));
        this.received.update(m => ({ ...m, ...Object.fromEntries(e.games.map(g => [g.id, now])) }));
        break;
      }
      case 'ext-game':
        this.alert(e.game);
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
  }

  /**
   * Live-Änderung einer Partie prüfen (Zustand läuft immer mit, damit ein späteres Einschalten nicht vom alten Stand aus
   * meldet). Meldet nur, wenn eine Meldeart an ist und das Brett gezeigt wird; ein Sprung braucht den Balken (sonst keine
   * Bewertung im Strom). Mehrere Meldungen kurz hintereinander: ein Ergebnis geht immer, ein Sprung erst nach 2 s Pause.
   * EINE Erkennung, drei Ausgaben (1.042.0): Vibrieren, Aufblitzen (Karte bzw. Tab-Titel im Hintergrund), Benachrichtigung
   * (nur wenn die Seite nicht im Blick ist).
   */
  private alert(game: LiveGame): void {
    const ev = this.alerts.observeDetail(game);
    if (!ev || (ev.kind === 'swing' && !this.evalOn())) return;
    // Benachrichtigung nur, wenn die Seite nicht im Blick ist; geht gar nichts hinaus, zählt es auch nicht für die Drosselung.
    const toNotify = this.notifyOn() && !pageAttended(document);
    if (!this.vibrateOn() && !this.flashOn() && !toNotify) return;
    const shown = this.allBoards().find(b => b.game.id === game.id);
    if (!shown) return;
    const now = Date.now();
    if (ev.kind === 'swing' && now - this.lastAlert < 2000) return;
    this.lastAlert = now;
    if (this.vibrateOn()) vibrate(ev.kind === 'result' ? VIBRATE_RESULT : VIBRATE_SWING);
    const board = this.translate.instant('tournaments.live.boardN', { board: shown.board });
    if (this.flashOn()) {
      if (document.hidden) this.tab.start(ev.kind === 'result' ? `${resultShort(game.result)} ${board}` : `${board}: ${pawnsText(ev.to ?? 0)}`);
      else this.flashBoard(game.id, ev.kind);
    }
    if (toNotify) this.notify(game, board, ev);
  }

  /**
   * Benachrichtigung je Brett (`tag` ersetzt die vorige desselben Bretts): „Brett 3“ / „Weiß – Schwarz 1-0“ bzw.
   * „Brett 3: +1.4 → −0.3“ / „Bewertung +1.4 → −0.3 · Weiß – Schwarz“. Klick holt das Fenster nach vorn und zeigt das Brett.
   */
  private notify(game: LiveGame, board: string, ev: LiveAlertEvent): void {
    const names = `${game.white.name} – ${game.black.name}`;
    let title = board;
    let body = `${names} ${resultShort(game.result)}`;
    if (ev.kind === 'swing') {
      const from = pawnsText(ev.from ?? 0);
      const to = pawnsText(ev.to ?? 0);
      title = `${board}: ${from} → ${to}`;
      body = `${this.translate.instant('tournaments.live.evalSwing', { from, to })} · ${names}`;
    }
    showLiveNotification(title, body, `rh-live-${game.id}`, () => this.showBoard(game.id, ev.kind));
  }

  /** Karte kurz aufblitzen lassen (Klasse an der Karte für {@link LIVE_FLASH_MS}); eine neue Meldung verlängert. */
  private flashBoard(gameId: string, kind: 'result' | 'swing'): void {
    const old = this.flashTimers.get(gameId);
    if (old) clearTimeout(old);
    this.flashes.update(m => ({ ...m, [gameId]: kind }));
    this.flashTimers.set(gameId, setTimeout(() => {
      this.flashTimers.delete(gameId);
      this.flashes.update(m => { const n = { ...m }; delete n[gameId]; return n; });
    }, LIVE_FLASH_MS));
  }

  /** Klick auf die Benachrichtigung: zum Brett scrollen und es hervorheben (auch ohne „Aufblitzen“). */
  showBoard(gameId: string, kind: 'result' | 'swing'): void {
    const el = this.host.nativeElement.querySelector(`[data-game="${CSS.escape(gameId)}"]`);
    const still = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches || this.eink.enabled();
    el?.scrollIntoView({ block: 'center', behavior: still ? 'auto' : 'smooth' });
    this.flashBoard(gameId, kind);
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

  setRound(r: number): void {
    if (this.viewToken()) { this.changeView({ round: r }); return; }
    void this.router.navigate([], { relativeTo: this.route, queryParams: { round: r }, queryParamsHandling: 'merge' });
  }

  setFilter(name: 'team' | 'player', value: string | null): void {
    if (this.viewToken()) { this.changeView({ [name]: value }); return; }
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
    if (this.viewToken()) { this.changeView({ add: parseAddParam(added.join(',')), ext: parseExtParam(ext.join(',')) }); return; }
    void this.router.navigate([], { relativeTo: this.route, queryParamsHandling: 'merge',
      queryParams: { add: joinParam(parseAddParam(added.join(','))), ext: joinParam(parseExtParam(ext.join(','))) } });
  }

  /** ✕ an einer Karte des Rasters. */
  removeBoard(b: ShownBoard): void {
    if (b.removeKind === 'add') this.removeAdded(b.addKey!);
    else if (b.removeKind === 'ext') this.removeExt(b.addKey!);
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

  /** „Brett drehen“: kehrt die Vorgabe (eigene Mannschaft bzw. Spieler unten) für diese eine Partie um. */
  toggleTurn(gameId: string): void {
    const next = new Set(this.turnedIds());
    if (!next.delete(gameId)) next.add(gameId);
    this.turnedIds.set(next);
    writeTurned(next);
  }

  /** „Besten Zug zeigen“: nur dieses Gerät — weder Adresse noch geteilte Ansicht, kein neuer Strom (der Zug kommt mit der Bewertung). */
  setBest(on: boolean): void {
    this.bestOn.set(on);
    writeLiveBest(on);
  }

  /** Vibration an/aus: nur dieses Gerät. Beim Einschalten kurz zur Bestätigung — der Klick ist die Nutzerinteraktion, die Chrome verlangt. */
  setVibrate(on: boolean): void {
    this.vibrateOn.set(on);
    writeLiveVibrate(on);
    if (on) vibrate(VIBRATE_CONFIRM);
  }

  /** Aufblitzen an/aus: nur dieses Gerät. Aus → ein laufender Tab-Hinweis endet. */
  setFlash(on: boolean): void {
    this.flashOn.set(on);
    writeLiveFlash(on);
    if (!on) this.tab.stop();
  }

  /**
   * Benachrichtigung an/aus. Beim Einschalten fragt der Browser nach der Erlaubnis (der Klick ist die Nutzergeste); abgelehnt
   * oder blockiert → Schalter zurück und ein Hinweis, wo man sie erlaubt.
   */
  async setNotify(on: boolean, toggle?: MatSlideToggle): Promise<void> {
    if (!on) { this.notifyOn.set(false); writeLiveNotify(false); return; }
    const granted = await requestNotifyPermission();
    this.notifyOn.set(granted);
    writeLiveNotify(granted);
    if (!granted) {
      if (toggle) toggle.checked = false;
      this.snackbar.warn(this.translate.instant('tournaments.live.notifyDenied'));
    }
  }

  /**
   * Balken an/aus (1.039.0): nur für diesen Zuschauer, gemerkt im Browser — auch Besitzer und Zuschauer der geteilten Ansicht
   * (sie wird dabei NICHT geändert). Auf der langen Adresse fällt ein ausdrückliches `?eval=` weg, sonst überstimmte es die
   * neue Wahl beim Neuladen.
   */
  setEval(on: boolean): void {
    this.evalOn.set(on);
    writeLiveEval(on);
    this.connect();
    if (!this.viewToken() && this.urlEval)
      void this.router.navigate([], { relativeTo: this.route, queryParams: { eval: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  /** Link dieser Ansicht: Runde immer ausdrücklich (sonst zeigte er morgen die nächste), Mannschaft/Spieler; Balken nicht (jeder selbst). */
  shareUrl(): string {
    const tree = this.router.createUrlTree(['/tournaments', this.id, 'live'], { queryParams: this.longParams(this.currentView()) });
    return window.location.origin + this.router.serializeUrl(tree);
  }

  /** Teile der langen Adresse (bleibt gültig; Rückfall, wenn das Anlegen der geteilten Ansicht scheitert). */
  private longParams(v: LiveViewState): Record<string, string | number | null> {
    return { round: v.round, team: v.team, player: v.player, add: joinParam(v.add), ext: joinParam(v.ext) };
  }

  /** Kurzer, lebender Link einer geteilten Ansicht. */
  viewUrl(token: string): string { return `${window.location.origin}/live/${token}`; }

  /**
   * „Link teilen“ (1.029.0, Wunsch 2026-11-26 „einfach einen Hash“): beim ersten Mal wird die Ansicht als geteilte Ansicht
   * angelegt (Schlüssel bleibt in diesem Browser) und die Seite wechselt auf `/live/<token>` — jede weitere Änderung sehen
   * alle mit dem Link. Danach teilt der Knopf einfach den kurzen Link. Scheitert das Anlegen, geht die lange Adresse.
   */
  share(): void {
    const token = this.viewToken();
    if (token) { this.shareLink(this.viewUrl(token)); return; }
    this.api.createView(this.id, this.currentView()).subscribe({
      next: r => {
        if (r.editKey) writeViewKey(r.token, r.editKey);
        this.shareLink(this.viewUrl(r.token));
        void this.router.navigate(['/live', r.token], { replaceUrl: true });
      },
      error: () => this.shareLink(this.shareUrl()),
    });
  }

  /** Am Handy das Teilen-Blatt, sonst Zwischenablage mit Snackbar (DESIGN 5.x „Link teilen“, wie die Partie-Links). */
  private shareLink(url: string): void {
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
