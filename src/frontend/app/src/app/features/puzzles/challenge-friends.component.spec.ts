import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { Friend } from '../../core/models';

describe('ChallengeFriendsComponent', () => {
  let httpMock: HttpTestingController;

  const PENDING_URL = '/api/challenges/outgoing/pending-counts';

  function create(source: 'standard' | 'book' = 'standard') {
    const fixture = TestBed.createComponent(ChallengeFriendsComponent);
    fixture.componentInstance.puzzleId = 42;
    fixture.componentInstance.source = source;
    return fixture;
  }

  const friends: Friend[] = [
    { friendshipId: 1, userId: 7, username: 'a', displayName: null, chessComUsername: null, lichessUsername: null, fideId: null, chessResultsId: null },
    { friendshipId: 2, userId: 8, username: 'b', displayName: null, chessComUsername: null, lichessUsername: null, fideId: null, chessResultsId: null },
  ];

  /** loadFriends() feuert zwei Requests: Freundesliste + Offene-Challenges-Zähler. Beide beantworten. */
  function flushLoad(counts: Record<number, number> = {}) {
    httpMock.expectOne('/api/friends').flush(friends);
    httpMock.expectOne(PENDING_URL).flush(counts);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ChallengeFriendsComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads friends lazily only once', () => {
    const c = create().componentInstance;
    c.loadFriends();
    flushLoad();
    c.loadFriends(); // zweiter Aufruf darf keinen weiteren Request feuern
    httpMock.expectNone('/api/friends');
    httpMock.expectNone(PENDING_URL);
    expect(c.friends.length).toBe(2);
  });

  it('toggleAll selects and clears all friends', () => {
    const c = create().componentInstance;
    c.loadFriends();
    flushLoad();

    c.toggleAll(true);
    expect(c.selected.size).toBe(2);
    expect(c.allSelected).toBeTrue();

    c.toggleAll(false);
    expect(c.selected.size).toBe(0);
  });

  it('someSelected is true for a partial selection', () => {
    const c = create().componentInstance;
    c.loadFriends();
    flushLoad();

    c.toggle(7, true);
    expect(c.someSelected).toBeTrue();
    expect(c.allSelected).toBeFalse();
  });

  it('loads the per-friend count of my still-open challenges', () => {
    const c = create().componentInstance;
    c.loadFriends();
    httpMock.expectOne('/api/friends').flush(friends);
    httpMock.expectOne(PENDING_URL).flush({ 7: 3 });

    expect(c.pendingCounts[7]).toBe(3);
    expect(c.pendingCounts[8]).toBeUndefined(); // kein offener Rückstand → keine Klammer
  });

  it('send() posts the selected ids with the given source and refreshes the counts', () => {
    const c = create('book').componentInstance;
    c.loadFriends();
    flushLoad({ 7: 1 });

    c.toggle(7, true);
    c.toggle(8, true);
    c.send();

    const req = httpMock.expectOne('/api/challenges');
    expect(req.request.body).toEqual({ toUserIds: [7, 8], puzzleId: 42, source: 'book' });
    req.flush({ sent: 2, skipped: [] });

    // Nach dem Senden werden die Zähler neu geladen (die frisch verschickten sind nun offen).
    httpMock.expectOne(PENDING_URL).flush({ 7: 2, 8: 1 });
    expect(c.pendingCounts[8]).toBe(1);

    expect(c.selected.size).toBe(0);
    expect(c.sending).toBeFalse();
  });

  it('onMenuOpened emits opened (so the host can stop auto-advance) and loads friends', () => {
    const c = create().componentInstance;
    let emitted = false;
    c.opened.subscribe(() => emitted = true);
    c.onMenuOpened();
    flushLoad();
    expect(emitted).toBeTrue();
  });

  it('send() does nothing without a selection', () => {
    const c = create().componentInstance;
    c.send();
    httpMock.expectNone('/api/challenges');
  });
});
