import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { TournamentHistoryComponent } from './tournament-history.component';
import { HistoryFriend, PlayerHistory, PlayerHistoryEntry } from './tournament-history.model';

function played(over: Partial<PlayerHistoryEntry> = {}): PlayerHistoryEntry {
  return {
    chessResultsId: '1107064', name: 'Schach Tirol Open 2025', endDate: '2025-08-30',
    rank: 56, playerCount: 56, rounds: 9, points: 1.5, performanceRating: 1740,
    ratingChange: -51.6, ratingBefore: 1923, hasResult: true, cardFetched: true,
    speed: 'standard', gamesPlayed: null, ...over,
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

    // Der EIGENE Verlauf laeuft sofort los; die Freundesliste fuellt daneben die Reiter.
    const own = http.expectOne(r => r.url === '/api/tournament-history');
    http.expectOne('/api/tournament-history/friends').flush(friends);
    return own;
  }

  it('lädt zuerst nur den eigenen Verlauf', async () => {
    const req = await setup();

    expect(req.request.params.get('userIds')).toBe('1');
    req.flush([history()]);
    expect(component.current()).not.toBeNull();
    expect(component.loading()).toBeFalse();
    http.verify();
  });

  /** Ein Reiter je Konto: ich zuerst, danach die Freunde — auch die ohne Namen im Profil. */
  it('legt für jedes Konto einen Reiter an', async () => {
    const req = await setup([
      { userId: 7, displayName: 'A', exact: true, hasName: true },
      { userId: 8, displayName: 'B', exact: false, hasName: false },
    ]);
    req.flush([history()]);

    expect(component.tabs().map(t => [t.userId, t.enabled])).toEqual([[1, true], [7, true], [8, false]]);
    expect(component.activeIndex()).toBe(0);
  });

  /**
   * Ein Reiter = EIN Abruf. „Alle Freunde auf einmal" hiesse, fuer jedes Konto eine Trefferliste
   * bei chess-results zu holen — auch fuer die, die niemand ansieht.
   */
  it('lädt beim Reiterwechsel genau das eine Konto', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);
    first.flush([history()]);

    component.onTabChange(1);

    const req = http.expectOne(r => r.url === '/api/tournament-history');
    expect(req.request.params.get('userIds')).toBe('7');
    req.flush([history({ userId: 7, displayName: 'Freund' })]);
    expect(component.current()?.userId).toBe(7);
    http.verify();
  });

  /** Ein schon geoeffneter Reiter steht beim Zurueckwechseln sofort wieder da. */
  it('behält geladene Reiter im Zwischenspeicher', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);
    first.flush([history()]);

    component.onTabChange(1);
    http.expectOne(r => r.url === '/api/tournament-history')
      .flush([history({ userId: 7, displayName: 'Freund' })]);

    component.onTabChange(0);
    // Die Tabelle steht sofort — der Abruf laeuft nur zum Auffrischen daneben.
    expect(component.current()?.userId).toBe(1);
    expect(component.loading()).toBeFalse();
    http.expectOne(r => r.url === '/api/tournament-history').flush([history()]);
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

    const h = component.current()!;
    expect(component.past(h).map(e => e.chessResultsId)).toEqual(['1107064']);
    expect(component.upcoming(h).map(e => e.chessResultsId)).toEqual(['2']);
  });

  /**
   * „3" allein sagt nicht, ob das aus drei oder aus neun Partien kam. Die Partienzahl steht auf
   * der Spielerkarte und ist NICHT die Rundenzahl des Turniers — in einer Liga wird ein Spieler
   * an einem Teil der Termine aufgestellt.
   */
  it('schreibt die Punkte als Anteil an den gespielten Partien', async () => {
    const req = await setup();
    req.flush([history({ entries: [played({ points: 2, gamesPlayed: 5 })] })]);
    fixture.detectChanges();

    const zelle = fixture.nativeElement.querySelector('.rows .row .row-num');
    expect(zelle.textContent.replace(/\s/g, '')).toBe('2/5');
  });

  it('laesst den Nenner weg, solange die Partienzahl fehlt', async () => {
    // Erfundene Nenner sind schlimmer als eine blanke Punktzahl.
    const req = await setup();
    req.flush([history({ entries: [played({ points: 2, gamesPlayed: null })] })]);
    fixture.detectChanges();

    const zelle = fixture.nativeElement.querySelector('.rows .row .row-num');
    expect(zelle.textContent.replace(/\s/g, '')).toBe('2');
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

    const summary = component.summary(component.current()!);
    expect(summary.played).toBe(3);
    // Turnierschach und Blitz getrennt — 1900 im Blitz ist nicht 1900 im Turnierschach.
    expect(summary.speeds.map(s => [s.speed, s.played, s.performance]))
      .toEqual([['standard', 2, 1800], ['blitz', 1, 1600]]);
  });

  /**
   * Der Filter greift UEBERALL gleich — Liste, Jahresgruppen und Summen. Eine Zeile, die eine
   * andere Menge zusammenfasst als die Tabelle darunter, waere schlimmer als kein Filter.
   */
  it('schränkt Liste UND Auswertung auf die gewählte Bedenkzeit ein', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ chessResultsId: '1', speed: 'blitz', performanceRating: 1600, endDate: '2025-01-06' }),
        played({ chessResultsId: '2', speed: 'standard', performanceRating: 1800, endDate: '2025-08-30' }),
      ],
    })]);

    component.onSpeedFilter('blitz');

    const h = component.current()!;
    expect(component.past(h).map(e => e.chessResultsId)).toEqual(['1']);
    expect(component.summary(h).speeds.map(s => s.speed)).toEqual(['blitz']);
    expect(component.summary(h).played).toBe(1);
  });

  /** Angeboten werden nur Klassen, in denen dieses Konto wirklich gespielt hat. */
  it('bietet nur vorhandene Bedenkzeiten als Filter an', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ chessResultsId: '1', speed: 'blitz' }),
        played({ chessResultsId: '2', speed: 'standard' }),
      ],
    })]);

    expect(component.availableSpeeds()).toEqual(['standard', 'blitz']);
  });

  /**
   * Turniere und Partien sind zwei verschiedene Groessen: fuenf Wochenend-Opens sind fuenf
   * Turniere und rund 25 Partien, eine Ligasaison ein Turnier und drei Partien.
   */
  it('summiert die gespielten Partien je Klasse', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ speed: 'blitz', gamesPlayed: 9, performanceRating: 1700 }),
        played({ chessResultsId: '2', speed: 'blitz', gamesPlayed: 13, performanceRating: 1700 }),
      ],
    })]);

    const blitz = component.summary(component.current()!).speeds[0];
    expect(blitz.played).toBe(2);
    expect(blitz.games).toBe(22);
  });

  /**
   * Kennt noch keine Karte die Partienzahl, steht KEINE Zahl da statt einer erfundenen Null —
   * die Karten vor der Zaehlung tragen sie nicht, der naechtliche Durchgang holt sie nach.
   */
  it('erfindet keine Partienzahl, wo keine bekannt ist', async () => {
    const req = await setup();
    req.flush([history({ entries: [played({ speed: 'blitz', gamesPlayed: null })] })]);

    expect(component.summary(component.current()!).speeds[0].games).toBeNull();
  });

  /**
   * Direkt nach einem Deploy ist jedes Turnier `unknown` — die Bedenkzeit steht auf einer eigenen
   * Seite, die erst der naechtliche Durchgang holt. Faellt diese Gruppe aus der Auswertung, ist die
   * Uebersicht LEER und die Performance verschwunden. Genau so gemeldet.
   */
  it('zählt Turniere ohne bekannte Bedenkzeit als eigene Gruppe', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ performanceRating: 1700, speed: 'unknown' }),
        played({ chessResultsId: '2', performanceRating: 1900, speed: 'unknown' }),
      ],
    })]);

    const speeds = component.summary(component.current()!).speeds;
    expect(speeds.map(s => [s.speed, s.played, s.performance])).toEqual([['unknown', 2, 1800]]);
  });

  /** Bekannte Klassen stehen VOR den unklassifizierten. */
  it('stellt die eingeordneten Klassen voran', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ performanceRating: 1700, speed: 'unknown' }),
        played({ chessResultsId: '2', performanceRating: 1900, speed: 'blitz' }),
      ],
    })]);

    expect(component.summary(component.current()!).speeds.map(s => s.speed))
      .toEqual(['blitz', 'unknown']);
  });

  /**
   * chess-results schreibt eine 0 in die Performance-Spalte, wenn es sie NICHT berechnet hat
   * (Gegner ohne Wertung, sehr wenige Partien, 0 % oder 100 %). Als Wertung gelesen zieht sie den
   * Schnitt nach unten — gemeldet an einem Konto, bei dem vier solche Turniere standen.
   */
  it('rechnet eine Performance von 0 nicht in den Schnitt', async () => {
    const req = await setup();
    req.flush([history({
      entries: [
        played({ chessResultsId: '1', performanceRating: 1800 }),
        played({ chessResultsId: '2', performanceRating: 0 }),
      ],
    })]);

    const speeds = component.summary(component.current()!).speeds;
    // Beide Turniere zaehlen als gespielt, aber nur eines traegt eine Wertung.
    expect(speeds[0].played).toBe(2);
    expect(speeds[0].performance).toBe(1800);
  });

  /** Eine Klasse ohne gewertete Performance bleibt mit ihrer Anzahl stehen. */
  it('unterscheidet „nicht gespielt" von „keine Wertung"', async () => {
    const req = await setup();
    req.flush([history({
      entries: [played({ performanceRating: null, speed: 'rapid' })],
    })]);

    const speeds = component.summary(component.current()!).speeds;
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

    const years = component.years(component.current()!);
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
  it('führt Freunde ohne Namen im Profil auf, aber gesperrt', async () => {
    const req = await setup([
      { userId: 7, displayName: 'Mit Name', exact: true, hasName: true },
      { userId: 8, displayName: 'Ohne Name', exact: false, hasName: false },
    ]);
    req.flush([history()]);

    expect(component.friends().length).toBe(2);
    expect(component.tabs().filter(t => t.enabled).map(t => t.userId)).toEqual([1, 7]);
  });

  /** Ein gesperrter Reiter laedt nichts — dort gibt es nichts zu holen. */
  it('lädt für einen gesperrten Reiter nichts nach', async () => {
    const req = await setup([{ userId: 8, displayName: 'Ohne Name', exact: false, hasName: false }]);
    req.flush([history()]);

    // Material laesst einen gesperrten Reiter gar nicht erst waehlen; ruft ihn doch jemand auf,
    // bleibt es beim eigenen Verlauf.
    expect(component.tabs()[1].enabled).toBeFalse();
    http.verify();
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

    // Beide Textzellen spannen ueber die vier Zahlenspalten. Vorher (ungueltiges colspan auf
    // einem span) standen sie in der 3rem-Punktespalte und brachen wortweise um — eine
    // 78px hohe Zeile mitten in der Tabelle.
    const cells = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.row .row-num.span-rest'));
    expect(cells.length).toBe(2);
    for (const cell of cells) {
      const style = getComputedStyle(cell);
      expect(style.gridColumnStart).toBe('3');
      expect(style.gridColumnEnd).toBe('-1');
      expect(style.whiteSpace).toBe('nowrap');
    }
  });

  /**
   * Gemeldet auf 360/375px: unter „Kommt noch" war die Zeile breiter als die Karte, „9 Runden"
   * ragte rechts heraus und die SEITE bekam horizontalen Scroll — die Spaltenminima (6.5rem +
   * 10rem + 5rem + Abstaende = 360px) passten in keine 329px breite Karte. Der Name darf jetzt
   * auf 0 schrumpfen (Ellipse), die Rundenspalte nimmt nur ihre Textbreite.
   */
  it('passt „Kommt noch" auf einem 390px-Handy in die Karte, ohne seitwaerts ueberzulaufen', async () => {
    const req = await setup();
    const host = fixture.nativeElement as HTMLElement;
    // Hochkant-Handy nachstellen (iPhone-Breite), damit die Messung nicht vom Karma-Fenster abhaengt.
    host.style.display = 'block';
    host.style.width = '390px';
    host.style.overflowX = 'hidden';   // wie ein Viewport: was rauslaeuft, wuerde hier scrollen
    // Echter Text statt des Schluessels, damit die Rundenspalte so breit ist wie in der App.
    TestBed.inject(TranslateService).setTranslation('en', {
      turnier: { history: { roundsShort: '{{count}} rounds' } },
    }, true);

    const future = new Date(Date.now() + 30 * 86400_000).toISOString().slice(0, 10);
    req.flush([history({
      entries: [played({
        chessResultsId: '2', name: 'Tiroler Landesmeisterschaft im Schnellschach 2026 (Gruppe A)',
        endDate: future, rank: null, hasResult: false, rounds: 9,
      })],
    })]);
    fixture.detectChanges();

    const row = host.querySelector('.row.upcoming') as HTMLElement | null;
    expect(row).withContext('Zeile unter „Kommt noch" gerendert').toBeTruthy();
    const meta = row!.querySelector('.row-meta') as HTMLElement;
    expect(meta.textContent?.trim()).toBe('9 rounds');
    // Die Rundenspalte endet INNERHALB der Zeile (gegen den Ist-Stand: ~40px rechts daneben) …
    expect(meta.getBoundingClientRect().right).toBeLessThanOrEqual(row!.getBoundingClientRect().right + 1);
    // … der lange Name wird abgeschnitten statt die Zeile zu weiten …
    const name = row!.querySelector('.row-name') as HTMLElement;
    expect(name.scrollWidth).toBeGreaterThan(name.clientWidth);
    // … und die Seite selbst wird nicht breiter.
    expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth + 1);
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
  it('verwirft die Antwort eines überholten Reiters', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);

    component.onTabChange(1);
    const second = http.expectOne(r => r.url === '/api/tournament-history');

    // Die ALTE Antwort trifft nach dem Wechsel ein und darf den Reiter nicht bestimmen.
    second.flush([history({ userId: 7, displayName: 'Freund' })]);
    first.flush([history()]);

    expect(component.current()?.userId).toBe(7);
  });

  it('merkt den Reiter über den Seitenwechsel hinweg', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);
    first.flush([history()]);
    component.onTabChange(1);
    http.expectOne(r => r.url === '/api/tournament-history')
      .flush([history({ userId: 7, displayName: 'Freund' })]);

    TestBed.resetTestingModule();
    const again = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);

    expect(again.request.params.get('userIds')).toBe('7');
    again.flush([history({ userId: 7, displayName: 'Freund' })]);
  });

  /**
   * Der gemerkte Reiter kann weg sein (Freundschaft aufgeloest) — dann zurueck auf den eigenen,
   * statt auf einen Reiter zu zeigen, den es nicht mehr gibt.
   */
  it('fällt auf den eigenen Reiter zurück, wenn der gemerkte fehlt', async () => {
    const first = await setup([{ userId: 7, displayName: 'Freund', exact: true, hasName: true }]);
    first.flush([history()]);
    component.onTabChange(1);
    http.expectOne(r => r.url === '/api/tournament-history')
      .flush([history({ userId: 7, displayName: 'Freund' })]);

    TestBed.resetTestingModule();
    const again = await setup([]);          // der Freund ist weg
    expect(again.request.params.get('userIds')).toBe('7');
    again.flush([history({ userId: 7, displayName: 'Freund' })]);

    const back = http.expectOne(r => r.url === '/api/tournament-history');
    expect(back.request.params.get('userIds')).toBe('1');
    back.flush([history()]);
    expect(component.activeUserId()).toBe(1);
  });
});
