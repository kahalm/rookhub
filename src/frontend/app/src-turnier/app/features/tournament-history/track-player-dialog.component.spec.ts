import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TrackPlayerDialogComponent, splitName } from './track-player-dialog.component';

/**
 * „Spieler verfolgen": suchen, einen Treffer waehlen — daraus wird ein Reiter im Turnierverlauf.
 *
 * <p>Zwei Dinge sind hier wesentlich: gespeichert wird die Schreibweise der QUELLE (der Verlauf
 * sucht spaeter genau mit diesen Woertern wieder), und der Eintrag entsteht IM Dialog — scheitert
 * es, bleibt er offen und sagt es.</p>
 */
describe('TrackPlayerDialogComponent', () => {
  let fixture: ComponentFixture<TrackPlayerDialogComponent>;
  let component: TrackPlayerDialogComponent;
  let http: HttpTestingController;
  let closed: unknown;

  beforeEach(async () => {
    closed = undefined;
    await TestBed.configureTestingModule({
      imports: [TrackPlayerDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: (v: unknown) => { closed = v; } } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TrackPlayerDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  /** Der Nachname traegt die Suche — chess-results kennt keine Suche ueber eine Nummer. */
  it('sucht erst ab zwei Zeichen im Nachnamen', () => {
    component.lastName = 'O';
    expect(component.searchable()).toBeFalse();
    component.search();
    http.verify();

    component.lastName = 'Ob';
    expect(component.searchable()).toBeTrue();
  });

  it('fragt beide Quellen ab und zeigt die Treffer', () => {
    component.lastName = 'Oberschmid';
    component.firstName = 'Patrik';
    component.search();

    const req = http.expectOne(r => r.url === '/api/profile/player-search');
    expect(req.request.params.get('lastName')).toBe('Oberschmid');
    expect(req.request.params.get('firstName')).toBe('Patrik');
    req.flush({
      chessResultsResults: [{ name: 'Oberschmid, Patrik', fideId: '1693034', chessResultsId: '144749' }],
      fideResults: [],
    });
    fixture.detectChanges();

    expect(component.searching()).toBeFalse();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelectorAll('.hit').length).toBe(1);
    expect(host.textContent).toContain('Oberschmid, Patrik');
  });

  /**
   * Gespeichert wird die Schreibweise der QUELLE, nicht die getippte: die Eingabe darf abgekuerzt
   * sein („berschmid" findet ueber die Platzhalter-Suche „Oberschmid"), und der Verlauf sucht
   * spaeter genau mit diesen Woertern wieder bei chess-results.
   */
  it('schickt Name und Kennungen des gewählten Treffers', () => {
    component.lastName = 'berschmid';
    component.search();
    http.expectOne(r => r.url === '/api/profile/player-search').flush({
      chessResultsResults: [{ name: 'Oberschmid, Patrik', fideId: '1693034', chessResultsId: '144749' }],
      fideResults: [],
    });

    component.choose({
      name: 'Oberschmid, Patrik', fideId: '1693034', chessResultsId: '144749',
      elo: null, country: null, title: null,
    });

    const post = http.expectOne({ method: 'POST', url: '/api/tournament-history/tracked' });
    expect(post.request.body).toEqual({
      lastName: 'Oberschmid', firstName: 'Patrik', fideId: '1693034',
      chessResultsId: '144749', displayName: 'Oberschmid, Patrik',
    });

    post.flush({
      id: 3, displayName: 'Oberschmid, Patrik', exact: true, lastName: 'Oberschmid',
      firstName: 'Patrik', fideId: '1693034', chessResultsId: '144749',
    });
    expect(closed).toEqual(jasmine.objectContaining({ id: 3 }));
  });

  /** Ein Scheitern schliesst den Dialog NICHT — sonst passiert danach sichtbar nichts. */
  it('bleibt offen und nennt den Grund, wenn der Deckel erreicht ist', () => {
    component.lastName = 'Zuviel';
    component.search();
    http.expectOne(r => r.url === '/api/profile/player-search')
      .flush({ chessResultsResults: [{ name: 'Zuviel, Wer', fideId: null, chessResultsId: null }], fideResults: [] });

    component.choose({ name: 'Zuviel, Wer', fideId: null, chessResultsId: null, elo: null, country: null, title: null });
    http.expectOne({ method: 'POST', url: '/api/tournament-history/tracked' })
      .flush({ message: 'At most 20 tracked players.', limit: 20 }, { status: 400, statusText: 'Bad Request' });

    expect(closed).toBeUndefined();
    expect(component.failed()).toBeTruthy();
    expect(component.saving()).toBeFalse();
  });

  describe('splitName', () => {
    it('zerlegt „Nachname, Vorname" der Quelle', () => {
      expect(splitName('Oberschmid, Patrik', 'getippt', 'auch')).toEqual({
        lastName: 'Oberschmid', firstName: 'Patrik',
      });
    });

    /** Ein Name ohne Komma ist der Nachname; das getippte Vornamensfeld bleibt gueltig. */
    it('nimmt einen Namen ohne Komma als Nachnamen', () => {
      expect(splitName('Magnus', 'Carlsen', 'Magnus')).toEqual({
        lastName: 'Magnus', firstName: 'Magnus',
      });
    });

    it('faellt auf das Getippte zurück, wenn die Quelle nichts hergibt', () => {
      expect(splitName('', 'Carlsen', 'Magnus')).toEqual({ lastName: 'Carlsen', firstName: 'Magnus' });
    });
  });
});
