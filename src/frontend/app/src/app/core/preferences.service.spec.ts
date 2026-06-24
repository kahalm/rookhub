import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PreferencesService } from './preferences.service';
import { AuthService } from './auth.service';

describe('PreferencesService', () => {
  let httpMock: HttpTestingController;
  const auth = { isLoggedIn: false };

  function make(): PreferencesService {
    TestBed.configureTestingModule({
      providers: [
        PreferencesService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    return TestBed.inject(PreferencesService);
  }

  beforeEach(() => {
    localStorage.clear();
    auth.isLoggedIn = false;
  });

  afterEach(() => { httpMock?.verify(); localStorage.clear(); });

  it('uses defaults when localStorage is empty', () => {
    const svc = make();
    expect(svc.boardTheme).toBe('brown');
    expect(svc.pieceSet).toBe('cburnett');
    expect(svc.stockfishDepth).toBe(16);
    expect(svc.visualization).toBe(1);
    expect(svc.vizArrow).toBeTrue();
  });

  it('reads existing values from localStorage on construction', () => {
    localStorage.setItem('rookhub_board_theme', 'blue');
    localStorage.setItem('rookhub_visualization', '3');
    localStorage.setItem('rookhub_viz_arrow', 'false');
    const svc = make();
    expect(svc.boardTheme).toBe('blue');
    expect(svc.visualization).toBe(3);
    expect(svc.vizArrow).toBeFalse();
  });

  it('setVisualization clamps to 0..4 and persists', () => {
    const svc = make();
    svc.setVisualization(9);
    expect(svc.visualization).toBe(4);
    expect(localStorage.getItem('rookhub_visualization')).toBe('4');
    svc.setVisualization(-2);
    expect(svc.visualization).toBe(0);
  });

  it('setStockfishDepth clamps to 1..24 (logged out → no server call)', () => {
    const svc = make();
    svc.setStockfishDepth(99);
    expect(svc.stockfishDepth).toBe(24);
    svc.setStockfishDepth(0);
    expect(svc.stockfishDepth).toBe(1);
    httpMock.verify(); // kein PUT, da ausgeloggt
  });

  it('setBoardTheme sends a PUT only when logged in', () => {
    auth.isLoggedIn = true;
    const svc = make();
    svc.setBoardTheme('green');
    const req = httpMock.expectOne('/api/profile');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ boardTheme: 'green' });
    req.flush({});
    expect(svc.boardTheme).toBe('green');
    expect(localStorage.getItem('rookhub_board_theme')).toBe('green');
  });

  it('loadFromServer is a no-op when logged out', () => {
    const svc = make();
    svc.loadFromServer();
    httpMock.verify(); // kein GET
  });

  it('loadFromServer overwrites local values when logged in', () => {
    auth.isLoggedIn = true;
    const svc = make();
    svc.loadFromServer();
    httpMock.expectOne('/api/profile').flush({
      boardTheme: 'wood', pieceSet: 'merida', stockfishDepth: 50,
      puzzleDifficulty: 'hard', bookStockfishDepth: 8,
    });
    expect(svc.boardTheme).toBe('wood');
    expect(svc.pieceSet).toBe('merida');
    expect(svc.stockfishDepth).toBe(24);     // serverseitig 50 → geklemmt
    expect(svc.puzzleDifficulty).toBe('hard');
    expect(svc.bookStockfishDepth).toBe(8);
  });
});
