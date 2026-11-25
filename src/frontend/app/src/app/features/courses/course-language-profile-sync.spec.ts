import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { OfflineService } from '../../core/offline.service';
import { PreferencesService } from '../../core/preferences.service';
import { COURSE_LANG_KEY, COURSE_LANG_PROFILE_WAIT_MS, CourseLanguageService } from './course-language.service';
import { CourseService } from './course.service';

/**
 * Sprachwahl je Kurs im Profil (1.015.1): der lokale Schlüssel wird beim Abmelden geräumt (er verrät die Kurse) —
 * nach dem Anmelden kommt die Wahl aus dem Profil zurück, auf jedem Gerät. Gäste bleiben lokal.
 */
describe('CourseLanguageService — Wahl im Profil', () => {
  let auth: { isLoggedIn: boolean; currentUser: { userId: number } | null };
  let http: HttpTestingController;
  let prefs: PreferencesService;

  function setup(loggedIn: boolean): CourseLanguageService {
    auth = { isLoggedIn: loggedIn, currentUser: loggedIn ? { userId: 3 } : null };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: TranslateService, useValue: { currentLang: () => 'de', getFallbackLang: () => 'en' } },
        { provide: CourseService, useValue: {} },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    prefs = TestBed.inject(PreferencesService);
    return TestBed.inject(CourseLanguageService);
  }

  function puts(): Record<string, unknown>[] {
    return http.match(r => r.method === 'PUT' && r.url === '/api/profile').map(r => { const b = r.request.body; r.flush({}); return b; });
  }

  beforeEach(() => localStorage.removeItem(COURSE_LANG_KEY));
  afterEach(() => { localStorage.removeItem(COURSE_LANG_KEY); http.verify(); });

  it('Profil gewinnt — bei Konflikt der jüngere; nur Neueres geht hoch', () => {
    localStorage.setItem(COURSE_LANG_KEY, JSON.stringify({ c: { b1: 'en', b2: 'fr', fx: 'it' }, f: {}, t: { b1: 100, b2: 900 } }));
    const svc = setup(true);
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({ courseLanguages: { b1: { lang: 'de', at: 500 }, b2: { lang: 'en', at: 200 }, b3: { lang: 'da', at: 1 } } });
    expect(svc.choice({ bookId: 1 })).toBe('de');
    expect(svc.choice({ bookId: 2 })).toBe('fr');
    expect(svc.choice({ bookId: 3 })).toBe('da');
    expect(puts()).toEqual([{ courseLanguages: { b2: { lang: 'fr', at: 900 } } }]);   // f-Schlüssel bleibt am Gerät
  });

  it('lädt alte Gerätewahlen (ohne Zeitstempel) ins leere Profil hoch', () => {
    localStorage.setItem(COURSE_LANG_KEY, JSON.stringify({ c: { b9: 'en' }, f: {} }));
    setup(true);
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({});
    expect(puts()).toEqual([{ courseLanguages: { b9: { lang: 'en', at: 1 } } }]);
  });

  it('kein Rückfall auf die Vorgabesprache, solange das Profil lädt (ready wartet)', async () => {
    const svc = setup(true);
    let fertig = false;
    const ready = firstValueFrom(svc.ready()).then(() => fertig = true);
    await Promise.resolve();
    expect(fertig).toBeFalse();
    http.expectOne('/api/profile').flush({ courseLanguages: { b5: { lang: 'en', at: 7 } } });
    await ready;
    expect(svc.requestLang({ bookId: 5 })).toBe('en');   // nicht 'de' (Oberfläche)
    // Danach sofort fertig, ohne neue Anfrage.
    await firstValueFrom(svc.ready());
    http.expectNone('/api/profile');
  });

  it('ready gibt nach der Wartezeit auf, wenn das Profil nicht kommt', fakeAsync(() => {
    const svc = setup(true);
    let fertig = false;
    svc.ready().subscribe(() => fertig = true);
    tick(COURSE_LANG_PROFILE_WAIT_MS);
    expect(fertig).toBeTrue();
    http.expectOne('/api/profile').flush({});
  }));

  it('neue Wahl geht lokal UND ins Profil; nach Abmelden und Anmelden kommt sie aus dem Profil zurück', () => {
    const svc = setup(true);
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({});
    svc.setChoice({ bookId: 4 }, 'EN');
    const sent = puts();
    expect(sent.length).toBe(1);
    const entry = (sent[0] as { courseLanguages: Record<string, { lang: string; at: number }> }).courseLanguages['b4'];
    expect(entry.lang).toBe('en');

    TestBed.inject(OfflineService).clearOnLogout();   // der lokale Schlüssel bleibt eine Konto-Spur
    expect(localStorage.getItem(COURSE_LANG_KEY)).toBeNull();
    // Neuer Seitenaufruf nach dem Anmelden: am Gerät nichts mehr.
    const fresh = new CourseLanguageService(TestBed.inject(TranslateService), undefined, prefs);
    expect(fresh.requestLang({ bookId: 4 })).toBe('de');
    prefs.loadFromServer();
    http.expectOne('/api/profile').flush({ courseLanguages: { b4: entry } });
    expect(fresh.requestLang({ bookId: 4 })).toBe('en');
    expect(svc.requestLang({ bookId: 4 })).toBe('en');
    expect(JSON.parse(localStorage.getItem(COURSE_LANG_KEY)!).c.b4).toBe('en');
  });

  it('Gast: nur lokal, ready sofort', async () => {
    const svc = setup(false);
    await firstValueFrom(svc.ready());
    svc.setChoice({ bookId: 4 }, 'fr');
    expect(svc.requestLang({ bookId: 4 })).toBe('fr');
    http.expectNone('/api/profile');
    http.expectNone(r => r.method === 'PUT');
  });
});
