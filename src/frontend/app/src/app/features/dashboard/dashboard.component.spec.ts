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

const MENU = new Set<string>([
  'puzzles', 'friends', 'tournaments', 'repertoires', 'training-goals',
  'courses', 'leaderboards', 'games', 'weekly', 'stats', 'analysis',
]);

function setup(opts: { isAdmin?: boolean; friends?: unknown[]; arrived?: Subject<void> } = {}) {
  const arrived = opts.arrived ?? new Subject<void>();
  const dashboardService = {
    getRepertoires: () => of([]),
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
    ],
  });
  TestBed.overrideComponent(DashboardComponent, { set: { template: '' } });
  const fixture = TestBed.createComponent(DashboardComponent);
  fixture.detectChanges(); // ngOnInit
  return { component: fixture.componentInstance, fixture };
}

describe('DashboardComponent friend-count reactivity', () => {
  beforeEach(() => localStorage.removeItem('rookhub_dashboard_layout'));

  it('loads the initial friend count', () => {
    const { component } = setup({ friends: [{}, {}] });
    expect(component.friendCount).toBe(2);
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
        { provide: DashboardService, useValue: { getRepertoires: () => of([]), getSubscriptions: () => of([]), getFriends: () => of(friends), getPuzzleStats: () => of({ solved: 0, accuracy: 0, puzzleElo: 1500 }) } },
        { provide: MenuService, useValue: { visible$: of(MENU), isVisible: () => true } },
        { provide: ChessableService, useValue: { getActiveImportsAdmin: () => of([]) } },
        { provide: InAppNotificationService, useValue: { arrived$: arrived.asObservable() } },
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

describe('DashboardComponent tiles', () => {
  beforeEach(() => localStorage.removeItem('rookhub_dashboard_layout'));
  afterEach(() => localStorage.removeItem('rookhub_dashboard_layout'));

  it('shows the eligible tiles in default order (puzzles first, messages always present)', () => {
    const { component } = setup();
    const ids = component.visibleTiles.map(t => t.id);
    expect(ids[0]).toBe('puzzles');
    expect(ids).toContain('messages'); // immer sichtbar für eingeloggte Nutzer
    expect(ids).not.toContain('chessableQueue'); // nur Admin
  });

  it('hides the chessable-queue tile for non-admins', () => {
    expect(setup({ isAdmin: false }).component.visibleTiles.map(t => t.id)).not.toContain('chessableQueue');
  });

  it('shows the chessable-queue tile for admins', () => {
    expect(setup({ isAdmin: true }).component.visibleTiles.map(t => t.id)).toContain('chessableQueue');
  });

  it('toggling a tile hides it (outside edit mode) and persists', () => {
    const { component } = setup();
    const puzzles = component.tiles.find(t => t.id === 'puzzles')!;
    component.toggle(puzzles);
    expect(component.isEnabled(puzzles)).toBeFalse();
    expect(component.visibleTiles.map(t => t.id)).not.toContain('puzzles');
    expect(JSON.parse(localStorage.getItem('rookhub_dashboard_layout')!).hidden).toContain('puzzles');
  });

  it('edit mode still shows hidden tiles so they can be re-enabled', () => {
    const { component } = setup();
    const friends = component.tiles.find(t => t.id === 'friends')!;
    component.toggle(friends);
    component.editing = true;
    expect(component.visibleTiles.map(t => t.id)).toContain('friends');
  });

  it('drag-drop reorders the tiles and persists the new order', () => {
    const { component } = setup();
    component.editing = true;
    expect(component.visibleTiles[0].id).toBe('puzzles');
    component.drop({ previousIndex: 0, currentIndex: 2 } as any);
    expect(component.visibleTiles[2].id).toBe('puzzles');
    const savedOrder: string[] = JSON.parse(localStorage.getItem('rookhub_dashboard_layout')!).order;
    expect(savedOrder.indexOf('puzzles')).toBe(2);
  });

  it('reset restores the default order and clears hidden tiles', () => {
    const { component } = setup();
    const puzzles = component.tiles.find(t => t.id === 'puzzles')!;
    component.toggle(puzzles);
    component.drop({ previousIndex: 0, currentIndex: 3 } as any);
    component.resetLayout();
    expect(component.visibleTiles[0].id).toBe('puzzles');
    expect(component.isEnabled(puzzles)).toBeTrue();
    expect(localStorage.getItem('rookhub_dashboard_layout')).toBeNull();
  });

  it('appends newly introduced tiles that are missing from a stored layout', () => {
    // Alt gespeichertes Layout kennt nur 2 Kacheln → neue müssen hinten angehängt erscheinen.
    localStorage.setItem('rookhub_dashboard_layout', JSON.stringify({ order: ['friends', 'puzzles'], hidden: [] }));
    const { component } = setup();
    const ids = component.visibleTiles.map(t => t.id);
    expect(ids[0]).toBe('friends');
    expect(ids[1]).toBe('puzzles');
    expect(ids).toContain('stats'); // nicht im gespeicherten Layout → angehängt
  });
});
