import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
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

    component.save();

    expect(http.expectOne('/api/profile').request.body.email).toBe('');
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
