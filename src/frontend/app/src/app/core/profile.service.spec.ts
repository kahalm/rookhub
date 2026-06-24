import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ProfileService } from './profile.service';

describe('ProfileService', () => {
  let service: ProfileService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ProfileService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProfileService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getProfile GETs /api/profile', () => {
    service.getProfile<unknown>().subscribe();
    expect(httpMock.expectOne('/api/profile').request.method).toBe('GET');
  });

  it('updateProfile PUTs the dto', () => {
    service.updateProfile<unknown>({ displayName: 'X' }).subscribe();
    const req = httpMock.expectOne('/api/profile');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ displayName: 'X' });
    req.flush({});
  });

  it('searchPlayer sends lastName only when firstName is omitted', () => {
    service.searchPlayer<unknown>('Mueller').subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/profile/player-search');
    expect(req.request.params.get('lastName')).toBe('Mueller');
    expect(req.request.params.has('firstName')).toBeFalse();
    req.flush({});
  });

  it('searchPlayer adds firstName when given', () => {
    service.searchPlayer<unknown>('Mueller', 'Anna').subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/profile/player-search');
    expect(req.request.params.get('firstName')).toBe('Anna');
    req.flush({});
  });
});
