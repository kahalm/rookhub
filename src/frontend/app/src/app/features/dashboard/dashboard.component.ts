import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin, of, timer } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/auth.service';
import { Subscription } from '../../core/models';
import { DashboardService } from '../../core/dashboard.service';
import { DashboardLayoutService } from '../../core/dashboard-layout.service';
import { MenuService } from '../../core/menu.service';
import { InAppNotificationService } from '../../core/in-app-notification.service';
import { ChessableService, ChessableAdminImport } from '../chessable/chessable.service';

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

/** Kanonische Standardreihenfolge ALLER bekannten Kacheln (neue IDs hier ergänzen). */
const DEFAULT_ORDER = [
  'puzzles', 'trainingGoals', 'courses', 'leaderboards', 'tournaments', 'friends',
  'repertoires', 'games', 'weekly', 'stats', 'analysis', 'messages', 'chessableQueue',
];

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatListModule, MatTooltipModule, DragDropModule, TranslateModule,
  ],
  template: `
    <div class="dashboard">
      <div class="dashboard-head">
        <h1>{{ 'dashboard.welcome' | translate:{ username: auth.currentUser?.username } }}</h1>
        <div class="head-actions">
          @if (editing) {
            <button mat-button (click)="resetLayout()">
              <mat-icon>restart_alt</mat-icon> {{ 'dashboard.edit.reset' | translate }}
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

      <div class="dashboard-grid" [class.editing]="editing"
           cdkDropList [cdkDropListDisabled]="!editing" (cdkDropListDropped)="drop($event)">
        @for (tile of visibleTiles; track tile.id) {
          @let sub = tile.subtitle();
          <mat-card cdkDrag [cdkDragDisabled]="!editing" [class.tile-off]="editing && !isEnabled(tile)">
            @if (editing) {
              <div class="tile-edit-bar">
                <button mat-icon-button cdkDragHandle class="drag-handle"
                        [matTooltip]="'dashboard.edit.dragAria' | translate"
                        [attr.aria-label]="'dashboard.edit.dragAria' | translate">
                  <mat-icon>drag_indicator</mat-icon>
                </button>
                <span class="spacer"></span>
                <button mat-icon-button (click)="toggle(tile)"
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
            @if (!editing) {
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
    .dashboard-grid.editing { display: flex; flex-direction: column; }
    mat-icon[mat-card-avatar] { font-size: 40px; width: 40px; height: 40px; }
    .tile-edit-bar { display: flex; align-items: center; padding: 0 0.25rem; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .tile-edit-bar .spacer { flex: 1; }
    .drag-handle { cursor: grab; }
    mat-card.tile-off { opacity: 0.5; }
    .cdk-drag-preview { box-shadow: 0 5px 16px rgba(0,0,0,0.3); border-radius: 8px; }
    .cdk-drag-placeholder { opacity: 0.3; }
    .cdk-drag-animating { transition: transform 200ms cubic-bezier(0, 0, 0.2, 1); }
    .dashboard-grid.editing.cdk-drop-list-dragging mat-card:not(.cdk-drag-placeholder) { transition: transform 200ms cubic-bezier(0, 0, 0.2, 1); }
    .tournament-link { cursor: pointer; text-decoration: none; color: inherit; }
    .tournament-link:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    @media (max-width: 768px) {
      .dashboard { padding: 0.75rem; }
      h1 { font-size: 1.4rem; }
    }
  `]
})
export class DashboardComponent implements OnInit {
  private destroyRef = inject(DestroyRef);

  repertoireCount = 0;
  subscriptionCount = 0;
  friendCount = 0;
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
    courses: {
      id: 'courses', icon: 'school', titleKey: 'dashboard.courses.title',
      eligible: () => this.menuKeys.has('courses'),
      subtitle: () => ({ key: 'dashboard.courses.subtitle' }),
      buttons: [{ labelKey: 'dashboard.courses.open', link: '/courses' }],
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
    private layout: DashboardLayoutService,
    private menu: MenuService,
    private chessable: ChessableService,
    private translate: TranslateService,
    private notif: InAppNotificationService,
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
    // Volle Reihenfolge neu zusammensetzen: geeignete (neu sortiert) + nicht-geeignete (bewahrt).
    const ineligible = this.order.filter(id => !ids.includes(id));
    this.order = [...ids, ...ineligible];
    this.persist();
  }

  resetLayout(): void {
    this.layout.reset();
    this.order = [...DEFAULT_ORDER];
    this.hidden = new Set<string>();
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
    const fromSaved = saved.order.filter(id => known.has(id));
    const appended = DEFAULT_ORDER.filter(id => !fromSaved.includes(id));
    this.order = [...fromSaved, ...appended];
    this.hidden = new Set(saved.hidden.filter(id => known.has(id)));

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
      subscriptions: this.dashboardService.getSubscriptions().pipe(catchError(() => of([]))),
      friends: this.dashboardService.getFriends().pipe(catchError(() => of([]))),
      puzzleStats: this.dashboardService.getPuzzleStats().pipe(
        catchError(() => of({ totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0, puzzleElo: 1500 }))
      )
    }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ repertoires, subscriptions, friends, puzzleStats }) => {
      this.repertoireCount = repertoires.length;
      this.subscriptions = subscriptions;
      this.subscriptionCount = subscriptions.length;
      this.friendCount = friends.length;
      this.puzzleSolved = puzzleStats.solved || 0;
      this.puzzleAccuracy = puzzleStats.accuracy || 0;
      this.puzzleElo = puzzleStats.puzzleElo || 1500;
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
