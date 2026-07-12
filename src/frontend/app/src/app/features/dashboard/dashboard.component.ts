import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { forkJoin, of, timer } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/auth.service';
import { Subscription } from '../../core/models';
import { DashboardService, DashboardCourse } from '../../core/dashboard.service';
import { DashboardCacheService } from '../../core/dashboard-cache.service';
import { DashboardLayoutService } from '../../core/dashboard-layout.service';
import { MenuService } from '../../core/menu.service';
import { InAppNotificationService } from '../../core/in-app-notification.service';
import { FavoritesService } from '../../core/favorites.service';
import { ChessableService, ChessableAdminImport } from '../chessable/chessable.service';
import { ActivityTimerTileComponent } from '../training-goals/activity-timer-tile.component';

/** Ein Schnellzugriff-Button auf einer Kachel. */
interface TileButton { labelKey: string; link: string; }

/** Definition einer Dashboard-Kachel (Modul). Inhalt/Eignung/Untertitel werden lazy ausgewertet. */
interface TileDef {
  id: string;
  icon: string;
  titleKey: string;
  /** Ob die Kachel für diesen Nutzer überhaupt in Frage kommt (Menü-Sichtbarkeit / Admin). */
  eligible: () => boolean;
  /** Untertitel-i18n-Key + optionale Parameter (z. B. Zähler), live ausgewertet. */
  subtitle: () => { key: string; params?: Record<string, unknown> };
  buttons: TileButton[];
}

/** Kuratierter Standard: diese Kacheln sind anfänglich sichtbar — in genau dieser Reihenfolge. */
const DEFAULT_VISIBLE = [
  'puzzles', 'weekly', 'repertoires', 'pinnedCourses', 'courses', 'trainingGoals', 'leaderboards',
];
/** Kanonische Reihenfolge ALLER bekannten Kacheln: Standard-sichtbare zuerst, Rest dahinter
 *  (der Rest ist im Standard ausgeblendet, im Bearbeitungsmodus aber zuschaltbar). */
const DEFAULT_ORDER = [
  ...DEFAULT_VISIBLE,
  'activityTimer', 'favorites', 'tournaments', 'friends', 'games', 'stats', 'analysis', 'messages', 'chessableQueue',
];
/** Im Standard ausgeblendete Kacheln (alles außer DEFAULT_VISIBLE). */
const DEFAULT_HIDDEN = DEFAULT_ORDER.filter(id => !DEFAULT_VISIBLE.includes(id));

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatListModule, MatProgressBarModule, MatTooltipModule, DragDropModule, TranslatePipe,
    ActivityTimerTileComponent,
  ],
  template: `
    <div class="dashboard">
      <div class="dashboard-head">
        <h1>{{ 'dashboard.welcome' | translate:{ username: auth.currentUser?.username } }}</h1>
        <div class="head-actions">
          @if (editing) {
            <button mat-button (click)="applyDefault()">
              <mat-icon>restart_alt</mat-icon> {{ 'dashboard.edit.default' | translate }}
            </button>
            <button mat-raised-button color="primary" (click)="toggleEdit()">
              <mat-icon>done</mat-icon> {{ 'dashboard.edit.done' | translate }}
            </button>
          } @else {
            <button mat-stroked-button (click)="toggleEdit()">
              <mat-icon>tune</mat-icon> {{ 'dashboard.edit.customize' | translate }}
            </button>
          }
        </div>
      </div>

      @if (editing) {
        <p class="edit-hint">{{ 'dashboard.edit.hint' | translate }}</p>
      }

      <div class="dashboard-grid"
           cdkDropList cdkDropListOrientation="mixed" [cdkDropListDisabled]="!editing" (cdkDropListDropped)="drop($event)">
        @for (tile of visibleTiles; track tile.id; let first = $first; let last = $last) {
          @let sub = tile.subtitle();
          <mat-card cdkDrag [cdkDragDisabled]="!editing"
                    [class.tile-off]="editing && !isEnabled(tile)" [class.tile-editing]="editing">
            @if (editing) {
              <!-- Transparentes Schild: fängt NUR Klicks auf den Kachel-Buttons ab (keine
                   versehentliche Navigation). Bewusst KEIN cdkDragHandle mehr — sonst fängt es
                   auf dem Handy jede Berührung als Drag ab und die Seite lässt sich nicht scrollen.
                   Gezogen wird ausschließlich am dedizierten Griff in den Steuerelementen. -->
              <div class="tile-shield"></div>
              <div class="tile-controls">
                <button mat-icon-button class="tc-btn tc-drag" cdkDragHandle
                        [matTooltip]="'dashboard.edit.dragAria' | translate" [attr.aria-label]="'dashboard.edit.dragAria' | translate">
                  <mat-icon>drag_indicator</mat-icon>
                </button>
                <button mat-icon-button class="tc-btn" [disabled]="first" (click)="moveUp(tile)"
                        [matTooltip]="'dashboard.edit.moveUp' | translate" [attr.aria-label]="'dashboard.edit.moveUp' | translate">
                  <mat-icon>arrow_upward</mat-icon>
                </button>
                <button mat-icon-button class="tc-btn" [disabled]="last" (click)="moveDown(tile)"
                        [matTooltip]="'dashboard.edit.moveDown' | translate" [attr.aria-label]="'dashboard.edit.moveDown' | translate">
                  <mat-icon>arrow_downward</mat-icon>
                </button>
                <button mat-icon-button class="tc-btn tc-eye" (click)="toggle(tile)"
                        [matTooltip]="(isEnabled(tile) ? 'dashboard.edit.hideAria' : 'dashboard.edit.showAria') | translate"
                        [attr.aria-label]="(isEnabled(tile) ? 'dashboard.edit.hideAria' : 'dashboard.edit.showAria') | translate">
                  <mat-icon>{{ isEnabled(tile) ? 'visibility' : 'visibility_off' }}</mat-icon>
                </button>
              </div>
            }
            <mat-card-header>
              <mat-icon mat-card-avatar>{{ tile.icon }}</mat-icon>
              <mat-card-title>{{ tile.titleKey | translate }}</mat-card-title>
              <mat-card-subtitle>{{ sub.key | translate:sub.params }}</mat-card-subtitle>
            </mat-card-header>
            @if (tile.id === 'pinnedCourses') {
              <mat-card-content class="pinned-courses">
                @if (pinnedCourses.length === 0) {
                  <p class="pinned-empty">{{ 'dashboard.pinnedCourses.empty' | translate }}</p>
                } @else {
                  @for (c of pinnedCourses; track c.bookId) {
                    @let done = c.puzzleCount > 0 && c.solvedCount >= c.puzzleCount;
                    <div class="pinned-course">
                      <div class="pc-header">
                        <span class="pc-name" [title]="c.displayName">{{ c.displayName }}</span>
                        <span class="pc-prog" [class.pc-prog-done]="done">
                          @if (done) { <mat-icon class="pc-done-icon">emoji_events</mat-icon> }
                          {{ c.solvedCount }}/{{ c.puzzleCount }}
                        </span>
                      </div>
                      <mat-progress-bar class="pc-bar" mode="determinate" [value]="c.progressPercent"></mat-progress-bar>
                      <div class="pc-actions">
                        <button mat-flat-button color="primary" class="pc-btn pc-btn-primary"
                                [routerLink]="['/courses', c.bookId, 'sequential']" [disabled]="c.puzzleCount === 0">
                          <mat-icon>format_list_numbered</mat-icon>{{ 'courses.sequential' | translate }}
                        </button>
                        <button mat-stroked-button class="pc-btn"
                                [routerLink]="['/courses', c.bookId, 'random']" [disabled]="c.puzzleCount === 0">
                          <mat-icon>shuffle</mat-icon>{{ 'courses.random' | translate }}
                        </button>
                      </div>
                    </div>
                  }
                }
              </mat-card-content>
              <mat-card-actions>
                <button mat-button routerLink="/courses">{{ 'dashboard.pinnedCourses.viewAll' | translate }}</button>
              </mat-card-actions>
            } @else if (tile.id === 'activityTimer') {
              <mat-card-content>
                <app-activity-timer-tile></app-activity-timer-tile>
              </mat-card-content>
            } @else {
              <mat-card-actions>
                @for (b of tile.buttons; track b.link) {
                  <button mat-button [routerLink]="b.link">{{ b.labelKey | translate }}</button>
                }
              </mat-card-actions>
            }
          </mat-card>
        }
      </div>

      @if (auth.isAdmin && chessableActive.length > 0) {
        <h2>{{ 'dashboard.chessableQueue.heading' | translate }}</h2>
        <mat-list>
          @for (imp of chessableActive; track imp.id) {
            <mat-list-item>
              <mat-icon matListItemIcon>cloud_download</mat-icon>
              <span matListItemTitle>{{ imp.courseName || imp.bid }} — {{ imp.username }}</span>
              <span matListItemLine>{{ imp.statusLabel }}</span>
            </mat-list-item>
          }
        </mat-list>
      }

      @if (subscriptions.length > 0) {
        <h2>{{ 'dashboard.subscribedTournaments' | translate }}</h2>
        <mat-list>
          @for (sub of subscriptions; track sub.id) {
            <a mat-list-item [routerLink]="['/tournaments', sub.crawlerTournamentId]" class="tournament-link">
              <mat-icon matListItemIcon>emoji_events</mat-icon>
              <span matListItemTitle>{{ sub.tournamentName }}</span>
              <span matListItemLine>{{ 'dashboard.subscribedAt' | translate:{ date: (sub.subscribedAt | date) } }}</span>
            </a>
          }
        </mat-list>
      }
    </div>
  `,
  styles: [`
    .dashboard { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .dashboard-head { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; }
    .head-actions { display: flex; gap: 0.5rem; }
    .head-actions button mat-icon { margin-right: 0.25rem; }
    .edit-hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0.25rem 0 0.5rem; }
    .dashboard-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(300px, 100%), 1fr)); gap: 1rem; margin: 1rem 0; }
    mat-icon[mat-card-avatar] { font-size: 40px; width: 40px; height: 40px; }
    /* Bearbeitungsmodus: gleiche Rasteransicht, nur Schild + Steuerelemente als Overlay. */
    mat-card.tile-editing { position: relative; }
    mat-card.tile-off { opacity: 0.45; }
    .tile-controls .tc-drag { cursor: grab; }
    .tile-controls .tc-drag:active { cursor: grabbing; }
    .cdk-drag-preview .tc-drag { cursor: grabbing; }
    .tile-shield { position: absolute; inset: 0; z-index: 1; border-radius: inherit; }
    .tile-controls { position: absolute; top: 6px; right: 6px; z-index: 2; display: flex; align-items: center; gap: 0;
      background: color-mix(in srgb, var(--mat-sys-surface-container-high, #2a2a2a) 92%, transparent);
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent);
      border-radius: 999px; padding: 3px; box-shadow: 0 2px 6px rgba(0,0,0,0.22); }
    /* Material 3: NICHT die äußere Größe klemmen, sondern die MDC-Tokens setzen — sonst bleibt
       der 48px-State-Layer/Touch-Target darunter und überlappt die Nachbarbuttons. */
    .tile-controls .tc-btn {
      --mdc-icon-button-state-layer-size: 32px;
      --mdc-icon-button-icon-size: 18px;
      width: 32px; height: 32px; padding: 0;
    }
    .tile-controls .tc-btn mat-icon { font-size: 18px; width: 18px; height: 18px; line-height: 18px; }
    .tile-controls .tc-btn[disabled] { opacity: 0.35; }
    .tile-controls .tc-drag mat-icon { opacity: 0.7; }
    .tile-controls .tc-eye { color: var(--mat-sys-primary, #82b1ff); }
    .cdk-drag-preview { box-shadow: 0 5px 16px rgba(0,0,0,0.3); border-radius: 8px; }
    .cdk-drag-placeholder { opacity: 0.3; }
    .cdk-drag-animating { transition: transform 200ms cubic-bezier(0, 0, 0.2, 1); }
    .dashboard-grid.cdk-drop-list-dragging mat-card:not(.cdk-drag-placeholder) { transition: transform 200ms cubic-bezier(0, 0, 0.2, 1); }
    .tournament-link { cursor: pointer; text-decoration: none; color: inherit; }
    .tournament-link:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    /* Angepinnte-Kurse-Kachel: je Kurs Titel + Fortschrittsbalken + zwei Start-Buttons. */
    .pinned-courses { display: flex; flex-direction: column; gap: 0.9rem; padding-top: 0.25rem; }
    .pinned-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; margin: 0; font-size: 0.9rem; }
    .pinned-course { display: flex; flex-direction: column; gap: 0.5rem; }
    .pinned-course + .pinned-course { border-top: 1px solid color-mix(in srgb, currentColor 10%, transparent); padding-top: 0.75rem; }
    .pc-header { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; }
    .pc-name { font-weight: 600; font-size: 0.92rem; line-height: 1.3; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .pc-prog { font-variant-numeric: tabular-nums; font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); white-space: nowrap; display: inline-flex; align-items: center; gap: 0.25rem; }
    .pc-prog-done { color: color-mix(in srgb, #f9a825 85%, currentColor); font-weight: 600; }
    .pc-done-icon { font-size: 16px; width: 16px; height: 16px; }
    .pc-bar { border-radius: 999px; overflow: hidden; height: 6px; }
    .pc-bar ::ng-deep .mdc-linear-progress__buffer { border-radius: 999px; }
    .pc-actions { display: flex; gap: 0.5rem; margin-top: 0.15rem; }
    .pc-btn { flex: 1; min-width: 0; }
    .pc-btn mat-icon { font-size: 18px; width: 18px; height: 18px; margin-right: 4px; vertical-align: middle; }
    @media (max-width: 768px) {
      .dashboard { padding: 0.75rem; }
      h1 { font-size: 1.4rem; }
    }
  `]
})
export class DashboardComponent implements OnInit {
  private destroyRef = inject(DestroyRef);

  repertoireCount = 0;
  courseCount = 0;
  /** Vom Nutzer angepinnte Kurse (Schnellstart-Kachel). */
  pinnedCourses: DashboardCourse[] = [];
  subscriptionCount = 0;
  friendCount = 0;
  favoriteCount = 0;
  puzzleSolved = 0;
  puzzleAccuracy = 0;
  puzzleElo = 1500;
  subscriptions: Subscription[] = [];

  /** Admin: aktive Chessable-Importe aller User (laufend/pausiert), live gepollt. */
  chessableActive: (ChessableAdminImport & { statusLabel: string })[] = [];

  /** Bearbeitungsmodus: Kacheln per Drag & Drop sortieren + ein-/ausblenden. */
  editing = false;
  /** Aktuelle Sichtbarkeitsmenge der Menü-Keys (steuert die Kachel-Eignung). */
  private menuKeys = new Set<string>();
  /** Volle Reihenfolge aller bekannten Kachel-IDs (persistiert). */
  private order: string[] = [...DEFAULT_ORDER];
  /** Ausgeblendete Kachel-IDs (persistiert). */
  private hidden = new Set<string>();

  /** Statische Kacheldefinitionen; Closures greifen lazy auf die Live-Felder zu. */
  private readonly tileMap: Record<string, TileDef> = {
    puzzles: {
      id: 'puzzles', icon: 'extension', titleKey: 'dashboard.puzzles.title',
      eligible: () => this.menuKeys.has('puzzles'),
      subtitle: () => ({ key: 'dashboard.puzzles.subtitle', params: { elo: this.puzzleElo, solved: this.puzzleSolved, accuracy: this.puzzleAccuracy } }),
      buttons: [
        { labelKey: 'dashboard.puzzles.solve', link: '/puzzles' },
        { labelKey: 'dashboard.puzzles.daily', link: '/puzzles/daily/today' },
        { labelKey: 'dashboard.puzzles.endless', link: '/puzzles/endless' },
      ],
    },
    trainingGoals: {
      id: 'trainingGoals', icon: 'track_changes', titleKey: 'dashboard.trainingGoals.title',
      eligible: () => this.menuKeys.has('training-goals'),
      subtitle: () => ({ key: 'dashboard.trainingGoals.subtitle' }),
      buttons: [{ labelKey: 'dashboard.trainingGoals.open', link: '/training-goals' }],
    },
    activityTimer: {
      id: 'activityTimer', icon: 'timer', titleKey: 'dashboard.activityTimer.title',
      eligible: () => this.menuKeys.has('training-goals'),
      subtitle: () => ({ key: 'dashboard.activityTimer.subtitle' }),
      buttons: [],   // eigener Inhalt via <app-activity-timer-tile>
    },
    courses: {
      id: 'courses', icon: 'school', titleKey: 'dashboard.courses.title',
      eligible: () => this.menuKeys.has('courses'),
      subtitle: () => ({ key: 'dashboard.courses.count', params: { count: this.courseCount } }),
      buttons: [{ labelKey: 'dashboard.courses.open', link: '/courses' }],
    },
    pinnedCourses: {
      id: 'pinnedCourses', icon: 'push_pin', titleKey: 'dashboard.pinnedCourses.title',
      // Zeigt sich normal nur, wenn ≥1 Kurs angepinnt ist; im Bearbeitungsmodus immer (positionierbar),
      // dann mit Leer-Hinweis. Inhalt wird im Template gesondert gerendert (Liste + je 2 Start-Buttons).
      eligible: () => this.menuKeys.has('courses') && (this.editing || this.pinnedCourses.length > 0),
      subtitle: () => ({ key: 'dashboard.pinnedCourses.subtitle', params: { count: this.pinnedCourses.length } }),
      buttons: [],
    },
    leaderboards: {
      id: 'leaderboards', icon: 'leaderboard', titleKey: 'dashboard.leaderboards.title',
      eligible: () => this.menuKeys.has('leaderboards'),
      subtitle: () => ({ key: 'dashboard.leaderboards.subtitle' }),
      buttons: [{ labelKey: 'dashboard.leaderboards.view', link: '/leaderboards' }],
    },
    tournaments: {
      id: 'tournaments', icon: 'emoji_events', titleKey: 'dashboard.subscriptions.title',
      eligible: () => this.menuKeys.has('tournaments'),
      subtitle: () => ({ key: 'dashboard.subscriptions.count', params: { count: this.subscriptionCount } }),
      buttons: [{ labelKey: 'dashboard.subscriptions.browse', link: '/tournaments' }],
    },
    friends: {
      id: 'friends', icon: 'people', titleKey: 'dashboard.friends.title',
      eligible: () => this.menuKeys.has('friends'),
      subtitle: () => ({ key: 'dashboard.friends.count', params: { count: this.friendCount } }),
      buttons: [{ labelKey: 'dashboard.friends.manage', link: '/friends' }],
    },
    repertoires: {
      id: 'repertoires', icon: 'library_books', titleKey: 'dashboard.repertoires.title',
      eligible: () => this.menuKeys.has('repertoires'),
      subtitle: () => ({ key: 'dashboard.repertoires.count', params: { count: this.repertoireCount } }),
      buttons: [{ labelKey: 'dashboard.repertoires.viewAll', link: '/repertoires' }],
    },
    games: {
      id: 'games', icon: 'sports_esports', titleKey: 'dashboard.games.title',
      eligible: () => this.menuKeys.has('games'),
      subtitle: () => ({ key: 'dashboard.games.subtitle' }),
      buttons: [{ labelKey: 'dashboard.games.open', link: '/games' }],
    },
    favorites: {
      id: 'favorites', icon: 'favorite', titleKey: 'dashboard.favorites.title',
      eligible: () => this.menuKeys.has('favorites'),
      subtitle: () => ({ key: 'dashboard.favorites.count', params: { count: this.favoriteCount } }),
      buttons: [{ labelKey: 'dashboard.favorites.open', link: '/favorites' }],
    },
    weekly: {
      id: 'weekly', icon: 'article', titleKey: 'dashboard.weekly.title',
      eligible: () => this.menuKeys.has('weekly'),
      subtitle: () => ({ key: 'dashboard.weekly.subtitle' }),
      buttons: [{ labelKey: 'dashboard.weekly.view', link: '/weekly' }],
    },
    stats: {
      id: 'stats', icon: 'show_chart', titleKey: 'dashboard.stats.title',
      eligible: () => this.menuKeys.has('stats'),
      subtitle: () => ({ key: 'dashboard.stats.subtitle' }),
      buttons: [{ labelKey: 'dashboard.stats.view', link: '/stats' }],
    },
    analysis: {
      id: 'analysis', icon: 'analytics', titleKey: 'dashboard.analysis.title',
      eligible: () => this.menuKeys.has('analysis'),
      subtitle: () => ({ key: 'dashboard.analysis.subtitle' }),
      buttons: [{ labelKey: 'dashboard.analysis.open', link: '/analysis' }],
    },
    messages: {
      id: 'messages', icon: 'mail', titleKey: 'dashboard.messages.title',
      eligible: () => true, // /messages ist für jeden eingeloggten Nutzer erreichbar
      subtitle: () => ({ key: 'dashboard.messages.subtitle' }),
      buttons: [{ labelKey: 'dashboard.messages.open', link: '/messages' }],
    },
    chessableQueue: {
      id: 'chessableQueue', icon: 'cloud_download', titleKey: 'dashboard.chessableQueue.title',
      eligible: () => this.auth.isAdmin,
      subtitle: () => ({ key: 'dashboard.chessableQueue.count', params: { count: this.chessableActive.length } }),
      buttons: [{ labelKey: 'dashboard.chessableQueue.view', link: '/chessable' }],
    },
  };

  constructor(
    public auth: AuthService,
    private dashboardService: DashboardService,
    private cache: DashboardCacheService,
    private layout: DashboardLayoutService,
    private menu: MenuService,
    private chessable: ChessableService,
    private translate: TranslateService,
    private notif: InAppNotificationService,
    private favorites: FavoritesService,
  ) {}

  // ----- Kachel-Layout -----------------------------------------------------

  /** Geeignete Kacheln in gespeicherter Reihenfolge (neue/unbekannte IDs übersprungen). */
  get tiles(): TileDef[] {
    return this.order.map(id => this.tileMap[id]).filter((t): t is TileDef => !!t && t.eligible());
  }

  /** Im Bearbeitungsmodus alle geeigneten Kacheln, sonst nur die aktivierten. */
  get visibleTiles(): TileDef[] {
    const ordered = this.tiles;
    return this.editing ? ordered : ordered.filter(t => this.isEnabled(t));
  }

  isEnabled(tile: TileDef): boolean {
    return !this.hidden.has(tile.id);
  }

  toggleEdit(): void {
    this.editing = !this.editing;
  }

  toggle(tile: TileDef): void {
    if (this.hidden.has(tile.id)) this.hidden.delete(tile.id);
    else this.hidden.add(tile.id);
    this.persist();
  }

  drop(event: CdkDragDrop<TileDef[]>): void {
    if (event.previousIndex === event.currentIndex) return;
    const ids = this.tiles.map(t => t.id); // geeignete Kacheln in aktueller Reihenfolge
    moveItemInArray(ids, event.previousIndex, event.currentIndex);
    this.commitOrder(ids);
  }

  /** Kachel per Pfeil eine Position nach oben/unten schieben (Alternative zu Drag & Drop). */
  moveUp(tile: TileDef): void { this.shift(tile, -1); }
  moveDown(tile: TileDef): void { this.shift(tile, 1); }

  private shift(tile: TileDef, delta: number): void {
    const ids = this.tiles.map(t => t.id);
    const i = ids.indexOf(tile.id);
    const j = i + delta;
    if (i < 0 || j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    this.commitOrder(ids);
  }

  /** Geeignete Kacheln neu sortiert + nicht-geeignete (bewahrt) → persistieren. */
  private commitOrder(orderedEligibleIds: string[]): void {
    const ineligible = this.order.filter(id => !orderedEligibleIds.includes(id));
    this.order = [...orderedEligibleIds, ...ineligible];
    this.persist();
  }

  /** „Standard"-Knopf: kuratierten Default anwenden (DEFAULT_VISIBLE sichtbar in fester Reihenfolge,
   *  Rest ausgeblendet) und persistieren. */
  applyDefault(): void {
    this.order = [...DEFAULT_ORDER];
    this.hidden = new Set<string>(DEFAULT_HIDDEN);
    this.persist();
  }

  private persist(): void {
    this.layout.save({ order: this.order, hidden: [...this.hidden] });
  }

  // ----- Init --------------------------------------------------------------

  ngOnInit(): void {
    // Gespeichertes Layout laden: bekannte IDs in gespeicherter Reihenfolge zuerst,
    // danach noch nicht gesehene (neu hinzugekommene) Kacheln in Standardreihenfolge anhängen.
    const saved = this.layout.load();
    const known = new Set(DEFAULT_ORDER);
    if (saved.order.length === 0) {
      // Kein gespeichertes Layout → kuratierter Standard (DEFAULT_VISIBLE sichtbar, Rest aus).
      this.order = [...DEFAULT_ORDER];
      this.hidden = new Set(DEFAULT_HIDDEN);
    } else {
      // Vorhandenes Layout respektieren; neu hinzugekommene Kacheln hinten anhängen.
      const fromSaved = saved.order.filter(id => known.has(id));
      const appended = DEFAULT_ORDER.filter(id => !fromSaved.includes(id));
      this.order = [...fromSaved, ...appended];
      this.hidden = new Set(saved.hidden.filter(id => known.has(id)));
    }

    // Zwischenstand aus dem letzten Aufruf sofort anzeigen (Repertoire-/Kurs-Zähler,
    // angepinnte Kurse, Elo/Solved). Ist rein optional; die forkJoin-Antwort überschreibt
    // gleich mit den frischen Werten.
    const cached = this.cache.load();
    if (cached) {
      this.repertoireCount = cached.repertoireCount;
      this.courseCount = cached.courseCount;
      this.pinnedCourses = cached.pinnedCourses ?? [];
      this.subscriptions = cached.subscriptions ?? [];
      this.subscriptionCount = cached.subscriptionCount;
      this.friendCount = cached.friendCount;
      this.favoriteCount = cached.favoriteCount;
      this.puzzleSolved = cached.puzzleSolved;
      this.puzzleAccuracy = cached.puzzleAccuracy;
      this.puzzleElo = cached.puzzleElo;
    }

    // Menü-Sichtbarkeit live nachziehen (steuert, welche Kacheln überhaupt erscheinen).
    this.menu.visible$.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(set => this.menuKeys = set);

    // Freundeszahl reaktiv nachziehen, wenn eine Benachrichtigung eintrifft.
    this.notif.arrived$.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.dashboardService.getFriends().pipe(catchError(() => of([])))
      .subscribe(friends => this.friendCount = friends.length));

    // Admin: aktive Chessable-Queue laufend anzeigen (sofort + alle 10 s).
    if (this.auth.isAdmin) {
      timer(0, 10000).pipe(
        switchMap(() => this.chessable.getActiveImportsAdmin().pipe(catchError(() => of([] as ChessableAdminImport[])))),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(list => this.chessableActive = list.map(imp => ({ ...imp, statusLabel: this.chessableStatus(imp) })));
    }

    forkJoin({
      repertoires: this.dashboardService.getRepertoires().pipe(catchError(() => of([]))),
      courses: this.dashboardService.getCourses().pipe(catchError(() => of([]))),
      subscriptions: this.dashboardService.getSubscriptions().pipe(catchError(() => of([]))),
      friends: this.dashboardService.getFriends().pipe(catchError(() => of([]))),
      puzzleStats: this.dashboardService.getPuzzleStats().pipe(
        catchError(() => of({ totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0, puzzleElo: 1500 }))
      ),
      favorites: this.favorites.count().pipe(catchError(() => of(0))),
    }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ repertoires, courses, subscriptions, friends, puzzleStats, favorites }) => {
      this.repertoireCount = repertoires.length;
      this.courseCount = courses.length;
      this.pinnedCourses = courses.filter(c => c.isPinned)
        .sort((a, b) => a.displayName.localeCompare(b.displayName));
      this.subscriptions = subscriptions;
      this.subscriptionCount = subscriptions.length;
      this.friendCount = friends.length;
      this.favoriteCount = favorites;
      this.puzzleSolved = puzzleStats.solved || 0;
      this.puzzleAccuracy = puzzleStats.accuracy || 0;
      this.puzzleElo = puzzleStats.puzzleElo || 1500;
      // Frischen Snapshot fürs nächste Öffnen ablegen (per User in localStorage).
      this.cache.save({
        repertoireCount: this.repertoireCount,
        courseCount: this.courseCount,
        pinnedCourses: this.pinnedCourses,
        subscriptions: this.subscriptions,
        subscriptionCount: this.subscriptionCount,
        friendCount: this.friendCount,
        favoriteCount: this.favoriteCount,
        puzzleSolved: this.puzzleSolved,
        puzzleAccuracy: this.puzzleAccuracy,
        puzzleElo: this.puzzleElo,
      });
    });
  }

  /** Kurz-Status eines aktiven Imports: pausiert / Warteschlangen-Position / Hol-Fortschritt. */
  chessableStatus(imp: ChessableAdminImport): string {
    if (imp.status === 'paused') return this.translate.instant('chessable.statusPaused');
    if (imp.phase === 'queued') return this.translate.instant('chessable.queuePos', { pos: imp.queuedAhead + 1 });
    let s = this.translate.instant('chessable.phase_' + (imp.phase || 'queued'));
    if (imp.phase === 'fetching' && imp.chaptersTotal > 0) {
      s += ' ' + this.translate.instant('chessable.fetchProgress',
        { ch: imp.chaptersDone, total: imp.chaptersTotal, lines: imp.linesDone });
    }
    return s;
  }
}
