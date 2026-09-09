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

  /**
   * Lupe und Spinner gehoeren in den ICON-Slot des Knopfs (18 px, Aussenabstand), nicht in den
   * Text-Slot — dort war die Lupe 24 px gross, klebte am Text, und der Knopf sprang beim Suchen
   * in der Breite. MatButton projiziert einen @if-Zweig nur dann in den Icon-Slot, wenn er genau
   * EIN Wurzelelement hat; Icon UND Text im selben Zweig landen komplett im Text-Slot.
   */
  it('projiziert Lupe und Spinner in den Icon-Slot des Such-Knopfs', () => {
    const button = fixture.nativeElement.querySelector('button.pif-search') as HTMLElement;
    const label = () => button.querySelector('.mdc-button__label') as HTMLElement;

    expect(button.querySelector(':scope > mat-icon')).withContext('Lupe direkt im Knopf').toBeTruthy();
    expect(label().querySelector('mat-icon')).withContext('Lupe nicht im Text-Slot').toBeNull();
    expect(label().textContent).toMatch(/searchPlayer|Spieler suchen|Search player/);

    component.searching = true;
    fixture.detectChanges();

    expect(button.querySelector(':scope > mat-icon')).toBeNull();
    expect(button.querySelector(':scope > mat-spinner')).withContext('Spinner direkt im Knopf').toBeTruthy();
    expect(label().querySelector('mat-spinner')).withContext('Spinner nicht im Text-Slot').toBeNull();
    // Der Text bleibt in beiden Zustaenden stehen — deshalb springt der Knopf nicht in der Breite.
    expect(label().textContent).toMatch(/searchPlayer|Spieler suchen|Search player/);
  });

  /**
   * Handybreite (360 px): der Pfeil rechts in den Trefferzeilen darf nicht schrumpfen (bei Zeilen
   * mit CR- UND FIDE-Kennung war er nur noch ein 12-px-Strich), und viele Treffer rollen in EINEM
   * eigenen Bereich, statt das Formular drei Bildschirmhoehen nach unten zu schieben.
   * Messung wie in stats.component.spec.ts: Host auf Handybreite, dann DOM ausmessen.
   */
  it('hält bei Handybreite den Pfeil der Trefferzeilen und rollt viele Treffer im eigenen Bereich', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '360px';
    const hit = (i: number) => ({
      name: `Oberschmid-Mustermann, Peter Alexander ${i}`, chessResultsId: `14474${i}`,
      fideId: `169303${i}`, elo: 1923, country: 'AUT', title: 'GM',
    });
    component.results = {
      chessResultsResults: Array.from({ length: 30 }, (_, i) => hit(i)),
      fideResults: Array.from({ length: 30 }, (_, i) => hit(i)),
    };
    fixture.detectChanges();

    const arrows = [...host.querySelectorAll('.pif-hit mat-icon')] as HTMLElement[];
    expect(arrows.length).toBe(60);
    for (const arrow of arrows) {
      expect(arrow.getBoundingClientRect().width).withContext('Pfeil in voller Breite').toBeCloseTo(24, 0);
    }

    const results = host.querySelector('.pif-results') as HTMLElement;
    expect(getComputedStyle(results).overflowY).toBe('auto');
    expect(results.scrollHeight).withContext('Liste rollt im eigenen Bereich')
      .toBeGreaterThan(results.clientHeight);
  });
});
