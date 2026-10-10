import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OpenTournamentService } from '../../core/open-tournament.service';
import { AuthService } from '@rh/core/auth.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { TournamentDirectoryDetailComponent } from './tournament-directory-detail.component';
import { DirectoryEntry } from './tournament-directory.model';
import { SnackbarService } from '@rh/core/snackbar.service';

function entry(id: string, over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    id, chessResultsId: id, name: 'Open Braunau 2026', federation: 'AUT', state: 'Oberösterreich',
    startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: 'SK Braunau', director: null,
    chiefArbiter: null, rounds: 7, playerCount: 42, lat: 48.2, lon: 13.0, geoSource: 'City',
    geoPlaceName: 'Ranshofen', distanceKm: null, cancelled: false, subscribed: false,
    groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [], ...over,
  };
}

describe('TournamentDirectoryDetailComponent', () => {
  /** Angemeldet? Gast-Tests setzen das vor dem Aufbau auf false (Turnierseite seit 0.643.0 ohne Konto offen). */
  let signedIn = true;
  beforeEach(() => { signedIn = true; });

  let fixture: ComponentFixture<TournamentDirectoryDetailComponent>;
  let component: TournamentDirectoryDetailComponent;
  let http: HttpTestingController;

  async function setup(id = '1457129') {
    await TestBed.configureTestingModule({
      imports: [TournamentDirectoryDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } },
      ],
    }).compileComponents();

    // Diese Specs pruefen das ANGEMELDETE Verhalten (Gaeste: eigene Tests, seit 0.643.0 ohne Anmeldung offen).
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    fixture = TestBed.createComponent(TournamentDirectoryDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  /** Der Blick „liegt das Turnier schon geholt hier?" laeuft nach jedem geladenen Eintrag. */
  function flushImportLookup(id: string, body: Record<string, unknown> | null = null) {
    const req = http.expectOne(`/api/tournaments/${id}`);
    if (body) req.flush(body);
    else req.flush('nicht geholt', { status: 404, statusText: 'Not Found' });
  }

  it('lädt das Turnier zur Id aus der Adresse', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    expect(component.entry()?.name).toBe('Open Braunau 2026');
    expect(component.loading()).toBeFalse();
    expect(component.notFound()).toBeFalse();
    http.verify();
  });

  it('sagt es, wenn das Turnier nicht im Verzeichnis steht', async () => {
    await setup('999');
    http.expectOne('/api/tournament-directory/999')
      .flush('weg', { status: 404, statusText: 'Not Found' });

    expect(component.notFound()).toBeTrue();
    expect(component.entry()).toBeNull();
    // Ohne Eintrag wird auch nicht nach einem geholten Turnier gesucht.
    http.verify();
  });

  it('wertet eine ungültige Id (400) ebenfalls als „nicht im Verzeichnis"', async () => {
    await setup('kaputt');
    http.expectOne('/api/tournament-directory/kaputt')
      .flush('ungueltig', { status: 400, statusText: 'Bad Request' });

    expect(component.notFound()).toBeTrue();
    expect(component.loadFailed()).toBeFalse();
    http.verify();
  });

  /**
   * Ein Serverfehler hiess bisher „steht (noch) nicht im Verzeichnis" — wer das glaubte, meldete
   * ein Turnier nach, das laengst drinsteht (Codereview UX-040).
   */
  it('sagt bei einem Serverfehler „konnte nicht geladen werden" und lädt auf Wunsch erneut', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129')
      .flush('kaputt', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(component.loadFailed()).toBeTrue();
    expect(component.notFound()).toBeFalse();
    expect(page.textContent).toContain('tournamentDirectory.detail.loadFailed');
    expect(page.textContent).not.toContain('tournamentDirectory.unknownTournament');

    page.querySelector<HTMLButtonElement>('.load-failed button')!.click();
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');
    fixture.detectChanges();

    expect(component.loadFailed()).toBeFalse();
    expect(component.entry()?.name).toBe('Open Braunau 2026');
    http.verify();
  });

  it('bietet den Sprung zu Teilnehmern und Ergebnissen, sobald das Turnier geholt ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129', { id: 12, chessResultsId: '1457129', name: 'Open Braunau 2026' });

    expect(component.imported()?.id).toBe(12);
  });

  /**
   * Ohne Konto (seit 0.643.0): ein noch nicht geholtes Turnier bekommt trotzdem „Teilnehmer und Ergebnisse" — der
   * Knopf holt es und oeffnet es (Merken, das sonst holt, braucht ein Konto und fuehrt zur Anmeldung).
   */
  it('holt fuer Gaeste ein noch nicht geholtes Turnier ueber „Teilnehmer und Ergebnisse"', async () => {
    signedIn = false;
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');
    fixture.detectChanges();
    const open = spyOn(TestBed.inject(OpenTournamentService), 'open');
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.detail-actions button')) as HTMLButtonElement[];
    const results = buttons.find(b => b.textContent?.includes('tournamentDirectory.detail.results'));
    expect(results).withContext('Ergebnis-Knopf fuer Gaeste').toBeTruthy();
    results!.click();
    expect(open).toHaveBeenCalledWith('1457129');

    component.bookmark();
    expect(String(navigate.calls.mostRecent().args[0])).toContain('/login?returnUrl=');
    expect(fixture.nativeElement.textContent).toContain('tournamentDirectory.detail.notImportedHintGuest');
    http.verify();
  });

  it('nimmt kein fremdes Turnier, das nur zufällig auf die interne Nummer passt', async () => {
    // Die Crawler-Route loest erst die INTERNE Nummer auf. Beide Nummernkreise sind numerisch —
    // ohne Gegenprobe zeigte die Seite die Ergebnisse eines voellig anderen Turniers.
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129', { id: 1457129, chessResultsId: '888', name: 'Ganz anderes' });

    expect(component.imported()).toBeNull();
  });

  it('lässt eine verspätete Antwort nicht das Turnier eines anderen setzen', async () => {
    // Bei einem reinen Parameterwechsel wird die Komponente nicht zerstört — takeUntilDestroyed
    // greift also nicht. Ohne Vergleich mit dem ANGEZEIGTEN Eintrag führte „Ergebnisse" zum
    // falschen Turnier.
    await setup('111');
    http.expectOne('/api/tournament-directory/111').flush(entry('111'));
    const spaet = http.expectOne('/api/tournaments/111');

    // Weiter zu einem anderen Turnier, bevor die erste Nachfrage antwortet.
    component.entry.set(entry('222'));
    spaet.flush({ id: 9, chessResultsId: '111', name: 'Das alte' });

    expect(component.imported()).toBeNull();
  });

  it('merkt das Turnier, holt es gleich mit und schaltet die Anzeige um', async () => {
    // Ein Abo allein legt nur einen Vermerk an — Teilnehmer und Tabelle kaemen erst zum
    // Spielbeginn. Wer etwas merkt, will es aber ansehen koennen.
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    component.bookmark();
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });
    const crawl = http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' });
    expect(crawl.request.body).toEqual({ chessResultsId: '1457129', jobType: 'Full' });
    crawl.flush({ id: 7, status: 'Pending' });

    expect(component.entry()?.subscribed).toBeTrue();
    expect(component.importing()).toBeTrue();
    http.verify();
  });

  /**
   * Codereview 2026-09-29, F6-008: bookmark() hatte — anders als removeBookmark() — keinen
   * Riegel. Ein Doppeltipp schickte zwei Abos los: „Gemerkt", dann „Merken fehlgeschlagen".
   */
  it('schickt bei einem Doppeltipp auf „Merken" nur ein Abo los und sperrt den Knopf', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');
    fixture.detectChanges();

    component.bookmark();
    component.bookmark();
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.detail-actions button')!;
    expect(button.disabled).toBeTrue();

    // expectOne faellt bei zwei gleichen Anfragen um.
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' }).flush({ id: 7, status: 'Pending' });
    expect(component.busy()).toBeFalse();
    expect(component.entry()?.subscribed).toBeTrue();
    http.verify();
  });

  it('wertet „schon gemerkt" (409) als gemerkt statt als Fehler', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');
    const snackbar = TestBed.inject(SnackbarService);
    const warn = spyOn(snackbar, 'warn');
    const success = spyOn(snackbar, 'success');

    component.bookmark();
    http.expectOne({ method: 'POST', url: '/api/subscriptions' })
      .flush({ message: 'Already subscribed to this tournament.' }, { status: 409, statusText: 'Conflict' });

    expect(warn).not.toHaveBeenCalled();
    expect(success).toHaveBeenCalledWith('tournamentDirectory.bookmarked');
    expect(component.entry()?.subscribed).toBeTrue();
    expect(component.busy()).toBeFalse();
    http.verify();
  });

  it('bleibt gemerkt, wenn sich der Holen-Auftrag nicht einreihen laesst', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    component.bookmark();
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });
    http.expectOne('/api/tournaments/crawl').flush('nein', { status: 500, statusText: 'Server Error' });

    expect(component.entry()?.subscribed).toBeTrue();
    expect(component.importing()).toBeFalse();
    http.verify();
  });

  it('zeigt die Karte nur mit Koordinaten und passt sie auf den Ort ein', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    expect(component.pins().length).toBe(1);
    expect(component.mapCentre()).toEqual({ lat: 48.2, lon: 13.0, radiusKm: 6 });
    // Und derselbe Getter liefert dasselbe OBJEKT — ein frisches Literal je Zyklus liesse die
    // Karte in jeder Änderungserkennung neu einpassen (genau der Zoom-Fehler im Kalender).
    expect(component.mapCentre()).toBe(component.mapCentre());
  });

  it('lässt die Karte weg, wenn das Turnier nicht verortet ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129')
      .flush(entry('1457129', { lat: null, lon: null, geoSource: 'None' }));
    flushImportLookup('1457129');

    expect(component.pins()).toEqual([]);
    expect(component.mapCentre()).toBeNull();
  });

  it('führt zurück in den Kalender statt in den Browserverlauf', async () => {
    // Der Einstieg kann eine Benachrichtigung gewesen sein — dann führte „zurück" aus der App.
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    component.back();
    expect(navigate).toHaveBeenCalledWith(['/tournaments/calendar']);
  });

  // ----- In den privaten Kalender uebertragen -------------------------------

  it('baut aus dem Turnier einen Ganztages-Termin mit allem, was bekannt ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    const event = component.calendarEvent()!;

    // Dieselbe Kennung wie auf der Karte — sonst landet derselbe Termin doppelt im Kalender (F6-009).
    expect(event.uid).toBe('directory-1457129@rookhub');
    expect(event.title).toBe('Open Braunau 2026');
    expect(event.start).toBe('2026-12-18');
    expect(event.end).toBe('2026-12-20');
    expect(event.location).toBe('Ranshofen');
    expect(event.url).toBe('https://chess-results.com/tnr1457129.aspx?lan=1');
    // Nur, was auch stimmt: Bedenkzeit, Runden, Gemeldete, Veranstalter.
    expect(event.description).toContain('90 min');
    expect(event.description).toContain('7');
    expect(event.description).toContain('SK Braunau');
  });

  it('bietet keinen Termin an, solange kein Datum bekannt ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129')
      .flush(entry('1457129', { startDate: null, endDate: null }));
    flushImportLookup('1457129');

    expect(component.calendarEvent()).toBeNull();
  });

  /**
   * Ein Turnier aus dem FIDE-Kalender hat keine chess-results-Nummer. Alles, was daran haengt,
   * muss dort verschwinden — der Link dorthin, das Merken (Abo + Holen-Auftrag) und die
   * Nachfrage, ob es schon geholt wurde. Ausblenden und Melden laufen ueber die IDENTITAET und
   * bleiben.
   */
  it('lässt bei einem Turnier ohne chess-results-Nummer Merken, Link und Nachfrage weg', async () => {
    await setup('f14805');
    http.expectOne('/api/tournament-directory/f14805')
      .flush(entry('f14805', {
        chessResultsId: null,
        sources: [{ kind: 'Fide', externalId: '14805', url: 'https://calendar.fide.com/calendar.php?id=14805' }],
      }));
    fixture.detectChanges();

    // Kein Blick in die Crawler-Datenbank — dort kann es dieses Turnier gar nicht geben.
    http.verify();

    const html = fixture.nativeElement.textContent as string;
    expect(html).toContain('tournamentDirectory.detail.notOnChessResults');
    expect(fixture.nativeElement.querySelectorAll('a[href*="chess-results.com"]').length).toBe(0);
    expect(fixture.nativeElement.querySelector('a[href*="calendar.fide.com"]')).toBeTruthy();

    component.bookmark();
    http.verify();
  });

  // ----- UI-Sweep 2026-10-10 -----

  it('nennt den Ankuendigungskalender als chess-results mit grauem Zusatz, unbekannte Quellen ohne rohen Schluessel (t-i18n-source)', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', {
      sources: [
        { kind: 'ChessResults', externalId: '1457129', url: 'https://chess-results.com/tnr1457129.aspx' },
        { kind: 'ChessResultsCalendar', externalId: '77', url: null },
        { kind: 'MarsChessFederation' as never, externalId: '1', url: null },
      ],
    }));
    flushImportLookup('1457129');
    fixture.detectChanges();
    const sources = [...fixture.nativeElement.querySelectorAll('.sources > .source')].map((n: Element) => n.textContent!.replace(/\s+/g, ' ').trim());
    expect(sources).toEqual([
      'tournamentDirectory.source.ChessResults',
      'tournamentDirectory.source.ChessResults(tournamentDirectory.source.ChessResultsCalendar)',
      'tournamentDirectory.source.Unknown',
    ]);
    expect(fixture.nativeElement.querySelector('.source-note').classList).toContain('muted');
  });

  it('zeigt keinen Bedenkzeit-Chip „Unbekannt" (t-chip-unknown)', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', { speed: 'Unknown' }));
    flushImportLookup('1457129');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.badges').textContent).not.toContain('tournamentDirectory.speed.Unknown');
  });

  it('gibt der Beschriftungsspalte feste 180 px und 16 px Abstand (t-dir-labels)', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', { chiefArbiter: 'ÖS Ing. Erich Wurzer' }));
    flushImportLookup('1457129');
    fixture.detectChanges();
    const dl = getComputedStyle(fixture.nativeElement.querySelector('.detail-grid'));
    if (window.innerWidth > 600) expect(dl.gridTemplateColumns.split(' ')[0]).toBe('180px');
    expect(dl.columnGap).toBe('16px');
  });

  it('fasst Kalender, chess-results und Melden als beschriftete Symbole zusammen (t-dir-buttons)', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');
    fixture.detectChanges();
    const icons = [...fixture.nativeElement.querySelectorAll('.icon-actions > *')] as HTMLElement[];
    expect(icons.map(b => b.querySelector('.short')!.textContent!.trim())).toEqual([
      'tournamentDirectory.card.short.calendar', 'tournamentDirectory.source.ChessResults', 'tournamentDirectory.card.short.report']);
    expect(icons.map(b => b.getAttribute('aria-label'))).toEqual([
      'tournamentDirectory.detail.toCalendar', 'tournamentDirectory.openChessResults', 'tournamentDirectory.report.cta']);
    expect(fixture.nativeElement.querySelector('.detail-actions .primary-mobile')?.textContent).toContain('tournamentDirectory.bookmark');
  });

  /**
   * Bei einer Liga sagt der Zeitraum fast nichts (September bis April), die Runden sagen alles.
   */
  it('zeigt die Spieltermine, sobald mehr als einer bekannt ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', {
      roundDates: [
        { round: 1, date: '2026-09-26', time: '14:00' },
        { round: 2, date: '2026-10-10', time: '14:00' },
      ],
    }));
    flushImportLookup('1457129');
    fixture.detectChanges();

    const dates = [...fixture.nativeElement.querySelectorAll('.play-date')]
      .map((n: Element) => n.textContent?.trim());
    // Mit Wochentag statt ISO (Codereview F6-010); ohne gewaehlte Sprache englisch.
    expect(dates).toEqual(['1. Sat, Sep 26', '2. Sat, Oct 10']);
  });

  /**
   * Was sich auf dieser Seite setzen laesst, muss sich hier auch zuruecknehmen lassen — vorher
   * stand dort nur der Vermerk „gemerkt" ohne Weg zurueck.
   */
  it('nimmt das Merken auf der Detailseite zurück', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', { subscribed: true }));
    flushImportLookup('1457129');

    component.removeBookmark();

    const req = http.expectOne('/api/subscriptions/by-tournament/1457129');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(component.entry()?.subscribed).toBeFalse();
    expect(component.busy()).toBeFalse();
    http.verify();
  });

  it('lässt „gemerkt" stehen, wenn das Lösen scheitert', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', { subscribed: true }));
    flushImportLookup('1457129');

    component.removeBookmark();
    http.expectOne('/api/subscriptions/by-tournament/1457129')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.entry()?.subscribed).toBeTrue();
    expect(component.busy()).toBeFalse();
  });

  it('sagt es, wenn die Datei auf diesem Geraet nicht erstellt werden konnte', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
    // Gesperrter Speicher / Umgebung ohne Blob-URLs — der Nutzer darf nicht ins Leere klicken.
    spyOn(URL, 'createObjectURL').and.throwError('kein Blob');

    component.addToCalendar();

    expect(warn).toHaveBeenCalled();
  });

  /**
   * Freitext von chess-results: steht im Ort, Veranstalter oder in der Bedenkzeit ein langes Wort
   * ohne Leerzeichen (URL, E-Mail, „90min/40+30min+30sec/Zug"), darf es die dd-Spalte nicht
   * sprengen — vorher wurde die Karte breiter als der Bildschirm und die ganze Seite scrollte quer.
   */
  it('lässt ein langes Wort ohne Leerzeichen umbrechen, statt die Seite quer scrollen zu lassen', async () => {
    await setup('1457129');
    const host = fixture.nativeElement as HTMLElement;
    // Kleines Android nachstellen, damit die Messung nicht vom Karma-Fenster abhaengt.
    host.style.display = 'block';
    host.style.width = '360px';

    http.expectOne('/api/tournament-directory/1457129')
      .flush(entry('1457129', { location: 'https://maps.app.goo.gl/z6pZeyGmneMbrUwLA?g_st=ac' }));
    flushImportLookup('1457129');
    fixture.detectChanges();

    const card = host.querySelector<HTMLElement>('.detail-card')!;
    const dl = host.querySelector<HTMLElement>('.detail-grid')!;
    expect(dl.textContent).toContain('maps.app.goo.gl');
    expect(dl.scrollWidth).toBeLessThanOrEqual(card.clientWidth);
  });
});
