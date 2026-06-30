import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';
import { FriendsComponent } from './friends.component';
import { ChallengeService } from '../../core/challenge.service';
import { RevengeService } from '../../core/revenge.service';
import { SnackbarService } from '../../core/snackbar.service';
import { InAppNotificationService } from '../../core/in-app-notification.service';

describe('FriendsComponent search race', () => {
  let component: FriendsComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FriendsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ChallengeService, useValue: { getIncoming: () => of([]), getOutgoing: () => of([]) } },
        { provide: RevengeService, useValue: { getNotifications: () => of([]), markSeen: () => of(null) } },
        { provide: SnackbarService, useValue: { info: () => {}, success: () => {} } },
      ],
    });
    // Template entfernen — wir testen nur die Such-Logik der Klasse, nicht das Rendering.
    TestBed.overrideComponent(FriendsComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(FriendsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit
    httpMock = TestBed.inject(HttpTestingController);
    // ngOnInit-Loads abräumen (Challenges/Revenge sind gestubbt → kein HTTP).
    httpMock.expectOne('/api/friends').flush([]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([]);
  });

  afterEach(() => httpMock.verify());

  it('cancels an in-flight search when a newer one starts (latest wins)', () => {
    component.searchQuery = 'ab';
    component.search();
    const stale = httpMock.expectOne(r => r.url.endsWith('q=ab'));

    component.searchQuery = 'abc';
    component.search();
    const latest = httpMock.expectOne(r => r.url.endsWith('q=abc'));

    expect(stale.cancelled).toBeTrue();          // switchMap brach die alte Anfrage ab
    latest.flush([{ userId: 1, username: 'abc' } as any]);
    expect(component.searchResults.length).toBe(1);
    expect(component.searchResults[0].username).toBe('abc');
  });

  it('ignores queries shorter than 2 chars (no request)', () => {
    component.searchQuery = 'a';
    component.search();
    httpMock.expectNone(r => r.url.includes('/friends/search'));
    expect(component.searchResults).toEqual([]);
  });

  it('keeps openRevengeCount from the friends payload (drives the red revenge icon)', () => {
    component.loadData();
    httpMock.expectOne('/api/friends').flush([
      { friendshipId: 1, userId: 7, username: 'rival', displayName: null, openRevengeCount: 3 },
      { friendshipId: 2, userId: 8, username: 'calm', displayName: null, openRevengeCount: 0 },
    ]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([]);

    expect(component.friends.find(f => f.userId === 7)?.openRevengeCount).toBe(3);
    expect(component.friends.find(f => f.userId === 8)?.openRevengeCount).toBe(0);
  });
});

describe('FriendsComponent revenge notifications (flattened subscribe)', () => {
  function setup(notifications: any[]) {
    const markSeen = jasmine.createSpy('markSeen').and.returnValue(of(null));
    TestBed.configureTestingModule({
      imports: [FriendsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ChallengeService, useValue: { getIncoming: () => of([]), getOutgoing: () => of([]) } },
        { provide: RevengeService, useValue: { getNotifications: () => of(notifications), markSeen } },
        { provide: SnackbarService, useValue: { info: () => {}, success: () => {} } },
      ],
    });
    TestBed.overrideComponent(FriendsComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(FriendsComponent);
    fixture.detectChanges();
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/friends').flush([]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([]);
    return { component: fixture.componentInstance, markSeen };
  }

  it('marks notifications seen when at least one is unseen', () => {
    const { component, markSeen } = setup([{ id: 1, seen: false }]);
    expect(component.revengeNotifications.length).toBe(1);
    expect(markSeen).toHaveBeenCalledTimes(1);
  });

  it('does NOT mark seen when all notifications are already seen', () => {
    const { markSeen } = setup([{ id: 1, seen: true }]);
    expect(markSeen).not.toHaveBeenCalled();
  });
});

describe('FriendsComponent reactive reload on notification arrival', () => {
  it('quietly reloads the friends list when a notification arrives (no manual refresh)', () => {
    const arrived = new Subject<void>();
    TestBed.configureTestingModule({
      imports: [FriendsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ChallengeService, useValue: { getIncoming: () => of([]), getOutgoing: () => of([]) } },
        { provide: RevengeService, useValue: { getNotifications: () => of([]), markSeen: () => of(null) } },
        { provide: SnackbarService, useValue: { info: () => {}, success: () => {} } },
        { provide: InAppNotificationService, useValue: { arrived$: arrived.asObservable() } },
      ],
    });
    TestBed.overrideComponent(FriendsComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(FriendsComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit → erster (nicht-stiller) Load
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/friends').flush([{ friendshipId: 1 } as any]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([]);
    expect(component.loading).toBeFalse();

    // Benachrichtigung trifft ein (z. B. Anfrage angenommen) → stiller Reload, KEIN Spinner.
    arrived.next();
    expect(component.loading).toBeFalse();
    httpMock.expectOne('/api/friends').flush([{ friendshipId: 1 }, { friendshipId: 2 } as any]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([]);
    expect(component.friends.length).toBe(2);

    httpMock.verify();
  });
});

describe('FriendsComponent pending sent requests', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [FriendsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ChallengeService, useValue: { getIncoming: () => of([]), getOutgoing: () => of([]) } },
        { provide: RevengeService, useValue: { getNotifications: () => of([]), markSeen: () => of(null) } },
        { provide: SnackbarService, useValue: { info: () => {}, success: () => {} } },
      ],
    });
    TestBed.overrideComponent(FriendsComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(FriendsComponent);
    fixture.detectChanges();
    const httpMock = TestBed.inject(HttpTestingController);
    return { component: fixture.componentInstance, httpMock };
  }

  it('loads my pending sent requests on init', () => {
    const { component, httpMock } = setup();
    httpMock.expectOne('/api/friends').flush([]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([
      { friendshipId: 9, addresseeId: 2, addresseeUsername: 'taulajoe', addresseeDisplayName: null, createdAt: '2026-06-15T10:22:02Z' },
    ]);

    expect(component.sentRequests.length).toBe(1);
    expect(component.sentRequests[0].addresseeUsername).toBe('taulajoe');
    httpMock.verify();
  });

  it('withdrawRequest() DELETEs the friendship and reloads', () => {
    const { component, httpMock } = setup();
    httpMock.expectOne('/api/friends').flush([]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([
      { friendshipId: 9, addresseeId: 2, addresseeUsername: 'taulajoe', addresseeDisplayName: null, createdAt: '2026-06-15T10:22:02Z' },
    ]);

    component.withdrawRequest(9);
    const del = httpMock.expectOne('/api/friends/9');
    expect(del.request.method).toBe('DELETE');
    del.flush({});

    // Erfolg → loadData() lädt alle drei Listen neu.
    httpMock.expectOne('/api/friends').flush([]);
    httpMock.expectOne('/api/friends/requests').flush([]);
    httpMock.expectOne('/api/friends/requests/sent').flush([]);
    expect(component.sentRequests.length).toBe(0);
    httpMock.verify();
  });
});
