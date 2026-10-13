import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import {
  ProfileIdentity, ProfileIdentityFormComponent, emailAnchorChanged, normalizeEmail,
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
  let auth: { isImpersonating: boolean };

  beforeEach(() => {
    auth = { isImpersonating: false };
    profile = {
      username: 'kahalm', email: 'a@b.local', firstName: 'Peter', lastName: 'Oberschmid',
      displayName: null, fideId: null, chessResultsId: null,
    };
    TestBed.configureTestingModule({
      imports: [ProfileIdentityFormComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: auth },
      ],
    });
    fixture = TestBed.createComponent(ProfileIdentityFormComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('profile', profile);
    fixture.componentRef.setInput('savedEmail', 'a@b.local');
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('zeigt die sechs Felder', () => {
    const names = [...fixture.nativeElement.querySelectorAll('input')]
      .map((i: HTMLInputElement) => i.getAttribute('name'));
    expect(names).toEqual(
      ['firstName', 'lastName', 'displayName', 'email', 'fideId', 'chessResultsId']);
  });

  /**
   * Die E-Mail ist der Reset-Anker: der Server verlangt fuer einen WECHSEL das aktuelle Passwort
   * (sonst 403). Das Feld darf aber nur dann erscheinen — sonst muesste man fuer jede
   * Namenskorrektur sein Passwort tippen. Verglichen wird wie auf dem Server normalisiert.
   */
  it('erkennt einen E-Mail-Wechsel wie der Server (trim + klein, leer = entfernt)', () => {
    expect(normalizeEmail('  A@B.Local ')).toBe('a@b.local');
    expect(normalizeEmail('   ')).toBeNull();
    expect(emailAnchorChanged('A@B.local', ' a@b.LOCAL ')).toBeFalse();
    expect(emailAnchorChanged(null, '')).toBeFalse();
    expect(emailAnchorChanged('a@b.local', 'c@d.local')).toBeTrue();
    expect(emailAnchorChanged('a@b.local', '')).withContext('Entfernen').toBeTrue();
    expect(emailAnchorChanged(null, 'a@b.local')).withContext('Erst-Setzen').toBeTrue();
  });

  it('fragt bei unveraenderter E-Mail nicht nach dem Passwort, auch in anderer Schreibweise', () => {
    fixture.componentRef.setInput('savedEmail', ' A@B.LOCAL');
    fixture.detectChanges();

    expect(component.needsPassword).toBeFalse();
    expect(fixture.nativeElement.querySelector('input[name="currentPassword"]')).toBeNull();
  });

  it('zeigt das Passwortfeld bei geaenderter E-Mail und reicht die Eingabe nach oben', () => {
    profile.email = 'neu@b.local';
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[name="currentPassword"]') as HTMLInputElement;
    expect(input).withContext('Passwortfeld').toBeTruthy();
    expect(input.type).toBe('password');
    expect(input.getAttribute('autocomplete')).toBe('current-password');

    input.value = 'Secret123!';
    input.dispatchEvent(new Event('input'));
    expect(component.currentPassword()).toBe('Secret123!');

    // Zurueck auf die alte Adresse: kein Wechsel mehr, das Feld verschwindet wieder.
    profile.email = 'a@b.local';
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[name="currentPassword"]')).toBeNull();
  });

  it('fragt auch beim Entfernen der E-Mail nach dem Passwort', () => {
    profile.email = '';
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('input[name="currentPassword"]')).toBeTruthy();
  });

  /**
   * Unter Impersonation sperrt der Server die E-Mail ohnehin (eigene 403), und das Passwort des
   * Nutzers kennt der Admin nicht — ein Passwortfeld fuehrte ihn nur in die Irre.
   */
  it('zeigt unter Impersonation kein Passwortfeld', () => {
    auth.isImpersonating = true;
    profile.email = 'neu@b.local';
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('input[name="currentPassword"]')).toBeNull();
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

  // UI-Sweep z2 (z2-rk-profile-suche-gesperrt): gesperrter Knopf nennt den Grund sichtbar darunter.
  it('nennt unter dem gesperrten Such-Knopf, dass erst der Name fehlt', () => {
    fixture.componentRef.setInput('profile', { ...profile, firstName: '', lastName: '' });
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button.pif-search') as HTMLButtonElement;
    const hint = fixture.nativeElement.querySelector('.pif-search-hint') as HTMLElement;
    expect(button.disabled).toBeTrue();
    expect(hint.textContent!.trim()).toBe('profile.searchPlayerNeedsName');
  });

  it('stellt den Such-Knopf auf Feldhoehe und erklaert darunter, was er tut (UI-Sweep t-profile-search)', () => {
    const button = fixture.nativeElement.querySelector('button.pif-search') as HTMLElement;
    expect(button.getBoundingClientRect().height).toBe(56);
    const hint = fixture.nativeElement.querySelector('.pif-search-box .pif-search-hint') as HTMLElement;
    expect(hint.textContent!.trim()).toMatch(/searchPlayerHint|FIDE/);
    expect(button.getAttribute('aria-describedby')).toBe(hint.id);
  });

  /**
   * UI-Sweep a1 (a1-t-profil-feldabstaende): Felder ohne Hinweis trugen den festen Material-
   * Reserveplatz (~32 px Abstand), Felder mit Hinweis ~12 px. Jetzt alle dynamic, Abstand 16 px.
   */
  it('setzt alle Felder auf dynamische Hinweisgroesse und haelt den Abstand einheitlich', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '360px';
    fixture.detectChanges();
    const fields = [...host.querySelectorAll('mat-form-field')] as HTMLElement[];
    expect(fields.length).toBe(6);
    for (const f of fields) {
      expect(f.querySelector('.mat-mdc-form-field-subscript-dynamic-size')).withContext(f.textContent!).not.toBeNull();
    }
    // Vorname → Nachname (ohne Hinweis) stehen am Handy untereinander mit 16 px Luft.
    const [first, last] = fields.map(f => f.querySelector('.mat-mdc-text-field-wrapper')!.getBoundingClientRect());
    expect(last.top - first.bottom).toBeCloseTo(16, 0);
    // FIDE → ChessResults ebenso.
    const fide = fields[4].querySelector('.mat-mdc-text-field-wrapper')!.getBoundingClientRect();
    const cr = fields[5].querySelector('.mat-mdc-text-field-wrapper')!.getBoundingClientRect();
    expect(cr.top - fide.bottom).toBeCloseTo(16, 0);
    // Hinweis unter „Spieler suchen“ eingerueckt wie die mat-hints (16 px).
    const hint = host.querySelector('.pif-search-hint') as HTMLElement;
    expect(getComputedStyle(hint).paddingLeft).toBe('16px');
  });

  /**
   * UI-Sweep a1 (a1-rk-profil-suche-falz): in der 600-px-Karte des RookHub-Profils bricht der
   * Suchblock um — dann Knopf (40 px) und Hinweis NEBENeinander ueber die volle Breite statt eines
   * schmalen Blocks mit dreizeiligem Hinweis.
   */
  it('stellt Knopf und Hinweis in mittlerer Breite nebeneinander', () => {
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '568px';
    fixture.detectChanges();
    const button = host.querySelector('button.pif-search') as HTMLElement;
    const hint = host.querySelector('.pif-search-hint') as HTMLElement;
    const b = button.getBoundingClientRect();
    const h = hint.getBoundingClientRect();
    expect(b.height).toBe(40);
    expect(h.left).toBeGreaterThanOrEqual(b.right);           // rechts neben dem Knopf
    expect(h.top).toBeLessThan(b.bottom);                     // in derselben Zeile
    const box = host.querySelector('.pif-search-box') as HTMLElement;
    expect(box.getBoundingClientRect().width).toBeCloseTo(568, 0);
    expect(box.getBoundingClientRect().height).toBeLessThanOrEqual(48);
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
