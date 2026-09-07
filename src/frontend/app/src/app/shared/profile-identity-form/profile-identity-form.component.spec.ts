import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import {
  ProfileIdentity, ProfileIdentityFormComponent,
} from './profile-identity-form.component';

/**
 * Das gemeinsame Identitaets-Formular beider Oberflaechen. Vorher standen dieselben sechs Felder
 * in ZWEI Komponenten getippt — und die Turnierseite hatte deshalb die Spielersuche nicht,
 * obwohl dort alles an den Kennungen haengt (der Verlauf sucht ueber den Namen, die Kennung
 * entscheidet bei Namensgleichheit).
 */
describe('ProfileIdentityFormComponent', () => {
  let fixture: ComponentFixture<ProfileIdentityFormComponent>;
  let component: ProfileIdentityFormComponent;
  let http: HttpTestingController;
  let profile: ProfileIdentity;

  beforeEach(() => {
    profile = {
      username: 'kahalm', email: 'a@b.local', firstName: 'Peter', lastName: 'Oberschmid',
      displayName: null, fideId: null, chessResultsId: null,
    };
    TestBed.configureTestingModule({
      imports: [ProfileIdentityFormComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    fixture = TestBed.createComponent(ProfileIdentityFormComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('profile', profile);
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('zeigt die sechs Felder', () => {
    const names = [...fixture.nativeElement.querySelectorAll('input')]
      .map((i: HTMLInputElement) => i.getAttribute('name'));
    expect(names).toEqual(
      ['firstName', 'lastName', 'displayName', 'email', 'fideId', 'chessResultsId']);
  });

  /** Die Suche laeuft ueber den NACHNAMEN — unter zwei Zeichen ist sie sinnlos. */
  it('sucht nicht ohne brauchbaren Nachnamen', () => {
    fixture.componentRef.setInput('profile', { ...profile, lastName: 'O' });
    fixture.detectChanges();

    expect(component.searchable).toBeFalse();
    component.searchPlayer();

    http.verify();
  });

  it('übernimmt einen einzelnen chess-results-Treffer samt FIDE-Kennung', () => {
    component.searchPlayer();

    const req = http.expectOne(r => r.url === '/api/profile/player-search');
    expect(req.request.params.get('lastName')).toBe('Oberschmid');
    expect(req.request.params.get('firstName')).toBe('Peter');
    req.flush({
      chessResultsResults: [{
        name: 'Oberschmid, Peter', chessResultsId: '144749', fideId: '1693034',
        elo: 1923, country: 'AUT', title: null,
      }],
      fideResults: [],
    });

    expect(profile.chessResultsId).toBe('144749');
    expect(profile.fideId).toBe('1693034');
  });

  /**
   * Ein einzelner FIDE-Treffer darf eine Kennung NICHT ueberschreiben, die schon vom
   * chess-results-Treffer kam — sonst setzt ein fremder Namensgleicher sie.
   */
  it('lässt eine vom chess-results-Treffer gelieferte FIDE-Kennung stehen', () => {
    component.searchPlayer();

    http.expectOne(r => r.url === '/api/profile/player-search').flush({
      chessResultsResults: [{
        name: 'Oberschmid, Peter', chessResultsId: '144749', fideId: '1693034',
        elo: null, country: null, title: null,
      }],
      fideResults: [{
        name: 'Oberschmid, Petra', chessResultsId: null, fideId: '9999999',
        elo: null, country: null, title: null,
      }],
    });

    expect(profile.fideId).toBe('1693034');
  });

  it('nimmt einen einzelnen FIDE-Treffer, wenn chess-results keine Kennung lieferte', () => {
    component.searchPlayer();

    http.expectOne(r => r.url === '/api/profile/player-search').flush({
      chessResultsResults: [],
      fideResults: [{
        name: 'Oberschmid, Peter', chessResultsId: null, fideId: '1693034',
        elo: null, country: null, title: null,
      }],
    });

    expect(profile.fideId).toBe('1693034');
    expect(profile.chessResultsId).toBeNull();
  });
});
