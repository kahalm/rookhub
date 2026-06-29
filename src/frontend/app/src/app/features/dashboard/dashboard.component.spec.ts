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
import { InAppNotificationService } from '../../core/in-app-notification.service';

describe('DashboardComponent friend-count reactivity', () => {
  let arrived: Subject<void>;
  let friends: unknown[];
  let component: DashboardComponent;

  beforeEach(() => {
    arrived = new Subject<void>();
    friends = [{}, {}]; // 2 Freunde initial
    const dashboardService = {
      getRepertoires: () => of([]),
      getSubscriptions: () => of([]),
      getFriends: () => of(friends),
      getPuzzleStats: () => of({ solved: 0, accuracy: 0, puzzleElo: 1500 }),
    };
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin: false } },
        { provide: DashboardService, useValue: dashboardService },
        { provide: ChessableService, useValue: { getActiveImportsAdmin: () => of([]) } },
        { provide: InAppNotificationService, useValue: { arrived$: arrived.asObservable() } },
      ],
    });
    TestBed.overrideComponent(DashboardComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit → initialer forkJoin
  });

  it('loads the initial friend count', () => {
    expect(component.friendCount).toBe(2);
  });

  it('refreshes the friend count when a notification arrives (friend accepted my invite)', () => {
    friends = [{}, {}, {}]; // ein Freund hat die Anfrage angenommen → 3
    arrived.next();
    expect(component.friendCount).toBe(3);
  });
});
