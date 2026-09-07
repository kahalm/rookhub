import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { TurnierAdminComponent } from './turnier-admin.component';

/**
 * Die Turnierseite hat genau EINEN Admin-Weg: als ein Nutzer einsteigen. Geprueft wird, dass
 * dabei die BESTEHENDE Mechanik laeuft (Server-Token, gesicherte Admin-Anmeldung) und nicht eine
 * zweite daneben.
 */
describe('TurnierAdminComponent', () => {
  let fixture: ComponentFixture<TurnierAdminComponent>;
  let component: TurnierAdminComponent;
  let http: HttpTestingController;
  let auth: AuthService;

  const users = () => ({
    items: [
      { id: 7, username: 'spieler', email: 's@t.local', isAdmin: false, createdAt: '2026-01-01', groups: [] },
      { id: 8, username: 'chefin', email: 'c@t.local', isAdmin: true, createdAt: '2026-01-02', groups: [] },
    ],
    totalCount: 2, page: 1, pageSize: 50,
  });

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [TurnierAdminComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    fixture = TestBed.createComponent(TurnierAdminComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    auth.adoptSession({ token: jwt(), userId: 1, username: 'chef', isAdmin: true });
    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  function flushUsers() {
    const req = http.expectOne(r => r.url === '/api/admin/users');
    req.flush(users());
    fixture.detectChanges();
    return req;
  }

  it('lädt die Konten und zeigt je Zeile einen Einstieg', () => {
    flushUsers();

    expect(component.users().length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.ta-users li').length).toBe(2);
  });

  it('gibt den Suchbegriff mit', () => {
    flushUsers();
    component.search = 'spiel';

    component.load();

    const req = http.expectOne(r => r.url === '/api/admin/users');
    expect(req.request.params.get('search')).toBe('spiel');
    req.flush(users());
  });

  /**
   * Der Einstieg laeuft ueber das SERVER-Token (es traegt die Rollen des Ziels, nicht die des
   * Admins) — es darf niemals im Client zusammengebaut werden.
   */
  it('steigt mit dem Server-Token ein, sichert die Admin-Anmeldung und geht in den Kalender', () => {
    flushUsers();
    const navigate = spyOn(TestBed.inject(Router), 'navigate');

    component.impersonate(component.users()[0]);

    const req = http.expectOne('/api/admin/users/7/impersonate');
    expect(req.request.method).toBe('POST');
    req.flush({ token: jwt(), userId: 7, username: 'spieler', isAdmin: false });
    // Der Menue-Dienst frischt danach auf (Sichtbarkeit haengt am angemeldeten Konto); dessen
    // Abruf interessiert hier nicht und bleibt bewusst unbeantwortet.

    expect(auth.currentUser?.username).toBe('spieler');
    expect(auth.isImpersonating).toBeTrue();
    expect(auth.impersonatorUsername).toBeNull();   // liefert der Server, hier nicht gesetzt
    expect(navigate).toHaveBeenCalledWith(['/tournaments/calendar']);
    expect(component.busyId()).toBeNull();
  });

  /** Scheitert der Einstieg, bleibt der Admin angemeldet — kein halber Zustand. */
  it('bleibt bei einem Fehlschlag als Admin angemeldet', () => {
    flushUsers();

    component.impersonate(component.users()[0]);
    http.expectOne('/api/admin/users/7/impersonate')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(auth.currentUser?.username).toBe('chef');
    expect(auth.isImpersonating).toBeFalse();
    expect(component.busyId()).toBeNull();
  });
});

/** Ein Token, das noch eine Stunde gilt — `AuthService` wirft abgelaufene sonst sofort weg. */
function jwt(expSecondsFromNow = 3600): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expSecondsFromNow }));
  return `x.${payload}.y`;
}
