import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, TranslationObject, provideTranslateService } from '@ngx-translate/core';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry } from './tournament-directory.model';
import { directoryCalendarEvent } from './directory-calendar-event';

function entry(over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    id: '1457129', chessResultsId: '1457129', name: 'Open Braunau', federation: 'AUT',
    state: 'Salzburg',
    startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: 7, playerCount: 42, lat: 48.2, lon: 13.0, geoSource: 'City', geoPlaceName: 'Ranshofen',
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [], ...over,
  };
}

/**
 * Die gemeinsame Kurzansicht. Sie steht in DREI Ansichten (Liste, Karten-Popup,
 * Kalender-Fenster) und bietet ueberall dieselben vier Aktionen — merken, in den Kalender
 * uebertragen, ausblenden, melden. Vorher zeigte jede Ansicht das Turnier anders, und wer es auf
 * der Karte fand, musste erst auf die Detailseite, um es zu merken.
 */
describe('TournamentCardComponent', () => {
  /** Angemeldet? Gast-Tests setzen das vor dem Aufbau auf false (Turnierseite seit 0.643.0 ohne Konto offen). */
  let signedIn = true;
  beforeEach(() => { signedIn = true; });

  let fixture: ComponentFixture<TournamentCardComponent>;
  let component: TournamentCardComponent;
  let http: HttpTestingController;

  function setup(over: Partial<DirectoryEntry> = {}, translations?: TranslationObject) {
    TestBed.configureTestingModule({
      imports: [TournamentCardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    // Diese Specs pruefen das ANGEMELDETE Verhalten (Gaeste: eigene Tests, seit 0.643.0 ohne Anmeldung offen).
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    fixture = TestBed.createComponent(TournamentCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('entry', entry(over));
    http = TestBed.inject(HttpTestingController);
    if (translations) {
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('de', translations);
      translate.use('de');
    }
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  /**
   * Ohne Konto (Kalender seit 0.643.0 offen): kein „Ausblenden" (gehoert einem Konto), und Merken bzw. Melden
   * fuehren zur Anmeldung mit Ruecksprung — ohne einen einzigen Abruf, der nur in 401 enden wuerde.
   */
  it('fuehrt Gaeste zum Merken und Melden zur Anmeldung und bietet kein Ausblenden an', () => {
    signedIn = false;
    setup();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);

    expect(fixture.nativeElement.querySelectorAll('.tc-actions button').length).toBe(3);
    component.bookmark();
    component.report();

    expect(navigate).toHaveBeenCalledTimes(2);
    expect(String(navigate.calls.first().args[0])).toContain('/login?returnUrl=');
    http.verify();
  });

  it('bietet vier Aktionen an', () => {
    setup();

    expect(fixture.nativeElement.querySelectorAll('.tc-actions button').length).toBe(4);
  });

  /**
   * Ohne Termin gibt es nichts in einen Kalender zu uebertragen — dann fehlt der Knopf, statt
   * einen leeren Termin anzubieten.
   */
  it('lässt den Kalender-Knopf weg, wenn kein Termin bekannt ist', () => {
    setup({ startDate: null });

    expect(fixture.nativeElement.querySelectorAll('.tc-actions button').length).toBe(3);
  });

  /**
   * „Merken" heisst auch „holen" (siehe bookmarkAndImport) — sonst kaeme das Turnier erst zum
   * Spielbeginn.
   */
  it('merkt das Turnier und holt es gleich mit', () => {
    setup();

    component.bookmark();

    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });
    const crawl = http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' });
    expect(crawl.request.body).toEqual({ chessResultsId: '1457129', jobType: 'Full' });
    crawl.flush({ id: 5, status: 'Pending' });

    expect(component.subscribed()).toBeTrue();
    http.verify();
  });

  /** Die KARTE faerbt ihren Punkt daraufhin um, ohne den Ausschnitt neu zu laden. */
  it('meldet das Merken nach draußen', () => {
    setup();
    let change: { entry: DirectoryEntry; subscribed: boolean } | null = null;
    component.subscribedChanged.subscribe(c => (change = c));

    component.bookmark();
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' }).flush({ id: 5, status: 'Pending' });

    expect(change!.subscribed).toBeTrue();
  });

  /**
   * Der Merken-Knopf ist ein UMSCHALTER. Vorher tat ein zweiter Klick GAR NICHTS — wer sich
   * vertippt hatte, musste das Abo woanders suchen.
   */
  it('nimmt das Merken beim zweiten Klick zurück', () => {
    setup({ subscribed: true });
    let change: { entry: DirectoryEntry; subscribed: boolean } | null = null;
    component.subscribedChanged.subscribe(c => (change = c));

    component.bookmark();

    const req = http.expectOne('/api/subscriptions/by-tournament/1457129');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(component.subscribed()).toBeFalse();
    expect(change!.subscribed).toBeFalse();
    // Kein zweites Abo und kein Holen-Auftrag.
    http.verify();
  });

  /** Scheitert das Loesen, bleibt „gemerkt" stehen — kein Symbol, das zurueckspringt. */
  it('lässt „gemerkt" bei einem Fehlschlag stehen', () => {
    setup({ subscribed: true });

    component.bookmark();
    http.expectOne('/api/subscriptions/by-tournament/1457129')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.subscribed()).toBeTrue();
    expect(component.busy()).toBeFalse();
  });

  it('blendet das Turnier aus und meldet das nach draußen', () => {
    setup();
    let change: { entry: DirectoryEntry; ignored: boolean } | null = null;
    component.ignoredChanged.subscribe(c => (change = c));

    component.toggleIgnore();

    const req = http.expectOne('/api/tournament-directory/1457129/ignore');
    expect(req.request.method).toBe('POST');
    req.flush(null);

    expect(component.ignored()).toBeTrue();
    expect(change!.ignored).toBeTrue();
    http.verify();
  });

  it('blendet ein ausgeblendetes Turnier wieder ein', () => {
    setup({ ignored: true });

    component.toggleIgnore();

    const req = http.expectOne('/api/tournament-directory/1457129/ignore');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(component.ignored()).toBeFalse();
    http.verify();
  });

  /**
   * Scheitert das Ausblenden, bleibt der Zustand wie er war — ein Symbol, das umschlaegt und
   * dann beim naechsten Laden zurueckspringt, ist schlimmer als eine Meldung.
   */
  it('lässt den Zustand bei einem Fehlschlag unverändert', () => {
    setup();

    component.toggleIgnore();
    http.expectOne('/api/tournament-directory/1457129/ignore')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.ignored()).toBeFalse();
    expect(component.busy()).toBeFalse();
  });

  /**
   * Sind die SPIELTERMINE bekannt, sagen sie mehr als der Zeitraum: eine Liga laeuft von
   * September bis April, gespielt wird an elf Tagen.
   */
  it('nennt bei bekannten Spielterminen deren Anzahl statt des Zeitraums', () => {
    setup({
      startDate: '2026-09-26', endDate: '2027-04-17',
      roundDates: [
        { round: 1, date: '2026-09-26', time: '14:00' },
        { round: 2, date: '2026-10-10', time: '14:00' },
        { round: 3, date: '2027-04-17', time: '14:00' },
      ],
    });

    // Ohne geladene Sprachdatei gibt ngx-translate den SCHLUESSEL zurueck — geprueft wird
    // deshalb, welcher Zweig gewaehlt wurde, nicht der fertige Satz.
    expect(component.dateText).toBe('tournamentDirectory.card.rounds');
  });

  it('zeigt einen einzelnen Tag ohne Bis-Datum', () => {
    setup({ startDate: '2026-12-18', endDate: '2026-12-18' });

    // Ohne gewaehlte Sprache: englisch, wie die Uebersetzungen (Codereview F6-010: vorher ISO).
    expect(component.dateText).toBe('Dec 18, 2026');
  });

  it('schreibt den Zeitraum sprachgerecht statt als ISO-Datum', () => {
    setup({ startDate: '2026-12-18', endDate: '2026-12-20' });

    expect(component.dateText).not.toContain('2026-12-18');
    expect(component.dateText.replace(/\s/g, ' ')).toBe('Dec 18 – 20, 2026');
  });

  /**
   * Codereview UX-039: ein Eintrag mit unplausibler Laufzeit („2007 bis 2026") kommt nur mit dem
   * Schalter in die Antwort — dann sagt die Karte, warum er sonst fehlt.
   */
  it('kennzeichnet einen unplausiblen Zeitraum, sonst nicht', () => {
    setup({ startDate: '2007-12-26', endDate: '2026-12-10', implausible: true });
    expect(fixture.nativeElement.querySelector('.badge.implausible')?.textContent?.trim())
      .toBe('tournamentDirectory.card.implausible');

    TestBed.resetTestingModule();
    setup();
    expect(fixture.nativeElement.querySelector('.badge.implausible')).toBeNull();
  });

  /** Publikum und Format stehen als Kurzangaben mit — dieselben, nach denen der Filter fragt. */
  it('zeigt Mannschaft, Altersklasse und Geschlechtsklasse als Kurzangaben', () => {
    setup({ kind: 'Team', ageGroups: ['U12'], gender: 'Female' });

    const badges = [...fixture.nativeElement.querySelectorAll('.badge')].map(
      (n: Element) => n.textContent?.trim());
    expect(badges).toContain('tournamentDirectory.kind.Team');
    expect(badges).toContain('tournamentDirectory.age.U12');
    expect(badges).toContain('tournamentDirectory.gender.Female');
  });

  it('führt erst über den Namen auf die Detailseite', () => {
    setup();
    let selected: DirectoryEntry | null = null;
    component.selected.subscribe(e => (selected = e));

    fixture.nativeElement.querySelector('.tc-name').click();

    expect(selected!.id).toBe('1457129');
  });

  /**
   * Ein Turnier aus dem FIDE-Kalender hat KEINE chess-results-Nummer — und Merken heisst Abo
   * plus Holen-Auftrag, die beide daran haengen. Der Knopf muss dort verschwinden: einer, der
   * nichts tun kann, ist schlimmer als ein fehlender.
   */
  it('lässt den Merken-Knopf weg, wenn das Turnier nicht auf chess-results steht', () => {
    setup({ id: 'f14805', chessResultsId: null });

    expect(fixture.nativeElement.querySelectorAll('.tc-actions button').length).toBe(3);
    expect(component.bookmarkable).toBeFalse();

    component.bookmark();

    http.verify();
  });

  /** Ausblenden und Melden gehen ueber die IDENTITAET, nicht ueber die chess-results-Nummer. */
  it('blendet auch ein Turnier ohne chess-results-Nummer aus', () => {
    setup({ id: 'f14805', chessResultsId: null });

    component.toggleIgnore();

    http.expectOne('/api/tournament-directory/f14805/ignore').flush(null);

    expect(component.ignored()).toBeTrue();
    http.verify();
  });

  /** Die Texte, wie sie de.json traegt — der vorgelesene Name ist Tooltip plus Turniername. */
  const DE = {
    tournamentDirectory: {
      bookmark: 'Merken', bookmarkRemove: 'Merken aufheben',
      detail: { toCalendar: 'In privaten Kalender übertragen' },
      report: { cta: 'Falsches Event melden' },
      card: { hide: 'Für mich ausblenden', show: 'Wieder zeigen', actionAria: '{{action}}: {{name}}' },
    },
  };

  const ariaLabels = (): (string | null)[] =>
    [...fixture.nativeElement.querySelectorAll('.tc-actions button')].map(
      (b: Element) => b.getAttribute('aria-label'));

  /**
   * Codereview UX-043: der Screenreader las „Merken" auch bei einem schon gemerkten Turnier —
   * wer darauf drueckte, hob das Merken auf. Der vorgelesene Name folgt jetzt dem Zustand wie
   * der Tooltip, und er nennt das Turnier: auf Seite 1 stehen bis zu 50 Karten mit denselben
   * vier Symbolen, in der Schaltflaechenliste waren das 200 gleichlautende Eintraege.
   */
  it('nennt im vorgelesenen Namen Zustand und Turnier', () => {
    setup({ subscribed: true }, DE);

    expect(ariaLabels()).toEqual([
      'Merken aufheben: Open Braunau',
      'In privaten Kalender übertragen: Open Braunau',
      'Für mich ausblenden: Open Braunau',
      'Falsches Event melden: Open Braunau',
    ]);
  });

  it('wechselt den vorgelesenen Namen mit dem Zustand', () => {
    setup({ ignored: true }, DE);
    const [bookmark, , ignore] = [...fixture.nativeElement.querySelectorAll('.tc-actions button')] as HTMLElement[];
    expect(bookmark.getAttribute('aria-label')).toBe('Merken: Open Braunau');
    expect(ignore.getAttribute('aria-label')).toBe('Wieder zeigen: Open Braunau');

    component.bookmark();
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' }).flush({ id: 5, status: 'Pending' });
    component.toggleIgnore();
    http.expectOne('/api/tournament-directory/1457129/ignore').flush(null);
    fixture.detectChanges();

    expect(bookmark.getAttribute('aria-label')).toBe('Merken aufheben: Open Braunau');
    expect(ignore.getAttribute('aria-label')).toBe('Für mich ausblenden: Open Braunau');
  });

  /**
   * Der Kalendereintrag traegt die IDENTITAET als Kennung: dieselbe Kennung aktualisiert den
   * Termin statt ihn zu verdoppeln, und ohne chess-results-Nummer gaebe es sonst gar keine.
   */
  it('nimmt die Identität als Kennung des Kalendereintrags und lässt den Link weg', () => {
    setup({ id: 'f14805', chessResultsId: null });

    const event = component.calendarEvent()!;
    expect(event.uid).toBe('directory-f14805@rookhub');
    expect(event.description).not.toContain('chess-results.com');
  });

  /** Dieselbe Abbildung wie die Detailseite — gleiche Kennung, keine rohen Uebersetzungsschluessel (F6-009). */
  it('baut den Termin wie die Detailseite: Kennung directory-<id>, übersetzte Runden', () => {
    setup({ organizer: 'SK Braunau' });

    const event = component.calendarEvent()!;
    expect(event.uid).toBe('directory-1457129@rookhub');
    expect(event.url).toBe('https://chess-results.com/tnr1457129.aspx?lan=1');
    expect(event).toEqual(directoryCalendarEvent(entry({ organizer: 'SK Braunau' }), TestBed.inject(TranslateService))!);
    expect(event.description).not.toContain('roundsCount');
  });
});
