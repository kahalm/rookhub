import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { DashboardService } from '../../core/dashboard.service';
import { ChessableService } from '../chessable/chessable.service';
import { AuthService } from '../../core/auth.service';
import { MenuService } from '../../core/menu.service';
import { InAppNotificationService } from '../../core/in-app-notification.service';
import { FavoritesService } from '../../core/favorites.service';

const MENU = new Set<string>([
  'puzzles', 'friends', 'tournaments', 'repertoires', 'training-goals',
  'courses', 'leaderboards', 'games', 'weekly', 'stats', 'analysis',
]);

function setup(opts: { isAdmin?: boolean; friends?: unknown[]; courses?: unknown[]; arrived?: Subject<void> } = {}) {
  const arrived = opts.arrived ?? new Subject<void>();
  const dashboardService = {
    getRepertoires: () => of([]),
    getCourses: () => of(opts.courses ?? []),
    getSubscriptions: () => of([]),
    getFriends: () => of(opts.friends ?? []),
    getPuzzleStats: () => of({ solved: 0, accuracy: 0, puzzleElo: 1500 }),
  };
  TestBed.configureTestingModule({
    imports: [DashboardComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: AuthService, useValue: { isAdmin: opts.isAdmin ?? false, currentUser: { username: 'me' } } },
      { provide: DashboardService, useValue: dashboardService },
      { provide: MenuService, useValue: { visible$: of(MENU), isVisible: (k: string) => MENU.has(k) } },
      { provide: ChessableService, useValue: { getActiveImportsAdmin: () => of([]) } },
      { provide: InAppNotificationService, useValue: { arrived$: arrived.asObservable() } },
      { provide: FavoritesService, useValue: { count: () => of(0) } },
    ],
  });
  TestBed.overrideComponent(DashboardComponent, { set: { template: '' } });
  const fixture = TestBed.createComponent(DashboardComponent);
  fixture.detectChanges(); // ngOnInit
  return { component: fixture.componentInstance, fixture };
}

describe('DashboardComponent friend-count reactivity', () => {
  beforeEach(() => localStorage.removeItem('rookhub_dashboard_layout_v2'));

  it('loads the initial friend count', () => {
    const { component } = setup({ friends: [{}, {}] });
    expect(component.friendCount).toBe(2);
  });

  it('loads the initial course count (shown on the courses tile like repertoires)', () => {
    const { component } = setup({ courses: [{}, {}, {}] });
    expect(component.courseCount).toBe(3);
  });

  it('refreshes the friend count when a notification arrives', () => {
    const arrived = new Subject<void>();
    let friends: unknown[] = [{}, {}];
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin: false, currentUser: { username: 'me' } } },
        { provide: DashboardService, useValue: { getRepertoires: () => of([]), getCourses: () => of([]), getSubscriptions: () => of([]), getFriends: () => of(friends), getPuzzleStats: () => of({ solved: 0, accuracy: 0, puzzleElo: 1500 }) } },
        { provide: MenuService, useValue: { visible$: of(MENU), isVisible: () => true } },
        { provide: ChessableService, useValue: { getActiveImportsAdmin: () => of([]) } },
        { provide: InAppNotificationService, useValue: { arrived$: arrived.asObservable() } },
        { provide: FavoritesService, useValue: { count: () => of(0) } },
      ],
    });
    TestBed.overrideComponent(DashboardComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    friends = [{}, {}, {}];
    arrived.next();
    expect(fixture.componentInstance.friendCount).toBe(3);
  });
});

describe('DashboardComponent cached snapshot', () => {
  const CACHE_KEY = 'rookhub_dashboard_cache_v1_u7';
  beforeEach(() => { localStorage.clear(); });
  afterEach(() => { localStorage.clear(); });

  it('paints cached values immediately before the network responds', () => {
    // Snapshot des letzten Aufrufs vorseeden.
    localStorage.setItem(CACHE_KEY, JSON.stringify({
      repertoireCount: 12, courseCount: 42, pinnedCourses: [], subscriptions: [],
      subscriptionCount: 3, friendCount: 5, favoriteCount: 8,
      puzzleSolved: 1234, puzzleAccuracy: 87, puzzleElo: 2100,
    }));
    // Alle Netz-Calls „hängen" (nie-emittierende Subjects) → forkJoin emittiert NIE →
    // die Komponente muss die Zahlen aus dem Cache anzeigen.
    const never = new Subject<never>().asObservable();
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin: false, currentUser: { username: 'me', userId: 7 } } },
        { provide: DashboardService, useValue: {
          getRepertoires: () => never, getCourses: () => never, getSubscriptions: () => never,
          getFriends: () => never, getPuzzleStats: () => never,
        } },
        { provide: MenuService, useValue: { visible$: of(MENU), isVisible: () => true } },
        { provide: ChessableService, useValue: { getActiveImportsAdmin: () => of([]) } },
        { provide: InAppNotificationService, useValue: { arrived$: new Subject<void>().asObservable() } },
        { provide: FavoritesService, useValue: { count: () => never } },
      ],
    });
    TestBed.overrideComponent(DashboardComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.repertoireCount).toBe(12);
    expect(c.courseCount).toBe(42);
    expect(c.puzzleElo).toBe(2100);
    expect(c.puzzleSolved).toBe(1234);
    expect(c.friendCount).toBe(5);
  });

  it('writes the fresh snapshot back to cache after a successful fetch', () => {
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin: false, currentUser: { username: 'me', userId: 7 } } },
        { provide: DashboardService, useValue: {
          getRepertoires: () => of([{}, {}]),
          getCourses: () => of([{ isPinned: false }, { isPinned: false }, { isPinned: false }]),
          getSubscriptions: () => of([]),
          getFriends: () => of([{}]),
          getPuzzleStats: () => of({ solved: 55, accuracy: 91, puzzleElo: 1900 }),
        } },
        { provide: MenuService, useValue: { visible$: of(MENU), isVisible: () => true } },
        { provide: ChessableService, useValue: { getActiveImportsAdmin: () => of([]) } },
        { provide: InAppNotificationService, useValue: { arrived$: new Subject<void>().asObservable() } },
        { provide: FavoritesService, useValue: { count: () => of(4) } },
      ],
    });
    TestBed.overrideComponent(DashboardComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const cached = JSON.parse(localStorage.getItem(CACHE_KEY)!);
    expect(cached.repertoireCount).toBe(2);
    expect(cached.courseCount).toBe(3);
    expect(cached.puzzleElo).toBe(1900);
    expect(cached.favoriteCount).toBe(4);
  });
});

describe('DashboardComponent tiles', () => {
  beforeEach(() => localStorage.removeItem('rookhub_dashboard_layout_v2'));
  afterEach(() => localStorage.removeItem('rookhub_dashboard_layout_v2'));

  it('shows the curated default tiles in order (puzzles first; non-default tiles hidden)', () => {
    const { component } = setup();
    const ids = component.visibleTiles.map(t => t.id);
    expect(ids).toEqual(['puzzles', 'weekly', 'repertoires', 'courses', 'trainingGoals', 'activityTimer', 'leaderboards']);
    expect(ids).not.toContain('messages');    // im Standard ausgeblendet
    expect(ids).not.toContain('tournaments');  // im Standard ausgeblendet
  });

  it('hides the chessable-queue tile for non-admins (even in edit mode)', () => {
    const { component } = setup({ isAdmin: false });
    component.editing = true;
    expect(component.visibleTiles.map(t => t.id)).not.toContain('chessableQueue');
  });

  it('offers the chessable-queue tile to admins in edit mode (default hidden)', () => {
    const { component } = setup({ isAdmin: true });
    expect(component.visibleTiles.map(t => t.id)).not.toContain('chessableQueue'); // Standard: aus
    component.editing = true;
    expect(component.visibleTiles.map(t => t.id)).toContain('chessableQueue');     // im Edit zuschaltbar
  });

  it('toggling a tile hides it (outside edit mode) and persists', () => {
    const { component } = setup();
    const puzzles = component.tiles.find(t => t.id === 'puzzles')!;
    component.toggle(puzzles);
    expect(component.isEnabled(puzzles)).toBeFalse();
    expect(component.visibleTiles.map(t => t.id)).not.toContain('puzzles');
    expect(JSON.parse(localStorage.getItem('rookhub_dashboard_layout_v2')!).hidden).toContain('puzzles');
  });

  it('edit mode still shows hidden tiles so they can be re-enabled', () => {
    const { component } = setup();
    const puzzles = component.tiles.find(t => t.id === 'puzzles')!;
    component.toggle(puzzles);   // jetzt ausgeblendet
    expect(component.visibleTiles.map(t => t.id)).not.toContain('puzzles');
    component.editing = true;
    expect(component.visibleTiles.map(t => t.id)).toContain('puzzles');
  });

  it('drag-drop reorders the tiles and persists the new order', () => {
    const { component } = setup();
    component.editing = true;
    expect(component.visibleTiles[0].id).toBe('puzzles');
    component.drop({ previousIndex: 0, currentIndex: 2 } as any);
    expect(component.visibleTiles[2].id).toBe('puzzles');
    const savedOrder: string[] = JSON.parse(localStorage.getItem('rookhub_dashboard_layout_v2')!).order;
    expect(savedOrder.indexOf('puzzles')).toBe(2);
  });

  it('moveDown / moveUp shift a tile by one and persist', () => {
    const { component } = setup();
    expect(component.visibleTiles[0].id).toBe('puzzles');
    const puzzles = component.tiles[0];
    component.moveDown(puzzles);
    expect(component.visibleTiles[1].id).toBe('puzzles');
    component.moveUp(puzzles);
    expect(component.visibleTiles[0].id).toBe('puzzles');
    const savedOrder: string[] = JSON.parse(localStorage.getItem('rookhub_dashboard_layout_v2')!).order;
    expect(savedOrder[0]).toBe('puzzles');
  });

  it('moveUp on the first tile is a no-op', () => {
    const { component } = setup();
    const before = component.visibleTiles.map(t => t.id);
    component.moveUp(component.tiles[0]);
    expect(component.visibleTiles.map(t => t.id)).toEqual(before);
  });

  it('applyDefault restores the curated default order + visibility and persists it', () => {
    const { component } = setup();
    const puzzles = component.tiles.find(t => t.id === 'puzzles')!;
    component.toggle(puzzles);
    component.drop({ previousIndex: 0, currentIndex: 3 } as any);
    component.applyDefault();
    expect(component.visibleTiles.map(t => t.id)).toEqual(['puzzles', 'weekly', 'repertoires', 'courses', 'trainingGoals', 'activityTimer', 'leaderboards']);
    expect(component.isEnabled(puzzles)).toBeTrue();
    const saved = JSON.parse(localStorage.getItem('rookhub_dashboard_layout_v2')!);
    expect(saved.order[0]).toBe('puzzles');
    expect(saved.hidden).toContain('messages'); // Nicht-Standard-Kachel ist ausgeblendet
  });

  it('appends newly introduced tiles that are missing from a stored layout', () => {
    // Alt gespeichertes Layout kennt nur 2 Kacheln → neue müssen hinten angehängt erscheinen.
    localStorage.setItem('rookhub_dashboard_layout_v2', JSON.stringify({ order: ['friends', 'puzzles'], hidden: [] }));
    const { component } = setup();
    const ids = component.visibleTiles.map(t => t.id);
    expect(ids[0]).toBe('friends');
    expect(ids[1]).toBe('puzzles');
    expect(ids).toContain('stats'); // nicht im gespeicherten Layout → angehängt
  });
});
