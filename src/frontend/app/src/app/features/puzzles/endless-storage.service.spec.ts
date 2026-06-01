import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EndlessStorageService, EndlessConfig } from './endless-storage.service';
import { AuthService } from '../../core/auth.service';

const CONFIG: EndlessConfig = {
  startElo: 1500, step: 25, themes: '', fasttrack: false, stockfishDepth: 8,
};

describe('EndlessStorageService highscore sync', () => {
  let svc: EndlessStorageService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: true } },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('never sends a highscore lower than the server value', () => {
    svc.loadFromServer().subscribe();
    http.expectOne(r => r.method === 'GET' && r.url.endsWith('/progress')).flush({
      progress: { ...CONFIG, highscore: 1500, updatedAt: '' },
      sessions: [],
    });

    // Lokaler Save mit niedrigerem Highscore -> darf den 1500er nicht unterbieten.
    svc.saveProgressToServer(CONFIG, 1200, null);

    const put = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/progress'));
    expect(put.request.body.highscore).toBe(1500);
    put.flush({});
  });
});
