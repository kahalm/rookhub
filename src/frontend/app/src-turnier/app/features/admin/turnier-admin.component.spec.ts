import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { MenuService } from '@rh/core/menu.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { of } from 'rxjs';
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

  /**
   * Codereview UX-043: jeder Knopf hiess „Als dieser Nutzer einsteigen" — in der
   * Schaltflaechenliste eines Screenreaders bis zu 50-mal dasselbe ohne Bezug. Der vorgelesene
   * Name nennt jetzt das Konto; der sichtbare Text bleibt vorne (Sprachsteuerung).
   */
  it('nennt am Einstiegs-Knopf das Konto', () => {
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', {
      admin: { users: { impersonate: 'Als dieser Nutzer einsteigen' } },
      turnierAdmin: { impersonateAria: '{{action}}: {{name}}' },
    });
    translate.use('de');
    flushUsers();

    const labels = [...fixture.nativeElement.querySelectorAll('.ta-users li button')].map(
      (b: Element) => b.getAttribute('aria-label'));
    expect(labels).toEqual(['Als dieser Nutzer einsteigen: spieler', 'Als dieser Nutzer einsteigen: chefin']);
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

  /**
   * Ein Benutzername darf 50 Zeichen lang sein und hat keine Leerzeichen — auf 360 px war so ein
   * Name breiter als die Zeile (386 px gegen 296 px) und schob Karte und Seite horizontal. Der Test
   * misst genau das: schmaler Host, langer Name, die Zeile darf nicht ueber ihren Platz hinauslaufen.
   */
  it('bricht einen langen Benutzernamen um statt die Zeile zu verbreitern (360 px)', () => {
    const host = fixture.nativeElement as HTMLElement;
    // Kleines Android nachstellen, damit die Messung nicht vom Karma-Fenster abhaengt.
    host.style.width = '360px';
    host.style.overflowX = 'hidden';   // wie ein Viewport: was rauslaeuft, wuerde hier scrollen
    const name = 'schachfreund'.padEnd(50, 'w');   // 50 Zeichen = MaxLength des Benutzernamens
    http.expectOne(r => r.url === '/api/admin/users').flush({
      items: [{ id: 9, username: name, email: 'lang@t.local', isAdmin: false, createdAt: '2026-01-01', groups: [] }],
      totalCount: 1, page: 1, pageSize: 50,
    });
    fixture.detectChanges();

    const li = host.querySelector('.ta-users li') as HTMLElement | null;
    expect(li).withContext('Kontozeile gerendert').toBeTruthy();
    expect(li!.textContent).toContain(name);
    expect(li!.scrollWidth).toBeLessThanOrEqual(li!.clientWidth + 1);
  });

  /**
   * Codereview F1-013 (Nacharbeit): kann AuthService die Admin-Anmeldung nicht sichern (Speicher gesperrt/voll),
   * steigt er nicht ein und liefert false. Die Seite darf dann weder „Eingestiegen als X" melden noch in den
   * Kalender wechseln — die Sitzung ist weiter die des Admins, und was er dort „als Nutzer" anlegte (Suchprofile,
   * Favoriten), landete in seinem eigenen Konto.
   */
  it('meldet einen gescheiterten Einstieg, wenn die Admin-Anmeldung nicht gesichert werden kann', () => {
    flushUsers();
    const navigate = spyOn(TestBed.inject(Router), 'navigate');
    const impersonate = spyOn(auth, 'impersonate').and.returnValue(false);
    const snackbar = TestBed.inject(SnackbarService);
    const warn = spyOn(snackbar, 'warn');
    const info = spyOn(snackbar, 'info');
    const refresh = spyOn(TestBed.inject(MenuService), 'refresh');

    component.impersonate(component.users()[0]);
    http.expectOne('/api/admin/users/7/impersonate')
      .flush({ token: jwt(), userId: 7, username: 'spieler', isAdmin: false });

    expect(impersonate).toHaveBeenCalled();
    expect(warn).toHaveBeenCalledWith('admin.users.impersonateFailed');
    expect(info).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(auth.currentUser?.username).toBe('chef');
    expect(component.busyId()).toBeNull();
  });

  // ----- Codereview UX-078 -----

  /** Ins eigene Konto lehnt der Server ab (400) — ein Knopf dafuer fuehrte nur zu „fehlgeschlagen". */
  it('bietet für das eigene Konto keinen Einstieg an', () => {
    http.expectOne(r => r.url === '/api/admin/users').flush({
      items: [
        { id: 1, username: 'chef', email: 'chef@t.local', isAdmin: true, createdAt: '2026-01-01', groups: [] },
        ...users().items,
      ],
      totalCount: 3, page: 1, pageSize: 50,
    });
    fixture.detectChanges();

    const rows = [...fixture.nativeElement.querySelectorAll('.ta-users li')] as HTMLElement[];
    expect(rows.length).toBe(3);
    expect(rows[0].querySelector('button')).withContext('Knopf beim eigenen Konto').toBeNull();
    expect(rows[0].querySelector('.ta-self')).not.toBeNull();
    expect(rows[1].querySelector('button')).not.toBeNull();

    component.impersonate(component.users()[0]);
    http.expectNone('/api/admin/users/1/impersonate');
  });

  /** In ein Admin-Konto erst nach Rueckfrage — erlaubt (Support-Fall), aber mit fremden Admin-Rechten. */
  it('fragt vor dem Einstieg in ein Admin-Konto nach', () => {
    flushUsers();
    const confirm = TestBed.inject(ConfirmService);
    const ask = spyOn(confirm, 'ask').and.returnValue(of(false));
    const chefin = component.users()[1];

    component.impersonate(chefin);
    expect(ask).toHaveBeenCalledWith('turnierAdmin.confirmAdmin', { name: 'chefin' });
    http.expectNone('/api/admin/users/8/impersonate');

    ask.and.returnValue(of(true));
    component.impersonate(chefin);
    http.expectOne('/api/admin/users/8/impersonate');

    // Ein gewoehnliches Konto ohne Rueckfrage.
    ask.calls.reset();
    component.busyId.set(null);
    component.impersonate(component.users()[0]);
    expect(ask).not.toHaveBeenCalled();
  });

  /** Geholt wird eine Seite (50) — wer weiter hinten steht, fehlte bisher ohne jeden Hinweis. */
  it('sagt, wenn mehr Konten passen, als die Liste zeigt', () => {
    http.expectOne(r => r.url === '/api/admin/users').flush({ ...users(), totalCount: 135 });
    fixture.detectChanges();

    const more = fixture.nativeElement.querySelector('.ta-more') as HTMLElement | null;
    expect(more).withContext('Hinweis auf weitere Konten').not.toBeNull();
    expect(component.total()).toBe(135);
  });

  it('zeigt keinen Hinweis, wenn alle passenden Konten da sind', () => {
    flushUsers();
    expect(fixture.nativeElement.querySelector('.ta-more')).toBeNull();
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
