import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { AuthService } from './auth.service';
import { OfflineService } from './offline.service';
import { PreferencesService } from './preferences.service';
import { PROFILE_WAIT_MS, SolveModeService } from './solve-mode.service';

/**
 * Spielweise im Profil (1.015.1): die Frage kam immer wieder, weil die Wahl nur im localStorage lag und beim Abmelden
 * geräumt wurde. Jetzt gilt sie je Konto über alle Geräte; Gäste bleiben lokal.
 */
describe('SolveModeService — Wahl im Profil', () => {
  const KEY = 'rookhub_solve_modes';
  let auth: { isLoggedIn: boolean; currentUser: { userId: number } | null };
  let http: HttpTestingController;
  let dialogOpen: jasmine.Spy;
  let svc: SolveModeService;
  let prefs: PreferencesService;

  function setup(loggedIn: boolean): void {
    auth = { isLoggedIn: loggedIn, currentUser: loggedIn ? { userId: 7 } : null };
    dialogOpen = jasmine.createSpy('open')
      .and.returnValue({ afterClosed: () => of({ mode: 'easy', applyAll: true }) });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: MatDialog, useValue: { open: dialogOpen } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    prefs = TestBed.inject(PreferencesService);
    svc = TestBed.inject(SolveModeService);
  }

  function stored(): Record<string, { mode: string; at: number }> {
    return JSON.parse(localStorage.getItem(KEY) || '{}');
  }

  function puts(): Record<string, unknown>[] {
    return http.match(r => r.method === 'PUT' && r.url === '/api/profile').map(r => {
      const body = r.request.body;
      r.flush({});
      return body;
    });
  }

  beforeEach(() => localStorage.removeItem(KEY));
  afterEach(() => { localStorage.removeItem(KEY); http.verify(); });

  it('Profil gewinnt — bei Konflikt der jüngere Eintrag', () => {
    localStorage.setItem(KEY, JSON.stringify({ puzzles: { mode: 'training', at: 100 }, daily: { mode: 'easy', at: 900 } }));
    setup(true);
    let got: string | undefined;
    svc.ensure('puzzles').subscribe(m => got = m);   // am Gerät gibt es eine Wahl → kein Warten
    expect(got).toBe('training');
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({
      solveModes: { puzzles: { mode: 'easy', at: 500 }, daily: { mode: 'training', at: 200 }, endless: { mode: 'easy', at: 50 } },
    });
    expect(svc.get('puzzles')).toBe('easy');       // Profil jünger
    expect(svc.get('daily')).toBe('easy');         // Gerät jünger
    expect(svc.get('endless')).toBe('easy');       // nur im Profil
    expect(stored()['puzzles'].at).toBe(500);
    const body = puts();
    expect(body.length).toBe(1);
    expect(body[0]).toEqual({ solveModes: { daily: { mode: 'easy', at: 900 } } });   // nur das Neuere geht hoch
    expect(dialogOpen).not.toHaveBeenCalled();
  });

  it('lädt den Gerätestand beim ersten Mal ins leere Profil hoch — niemand wird neu gefragt', () => {
    localStorage.setItem(KEY, JSON.stringify({ '*': { mode: 'easy', at: 10 }, 'course:*': { mode: 'training', at: 11 }, 'course:4': { mode: 'easy', at: 12 } }));
    setup(true);
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({ solveModes: {} });
    const body = puts();
    expect(body).toEqual([{ solveModes: {
      '*': { mode: 'easy', at: 10 }, 'course:*': { mode: 'training', at: 11 }, 'course:4': { mode: 'easy', at: 12 },
    } }]);
    // Ein zweites Laden desselben Profils lädt nicht noch einmal hoch.
    svc.ensure('puzzles').subscribe();
    expect(dialogOpen).not.toHaveBeenCalled();
    expect(puts().length).toBe(0);
  });

  it('kein Dialog, solange das Profil lädt — danach gilt die Wahl aus dem Profil', () => {
    setup(true);   // neues Gerät: lokal nichts
    let got: string | undefined;
    svc.ensure('puzzles').subscribe(m => got = m);
    expect(dialogOpen).not.toHaveBeenCalled();
    expect(got).toBeUndefined();
    http.expectOne('/api/profile').flush({ solveModes: { '*': { mode: 'training', at: 5 } } });
    expect(got).toBe('training');
    expect(dialogOpen).not.toHaveBeenCalled();
    expect(stored()['*'].mode).toBe('training');
  });

  it('dieselbe Profil-Anfrage für mehrere Wartende; Profil ohne Wahl → erst dann der Dialog, Wahl geht ins Profil', () => {
    setup(true);
    svc.ensure('puzzles').subscribe();
    svc.ensure('endless').subscribe();
    http.expectOne('/api/profile').flush({ solveModes: null });
    expect(dialogOpen).toHaveBeenCalledTimes(1);   // „für alle übernehmen“ → endless fragt nicht mehr
    const body = puts();
    expect(body.length).toBe(1);
    const sent = (body[0] as { solveModes: Record<string, { mode: string }> }).solveModes;
    expect(Object.keys(sent).sort()).toEqual(['*', 'puzzles']);
    expect(sent['*'].mode).toBe('easy');
  });

  it('antwortet das Profil nicht, fragt es nach der Wartezeit mit dem Gerätestand', fakeAsync(() => {
    setup(true);
    let got: string | undefined;
    svc.ensure('puzzles').subscribe(m => got = m);
    tick(PROFILE_WAIT_MS - 1);
    expect(dialogOpen).not.toHaveBeenCalled();
    tick(1);
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(got).toBe('easy');
    puts();
    http.expectOne(r => r.method === 'GET' && r.url === '/api/profile').flush({});   // kommt zu spät — schadet nicht
    puts();
  }));

  it('kein Dialog nach Abmelden und erneutem Anmelden', () => {
    setup(true);
    svc.ensure('puzzles').subscribe();
    http.expectOne('/api/profile').flush({});
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    puts();
    // Abmelden: der Gerätespeicher wird wie im echten Abmelden geräumt — die Spielweise ist eine Gerätevorliebe.
    TestBed.inject(OfflineService).clearOnLogout();
    auth.isLoggedIn = false; auth.currentUser = null;
    expect(svc.get('puzzles')).toBe('easy');
    // Wieder anmelden (Nachlauf lädt das Profil neu).
    auth.isLoggedIn = true; auth.currentUser = { userId: 7 };
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({ solveModes: { '*': { mode: 'easy', at: Date.now() } } });
    puts();
    svc.ensure('puzzles').subscribe();
    svc.ensure('daily').subscribe();
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });

  it('Gast: nur lokal — kein Profil, kein Hochladen', () => {
    setup(false);
    svc.ensure('puzzles').subscribe();
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(stored()['puzzles'].mode).toBe('easy');
    http.expectNone('/api/profile');
    svc.set('daily', 'training');
    http.expectNone(r => r.method === 'PUT');
  });
});
