import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TurnierProfileComponent } from './turnier-profile.component';

/**
 * Die Profilseite der Turnierseite. Sie geht ueber denselben Endpunkt wie RookHub, pflegt aber
 * nur das, was hier gebraucht wird — der Name vor allem: der Turnierverlauf sucht auf
 * chess-results ueber den NAMEN, ohne Nachnamen gibt es keinen Verlauf.
 */
describe('TurnierProfileComponent', () => {
  let fixture: ComponentFixture<TurnierProfileComponent>;
  let component: TurnierProfileComponent;
  let http: HttpTestingController;

  /**
   * Als FUNKTION und nicht als geteilte Konstante: die Komponente uebernimmt das Antwortobjekt
   * unveraendert, und ein Test, der darin etwas aendert, faerbte sonst auf die uebrigen ab —
   * genau so gesehen.
   */
  const loaded = () => ({
    username: 'patrik', email: 'p@example.at', firstName: 'Patrik', lastName: 'Oberschmid',
    displayName: null, fideId: '1693034', chessResultsId: '144749',
  });

  function setup(profile: object = loaded()) {
    TestBed.configureTestingModule({
      imports: [TurnierProfileComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        // Kein Rest aus anderen Specs im localStorage soll hier eine Impersonation vortaeuschen.
        { provide: AuthService, useValue: { isImpersonating: false } },
      ],
    });
    fixture = TestBed.createComponent(TurnierProfileComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/profile').flush(profile);
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lädt das Profil und rendert die sechs pflegbaren Felder', () => {
    setup();

    expect(component.loading()).toBeFalse();
    expect(component.profile()?.lastName).toBe('Oberschmid');
    // Vorname, Nachname, Anzeigename, E-Mail, FIDE-ID, chess-results-ID.
    expect(fixture.nativeElement.querySelectorAll('input').length).toBe(6);
  });

  /**
   * Neben der Ueberschrift steht EIN Hilfe-Icon mit beiden Texten (Name + Kennungen), nicht
   * zwei gleiche ?-Icons nebeneinander: die sind nicht unterscheidbar, und auf dem Handy trifft
   * man zufaellig eines und sieht nur die halbe Erklaerung. Ohne geladene Uebersetzungen liefert
   * die Pipe den Schluessel selbst — daran laesst sich pruefen, dass beide Teile drin sind.
   */
  it('zeigt Name- und Kennungs-Hilfe in EINEM Hilfe-Icon', () => {
    setup();

    const hints = fixture.nativeElement.querySelectorAll('app-help-hint');
    expect(hints.length).toBe(1);
    const label: string = hints[0].querySelector('button').getAttribute('aria-label');
    expect(label).toContain('turnier.profile.nameHelp');
    expect(label).toContain('turnier.profile.identityHelp');
    expect(label).toContain('\n\n');
  });

  /**
   * Nur die Felder DIESER Seite gehen mit. Ein vollstaendiges Profil-Objekt zurueckzuschicken
   * hiesse, die Einstellungen aus RookHub (Brett, Offline-Speicher, Zugaenge) mit dem Stand von
   * hier zu ueberschreiben — und die kennt diese Seite nicht.
   */
  it('schickt beim Speichern nur die eigenen Felder', () => {
    setup();
    component.profile()!.lastName = 'Neu';

    component.save();

    const req = http.expectOne('/api/profile');
    expect(req.request.method).toBe('PUT');
    expect(Object.keys(req.request.body).sort()).toEqual(
      ['chessResultsId', 'displayName', 'email', 'fideId', 'firstName', 'lastName']);
    expect(req.request.body.lastName).toBe('Neu');
    req.flush({ ...loaded(), lastName: 'Neu' });

    expect(component.saving()).toBeFalse();
    http.verify();
  });

  /**
   * Eine geleerte E-Mail heisst „entfernen" und muss als LEERER String gehen — `null` bedeutet
   * fuer den Server „unveraendert lassen".
   */
  it('schickt eine geleerte E-Mail als leeren String', () => {
    setup();
    component.profile()!.email = null;
    component.currentPassword.set('Secret123!');   // Entfernen ist ein Wechsel des Reset-Ankers

    component.save();

    const body = http.expectOne('/api/profile').request.body;
    expect(body.email).toBe('');
    expect(body.currentPassword).toBe('Secret123!');
  });

  /**
   * Die E-Mail ist der Reset-Anker: der Server verlangt fuer einen WECHSEL das aktuelle Passwort
   * (sonst 403). Ohne Feld und eigene Meldung liess sie sich auf dieser Seite gar nicht mehr
   * aendern — das Speichern scheiterte stumm mit „Konnte nicht gespeichert werden".
   */
  describe('E-Mail-Wechsel', () => {
    const passwordInput = (): HTMLInputElement | null =>
      fixture.nativeElement.querySelector('input[name="currentPassword"]');

    /**
     * Ueber das DOM tippen, nicht das Modell direkt aendern: die Seite ist OnPush (Angular-22-
     * Vorgabe), erst das Eingabe-Ereignis markiert die Ansicht — genau wie beim Nutzer.
     */
    async function typeInto(name: string, value: string): Promise<void> {
      await fixture.whenStable();   // ngModel schreibt den geladenen Wert asynchron ins Feld
      const input = fixture.nativeElement.querySelector(`input[name="${name}"]`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    }

    it('zeigt das Passwortfeld erst bei geaenderter Adresse (Schreibweise zaehlt nicht)', async () => {
      setup();
      // type="email" streift Leerzeichen schon im Browser ab; die Schreibweise bleibt.
      await typeInto('email', 'P@Example.AT');
      expect(component.profile()!.email).toBe('P@Example.AT');
      expect(passwordInput()).toBeNull();

      await typeInto('email', 'neu@example.at');
      expect(passwordInput()).withContext('Passwortfeld').toBeTruthy();
      expect(fixture.nativeElement.querySelectorAll('input').length).toBe(7);
    });

    it('schickt das eingetippte Passwort mit und merkt sich danach die neue Adresse', async () => {
      setup();
      await typeInto('email', 'neu@example.at');
      await typeInto('currentPassword', 'Secret123!');
      expect(component.currentPassword()).withContext('Zwei-Wege-Bindung ueber das Formular').toBe('Secret123!');

      component.save();

      const req = http.expectOne('/api/profile');
      expect(req.request.body.email).toBe('neu@example.at');
      expect(req.request.body.currentPassword).toBe('Secret123!');
      req.flush({ ...loaded(), email: 'neu@example.at' });
      fixture.detectChanges();

      expect(component.savedEmail()).toBe('neu@example.at');
      expect(component.currentPassword()).toBe('');
      expect(passwordInput()).withContext('gespeichert = kein Wechsel mehr').toBeNull();
    });

    it('schickt einen Wechsel ohne Passwort gar nicht erst ab', () => {
      setup();
      const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
      component.profile()!.email = 'neu@example.at';

      component.save();

      http.expectNone('/api/profile');
      expect(warn).toHaveBeenCalledWith('profile.emailPasswordRequired');
      expect(component.saving()).toBeFalse();
    });

    it('meldet eine 403 beim Wechsel als falsches Passwort und leert das Feld', () => {
      setup();
      const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
      component.profile()!.email = 'neu@example.at';
      component.currentPassword.set('falsch');

      component.save();
      http.expectOne('/api/profile').flush(
        { message: 'Current password is incorrect.' }, { status: 403, statusText: 'Forbidden' });

      expect(warn).toHaveBeenCalledWith('profile.emailPasswordWrong');
      expect(component.currentPassword()).toBe('');
      expect(component.savedEmail()).toBe('p@example.at');
      expect(component.saving()).toBeFalse();
    });
  });

  it('bleibt bedienbar, wenn das Speichern scheitert', () => {
    setup();

    component.save();
    http.expectOne('/api/profile').flush({ message: 'nope' }, { status: 500, statusText: 'Error' });

    expect(component.saving()).toBeFalse();
    expect(component.profile()).not.toBeNull();
  });

  it('meldet einen Ladefehler statt eine leere Seite zu zeigen', () => {
    TestBed.configureTestingModule({
      imports: [TurnierProfileComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        // Kein Rest aus anderen Specs im localStorage soll hier eine Impersonation vortaeuschen.
        { provide: AuthService, useValue: { isImpersonating: false } },
      ],
    });
    fixture = TestBed.createComponent(TurnierProfileComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/profile').flush(null, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    expect(component.loading()).toBeFalse();
    expect(component.profile()).toBeNull();
    expect(fixture.nativeElement.querySelector('.muted')).toBeTruthy();
  });
});
