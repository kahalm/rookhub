import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateModule } from '@ngx-translate/core';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { Friend } from '../../core/models';

describe('ChallengeFriendsComponent', () => {
  let httpMock: HttpTestingController;

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

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ChallengeFriendsComponent, TranslateModule.forRoot()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads friends lazily only once', () => {
    const c = create().componentInstance;
    c.loadFriends();
    httpMock.expectOne('/api/friends').flush(friends);
    c.loadFriends(); // zweiter Aufruf darf keinen weiteren Request feuern
    httpMock.expectNone('/api/friends');
    expect(c.friends.length).toBe(2);
  });

  it('toggleAll selects and clears all friends', () => {
    const c = create().componentInstance;
    c.loadFriends();
    httpMock.expectOne('/api/friends').flush(friends);

    c.toggleAll(true);
    expect(c.selected.size).toBe(2);
    expect(c.allSelected).toBeTrue();

    c.toggleAll(false);
    expect(c.selected.size).toBe(0);
  });

  it('someSelected is true for a partial selection', () => {
    const c = create().componentInstance;
    c.loadFriends();
    httpMock.expectOne('/api/friends').flush(friends);

    c.toggle(7, true);
    expect(c.someSelected).toBeTrue();
    expect(c.allSelected).toBeFalse();
  });

  it('send() posts the selected ids with the given source', () => {
    const c = create('book').componentInstance;
    c.loadFriends();
    httpMock.expectOne('/api/friends').flush(friends);

    c.toggle(7, true);
    c.toggle(8, true);
    c.send();

    const req = httpMock.expectOne('/api/challenges');
    expect(req.request.body).toEqual({ toUserIds: [7, 8], puzzleId: 42, source: 'book' });
    req.flush({ sent: 2, skipped: [] });

    expect(c.selected.size).toBe(0);
    expect(c.sending).toBeFalse();
  });

  it('onMenuOpened emits opened (so the host can stop auto-advance) and loads friends', () => {
    const c = create().componentInstance;
    let emitted = false;
    c.opened.subscribe(() => emitted = true);
    c.onMenuOpened();
    httpMock.expectOne('/api/friends').flush(friends);
    expect(emitted).toBeTrue();
  });

  it('send() does nothing without a selection', () => {
    const c = create().componentInstance;
    c.send();
    httpMock.expectNone('/api/challenges');
  });
});
