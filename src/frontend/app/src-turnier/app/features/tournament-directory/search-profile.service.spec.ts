import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { SearchProfileService } from './search-profile.service';
import { SearchProfileInput } from './tournament-directory.model';

describe('SearchProfileService', () => {
  let service: SearchProfileService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SearchProfileService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const input: SearchProfileInput = {
    name: 'Zuhause', placeQuery: '5020 Salzburg', lat: 47.8, lon: 13.04, radiusKm: 100,
    federations: [], speeds: ['Blitz'], weekendOnly: false, minPlayers: null,
    notifyNew: true, sortOrder: 0,
  };

  it('listet, legt an, ändert und löscht über die erwarteten Routen', () => {
    service.list().subscribe();
    http.expectOne({ method: 'GET', url: '/api/tournament-search-profiles' }).flush([]);

    service.create(input).subscribe();
    const post = http.expectOne({ method: 'POST', url: '/api/tournament-search-profiles' });
    expect(post.request.body.name).toBe('Zuhause');
    post.flush({ ...input, id: 1 });

    service.update(1, input).subscribe();
    http.expectOne({ method: 'PUT', url: '/api/tournament-search-profiles/1' }).flush({ ...input, id: 1 });

    service.remove(1).subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/tournament-search-profiles/1' }).flush({});
  });
});
