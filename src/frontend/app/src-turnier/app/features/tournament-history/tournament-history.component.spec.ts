import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentHistoryComponent } from './tournament-history.component';
import { HistoryFriend, PlayerHistory, PlayerHistoryEntry } from './tournament-history.model';

function played(over: Partial<PlayerHistoryEntry> = {}): PlayerHistoryEntry {
  return {
    chessResultsId: '1107064', name: 'Schach Tirol Open 2025', endDate: '2025-08-30',
    rank: 56, playerCount: 56, rounds: 9, points: 1.5, performanceRating: 1740,
    ratingChange: -51.6, ratingBefore: 1923, hasResult: true, cardFetched: true,
    speed: 'standard', ...over,
  };
}

function history(over: Partial<PlayerHistory> = {}): PlayerHistory {
  return { userId: 1, displayName: 'Ich', status: 'ok', entries: [played()], pending: 0, ...over };
}

/**
 * Der Turnierverlauf. Zwei Dinge sind hier wesentlich und deshalb einzeln geprueft: die Ansicht
 * laedt sich selbst nach, solange Ergebnisse fehlen (der Server holt sie einzeln im Hintergrund),
 * und der eigene Verlauf ist bei jeder Auswahl dabei — „nur Freunde" waere eine Ansicht, in der
 * man sich selbst sucht.
 */
describe('TournamentHistoryComponent', () => {
  let fixture: ComponentFixture<TournamentHistoryComponent>;
  let component: TournamentHistoryComponent;
  let http: HttpTestingController;

  beforeEach(() => localStorage.removeItem(TournamentHistoryComponent.ViewKey));
  afterEach(() => {
    localStorage.removeItem(TournamentHistoryComponent.ViewKey);
    TestBed.resetTestingModule();
  });

  async function setup(friends: HistoryFriend[] = []) {
    await TestBed.configureTestingModule({
      imports: [TournamentHistoryComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    // Die Auswahl haengt an der eigenen Kennung — ohne angemeldetes Konto gaebe es keine.
    const auth = TestBed.inject(AuthService);
    spyOnProperty(auth, 'currentUser', 'get').and.returnValue({
      token: 't', username: 'ich', userId: 1, isAdmin: false,
    });

    fixture = TestBed.createComponent(TournamentHistoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    // Die Freundesliste zuerst: ist „alle Freunde" die gemerkte Auswahl, laedt der Verlauf
    // erst DANACH — er muss ja wissen, wen er meint.
    http.expectOne('/api/tournament-history/friends').flush(friends);
    return http.expectOne(r => r.url === '/api/tournament-history');
  }

  it('lädt zuerst nur den eigenen Verlauf', async () => {
    const req = await setup();

    expect(req.request.params.get('userIds')).toBe('1');
    req.flush([history()]);
    expect(component.histories().length).toBe(1);
    expect(component.loading()).toBeFalse();
    http.verify();
  });

  /**
   * Der eigene Verlauf ist IMMER dabei. „Nur Freunde" waere eine Ansicht, in der man sich selbst
   * sucht — und der Vergleich ist der Zweck der Umschaltung.
   */
  it('nimmt bei „alle Freunde" den eigenen Verlauf mit', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);
    first.flush([history()]);

    component.onWhoseChange('all');

    const req = http.expectOne(r => r.url === '/api/tournament-history');
    expect(req.request.params.get('userIds')).toBe('1,7');
    req.flush([history(), history({ userId: 7, displayName: 'Freund' })]);
    http.verify();
  });

  it('fragt bei einzelner Auswahl nur die gewählten Freunde', async () => {
    const first = await setup([
      { userId: 7, displayName: 'A', exact: true, hasName: true },
      { userId: 8, displayName: 'B', exact: false, hasName: true },
    ]);
    first.flush([history()]);

    component.onWhoseChange('pick');
    http.expectOne(r => r.url === '/api/tournament-history').flush([history()]);
    component.onPickedChange([8]);

    const req = http.expectOne(r => r.url === '/api/tournament-history');
    expect(req.request.params.get('userIds')).toBe('1,8');
    req.flush([history()]);
  });

  /**
   * Der Server holt die Ergebnisse einzeln im Hintergrund. Ohne Nachfragen saehe man eine halbe
   * Tabelle und hielte sie fuer endgueltig.
   */
  it('fragt nach, solange Ergebnisse fehlen', async () => {
    const req = await setup();
    req.flush([history({ pending: 3 })]);

    expect(component.pending()).toBe(3);

    // Der Poll laeuft entprellt — abwarten, dann muss eine zweite Anfrage vorliegen.
    await new Promise(resolve => setTimeout(resolve, 4200));
    http.expectOne(r => r.url === '/api/tournament-history').flush([history({ pending: 0 })]);

    expect(component.pending()).toBe(0);
    http.verify();
  });

  it('fragt nicht nach, wenn alles da ist', async () => {
    const req = await setup();
    req.flush([history({ pending: 0 })]);

    await new Promise(resolve => setTimeout(resolve, 4200));

    http.verify();   // keine zweite Anfrage
  });

  /**
   * Ein Grund ist besser als eine leere Tabelle: „trage deinen Namen ein" ist eine
   * Handlungsanweisung, „keine Turniere" waere eine Falschaussage.
   */
  it('nennt den Grund, wenn der Nachname fehlt', async () => {
    const req = await setup();
    req.flush([history({ status: 'noName', entries: [] })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.hint')).toBeTruthy();
  });

  /**
   * Kuenftige und gespielte Turniere sind verschiedene Arten von Zeile — „Platz 56 von 56" und
   * „noch nicht gespielt" gehoeren nicht in dieselbe Tabelle.
   */
  it('trennt kommende von gespielten Turnieren', async () => {
    const req = await setup();
    const future = new Date(Date.now() + 30 * 86400_000).toISOString().slice(0, 10);
    req.flush([history({
      entries: [
        played(),
        played({ chessResultsId: '2', name: 'Liga', endDate: future, rank: null, hasResult: false }),
      ],
    })]);

    const h = component.histories()[0];
    expect(component.past(h).map(e => e.chessResultsId)).toEqual(['1107064']);
    expect(component.upcoming(h).map(e => e.chessResultsId)).toEqual(['2']);
  });

  /**
   * Ein Verlauf ohne Summe laesst einen selbst zusammenzaehlen — und genau darum sieht man ihn
   * an. Gezaehlt werden nur die Turniere MIT Ergebnis.
   */
  it('mittelt die Performance JE Bedenkzeit-Klasse', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ performanceRating: 1740, speed: 'standard' }),
        played({ chessResultsId: '2', performanceRating: 1860, speed: 'standard' }),
        played({ chessResultsId: '3', performanceRating: 1600, speed: 'blitz' }),
        played({ chessResultsId: '4', hasResult: false, points: null, performanceRating: null }),
      ],
    })]);

    const summary = component.summary(component.histories()[0]);
    expect(summary.played).toBe(3);
    // Turnierschach und Blitz getrennt — 1900 im Blitz ist nicht 1900 im Turnierschach.
    expect(summary.speeds.map(s => [s.speed, s.played, s.performance]))
      .toEqual([['standard', 2, 1800], ['blitz', 1, 1600]]);
  });

  /** Eine Klasse ohne gewertete Performance bleibt mit ihrer Anzahl stehen. */
  it('unterscheidet „nicht gespielt" von „keine Wertung"', async () => {
    const req = await setup();
    req.flush([history({
      entries: [played({ performanceRating: null, speed: 'rapid' })],
    })]);

    const speeds = component.summary(component.histories()[0]).speeds;
    expect(speeds.length).toBe(1);
    expect(speeds[0].speed).toBe('rapid');
    expect(speeds[0].performance).toBeNull();
  });

  /**
   * Gespielte Turniere stehen nach JAHREN getrennt, neueste zuerst — eine durchlaufende Liste
   * beantwortet die Frage „wie lief die Saison" nicht.
   */
  it('gruppiert die gespielten Turniere nach Jahren, neueste zuerst', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ chessResultsId: '1', endDate: '2025-08-30', performanceRating: 1700 }),
        played({ chessResultsId: '2', endDate: '2024-05-10', performanceRating: 1600 }),
        played({ chessResultsId: '3', endDate: '2025-01-06', performanceRating: 1800 }),
      ],
    })]);

    const years = component.years(component.histories()[0]);
    expect(years.map(g => g.year)).toEqual(['2025', '2024']);
    expect(years[0].entries.length).toBe(2);
    // Je Jahr dieselbe Auswertung wie oben.
    expect(years[0].speeds[0].performance).toBe(1750);
    expect(years[1].speeds[0].performance).toBe(1600);
  });

  /**
   * Gemeldet als „ich habe Freunde, kann aber keine auswaehlen": wer Freunde OHNE Namen im Profil
   * hat, bekam eine leere Auswahl und keinen Grund dafuer. Sie stehen jetzt da — nur nicht
   * auswaehlbar.
   */
  it('führt Freunde ohne Namen im Profil auf, aber nicht als Auswahl', async () => {
    const req = await setup([
      { userId: 7, displayName: 'Mit Name', exact: true, hasName: true },
      { userId: 8, displayName: 'Ohne Name', exact: false, hasName: false },
    ]);
    req.flush([history()]);

    expect(component.friends().length).toBe(2);
    expect(component.selectableFriends().map(f => f.userId)).toEqual([7]);
  });

  /** „Alle Freunde" darf nur die meinen, bei denen es etwas zu holen gibt. */
  it('nimmt bei „alle Freunde" nur die mit Namen', async () => {
    const req = await setup([
      { userId: 7, displayName: 'Mit Name', exact: true, hasName: true },
      { userId: 8, displayName: 'Ohne Name', exact: false, hasName: false },
    ]);
    req.flush([history()]);

    component.onWhoseChange('all');
    const call = http.expectOne(r => r.url === '/api/tournament-history');
    expect(call.request.params.get('userIds')).toBe('1,7');
    call.flush([history()]);
  });

  /**
   * Ein fehlendes Ergebnis hat ZWEI Ursachen, und sie verlangen Verschiedenes vom Leser: „wird
   * gerade geholt" heisst warten, „chess-results fuehrt hier keines" heisst, dass Warten nichts
   * bringt. Vorher stand in beiden Faellen „noch kein Ergebnis".
   */
  it('unterscheidet „wird geholt" von „es gibt hier keines"', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ chessResultsId: '9', hasResult: false, cardFetched: false, points: null, performanceRating: null }),
        played({ chessResultsId: '8', hasResult: false, cardFetched: true, points: null, performanceRating: null }),
      ],
      pending: 1,
    })]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('turnier.history.fetchingResult');
    expect(text).toContain('turnier.history.noSingleResult');
  });

  /**
   * Ein Klick fuehrt auf das TURNIER, nicht ins Verzeichnis. Vorher ging er auf
   * `/tournaments/calendar/{id}` und landete bei der Mehrheit der Verlaufs-Eintraege auf „steht
   * (noch) nicht im Verzeichnis" — und das heilt nicht: der naechtliche Sweep liest nur 30 Tage
   * zurueck, ein 2024 gespieltes Turnier steht dort NIE.
   */
  it('führt bei einem schon geholten Turnier direkt zu dessen Seite', async () => {
    const req = await setup();
    req.flush([history()]);
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    component.open(played());

    http.expectOne('/api/tournaments/1107064').flush({ id: 42, chessResultsId: '1107064', name: 'X' });

    expect(navigate).toHaveBeenCalledWith(['/tournaments', 42]);
    expect(component.opening()).toBeNull();
  });

  /** Ist es NICHT geholt, wird der Holen-Auftrag eingereiht — der einzige Weg dorthin. */
  it('reiht das Holen ein, wenn das Turnier fehlt', async () => {
    const req = await setup();
    req.flush([history()]);

    component.open(played());

    http.expectOne('/api/tournaments/1107064')
      .flush('weg', { status: 404, statusText: 'Not Found' });
    const crawl = http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' });
    expect(crawl.request.body).toEqual({ chessResultsId: '1107064', jobType: 'Full' });
    crawl.flush({ id: 7, status: 'Pending' });

    expect(component.opening()).toBe('1107064');
  });

  /** Laesst sich der Auftrag nicht einreihen, wird das gesagt statt endlos gewartet. */
  it('meldet einen gescheiterten Holen-Auftrag', async () => {
    const req = await setup();
    req.flush([history()]);
    const warn = spyOn(TestBed.inject(SnackbarService), 'warn');

    component.open(played());
    http.expectOne('/api/tournaments/1107064')
      .flush('weg', { status: 404, statusText: 'Not Found' });
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' })
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(warn).toHaveBeenCalled();
    expect(component.opening()).toBeNull();
  });

  /**
   * Ueberholte Antworten duerfen die Tabelle nicht bestimmen: wer waehrend eines laufenden
   * Abrufs umschaltet, bekaeme sonst den Stand der alten Auswahl.
   */
  it('verwirft die Antwort einer überholten Auswahl', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);

    component.onWhoseChange('all');
    const second = http.expectOne(r => r.url === '/api/tournament-history');

    // Die ALTE Antwort trifft nach der neuen Auswahl ein.
    second.flush([history(), history({ userId: 7, displayName: 'Freund' })]);
    first.flush([history()]);

    expect(component.histories().length).toBe(2);
  });

  it('merkt die Auswahl über den Seitenwechsel hinweg', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);
    first.flush([history()]);
    component.onWhoseChange('all');
    http.expectOne(r => r.url === '/api/tournament-history').flush([history()]);

    TestBed.resetTestingModule();
    const again = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);

    expect(again.request.params.get('userIds')).toBe('1,7');
    again.flush([history()]);
  });
});
