import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FriendsService } from './friends.service';

describe('FriendsService', () => {
  let service: FriendsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [FriendsService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FriendsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('search URL-encodes the query', () => {
    service.search('a b&c').subscribe();
    const req = httpMock.expectOne('/api/friends/search?q=a%20b%26c');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('getFriends + getRequests hit the right routes', () => {
    service.getFriends().subscribe();
    const friends = httpMock.expectOne('/api/friends');
    expect(friends.request.method).toBe('GET');
    friends.flush([]);
    service.getRequests().subscribe();
    httpMock.expectOne('/api/friends/requests').flush([]);
  });

  it('sendRequest/accept/decline POST, remove DELETEs', () => {
    service.sendRequest(5).subscribe();
    const send = httpMock.expectOne('/api/friends/request/5');
    expect(send.request.method).toBe('POST');
    send.flush({});

    service.accept(7).subscribe();
    const accept = httpMock.expectOne('/api/friends/accept/7');
    expect(accept.request.method).toBe('POST');
    accept.flush({});

    service.decline(7).subscribe();
    httpMock.expectOne('/api/friends/decline/7').flush({});

    service.remove(9).subscribe();
    const remove = httpMock.expectOne('/api/friends/9');
    expect(remove.request.method).toBe('DELETE');
    remove.flush({});
  });

  it('getStats + getRevenge target the per-user routes', () => {
    service.getStats<unknown>(3).subscribe();
    const stats = httpMock.expectOne('/api/friends/3/stats');
    expect(stats.request.method).toBe('GET');
    stats.flush({});

    service.getRevenge<unknown>(3).subscribe();
    httpMock.expectOne('/api/friends/3/revenge').flush({});
  });
});
