import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@rh/core/auth.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentDetailComponent } from './tournament-detail.component';
import { Subscription, TournamentGroup, TournamentPlayer, TournamentTeam } from '@rh/core/models';
import { SnackbarService } from '@rh/core/snackbar.service';
import { OpenTournamentService } from '../../core/open-tournament.service';

describe('TournamentDetailComponent', () => {
  /** Angemeldet? Gast-Tests setzen das vor dem Aufbau auf false (Turnierseite seit 0.643.0 ohne Konto offen). */
  let signedIn = true;
  beforeEach(() => { signedIn = true; });

  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    // Diese Specs pruefen das ANGEMELDETE Verhalten (Gaeste: eigene Tests, seit 0.643.0 ohne Anmeldung offen).
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  /**
   * Ohne Konto (seit 0.643.0): Teilnehmer, Teams und Paarungen laden wie sonst, aber KEIN persoenlicher Abruf
   * (Favoriten, Merkliste, Beobachtung — die endeten nur in 401 und „Abo-Status konnte nicht geladen werden").
   * Sterne liegen auf dem Geraet, mit denselben Schluesseln wie die oeffentliche Ansicht /t/{id}; Merken fuehrt zur
   * Anmeldung.
   */
  it('laesst Gaeste ohne persoenliche Abrufe hinein, Sterne auf dem Geraet, Merken zur Anmeldung', async () => {
    signedIn = false;
    localStorage.removeItem('public_fav_players_4711');
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '4711' }), queryParams: {} } } },
      ],
    }).compileComponents();
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/tournaments/4711').flush({
      id: 1, name: 'Schach Tirol Open', chessResultsId: '4711', location: null, date: null, totalRounds: 0, knownRounds: 0, createdAt: '', updatedAt: '',
    });
    http.expectOne('/api/tournaments/4711/players').flush([{ id: 1, snr: 7, title: null, name: 'Anna', fideId: null, elo: 1900, country: 'AUT', teamName: null, boardNumber: null }]);
    http.expectOne('/api/tournaments/4711/teams').flush([]);
    http.verify();   // keine Favoriten, keine Merkliste, keine Beobachtung

    const component = fixture.componentInstance;
    component.toggleFavorite(component.players[0]);
    expect(JSON.parse(localStorage.getItem('public_fav_players_4711')!)).toEqual([7]);

    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    component.subscribe();
    expect(String(navigate.calls.mostRecent().args[0])).toContain('/login?returnUrl=');
    http.verify();
    localStorage.removeItem('public_fav_players_4711');
  });

  /** Rendert die Detailseite bis zur Aktionsleiste; alle Start-Requests werden mit Leerdaten beantwortet. */
  async function render(monitor: { active: boolean; activeUntil: string | null }, groups?: TournamentGroup[],
                        opts: { chessResultsId?: string; subscriptions?: Subscription[] } = {}): Promise<ComponentFixture<TournamentDetailComponent>> {
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '4711' }), queryParams: {} } } },
      ],
    }).compileComponents();
    // Diese Specs pruefen das ANGEMELDETE Verhalten (Gaeste: eigene Tests, seit 0.643.0 ohne Anmeldung offen).
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); // ngOnInit
    http.expectOne('/api/tournament-favorites?tournamentId=4711').flush([]);
    http.expectOne('/api/tournament-favorites/settings/4711').flush({ showFavoritesOnly: false });
    http.expectOne('/api/tournaments/4711').flush({
      id: 1, name: 'Schach Tirol Open', chessResultsId: opts.chessResultsId ?? '4711', location: null, date: null, totalRounds: 0, knownRounds: 0, createdAt: '', updatedAt: '',
      groups,
    });
    http.expectOne('/api/subscriptions').flush(opts.subscriptions ?? []);
    // lastKnownRounds 0 => kein Monitor-Poll, der im Test weiterliefe.
    http.expectOne('/api/tournament-monitors/4711').flush({ ...monitor, lastKnownRounds: 0 });
    http.expectOne('/api/tournaments/4711/players').flush([]);
    http.expectOne('/api/tournaments/4711/teams').flush([]);
    fixture.detectChanges();
    return fixture;
  }

  const actionsOf = (fixture: ComponentFixture<TournamentDetailComponent>): HTMLElement[] =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.action-bar a, .action-bar button'));

  /**
   * Mobil sind die Aktionen nackte Icon-Kreise (.btn-label display:none nimmt den Text auch aus dem
   * Accessibility-Tree): jeder Knopf braucht aria-label + Tooltip (Tooltip-Trigger-Klasse = Modul verdrahtet).
   */
  it('beschriftet alle Aktionen per aria-label und Tooltip', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const actions = actionsOf(fixture);
    expect(actions.length).toBe(5);
    for (const a of actions) {
      expect(a.getAttribute('aria-label')).withContext(a.outerHTML).toBeTruthy();
      expect(a.classList.contains('mat-mdc-tooltip-trigger')).withContext(a.outerHTML).toBeTrue();
    }
    // Ohne Theme-Farbe: color="primary"/"warn" war unter M3 wirkungslos und ist weg.
    expect(actions.some(a => a.hasAttribute('color'))).toBeFalse();
  });

  /** Aus: Icon 'notifications_active' (die Glocke gehoert seit F6-011 allein dem Beobachten), Beschriftung 'Beobachten'. */
  it('zeigt bei inaktiver Beobachtung die Glocke und die Start-Beschriftung', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const monitorBtn = actionsOf(fixture).find(a => a.getAttribute('aria-label') === 'tournaments.actions.monitor');
    expect(monitorBtn).toBeTruthy();
    expect(monitorBtn!.querySelector('mat-icon')!.textContent!.trim()).toBe('notifications_active');
  });

  /**
   * An: Icon 'notifications_off' (= Aktion 'Beobachtung beenden'), Beschriftung mit Endzeit — vorher
   * sahen an und aus auf dem Handy identisch aus (gleiches Icon, Label weg, color="primary" faerbte nichts).
   * Bis F6-011 war es 'visibility_off' — dasselbe Symbol heisst in der Kurzansicht „Für mich ausblenden".
   */
  it('zeigt bei aktiver Beobachtung die durchgestrichene Glocke und die Endzeit-Beschriftung', async () => {
    const fixture = await render({ active: true, activeUntil: '2026-09-09T18:30:00' });
    const monitorBtn = actionsOf(fixture).find(a => a.getAttribute('aria-label') === 'tournaments.actions.monitoringUntil');
    expect(monitorBtn).withContext('Monitor-Knopf mit monitoringUntil-Label').toBeTruthy();
    expect(monitorBtn!.querySelector('mat-icon')!.textContent!.trim()).toBe('notifications_off');
    expect(actionsOf(fixture).some(a => a.getAttribute('aria-label') === 'tournaments.actions.monitor')).toBeFalse();
  });

  // ----- Ein Begriff, eine Rangfolge (F6-011) --------------------------------

  const icons = (fixture: ComponentFixture<TournamentDetailComponent>): string[] =>
    actionsOf(fixture).map(a => a.querySelector('mat-icon')!.textContent!.trim());

  /**
   * Gemerkt wird im Kalender mit dem Lesezeichen („Merken"); hier hiess dieselbe Aktion „Abonnieren" mit Glocke —
   * man hielt es fuer ein zweites Abo. Und das durchgestrichene Auge (Kurzansicht: „Für mich ausblenden") stand
   * hier fuer „Beobachtung beenden".
   */
  it('nennt das Abo „Merken" mit Lesezeichen wie der Kalender, ohne Glocke und Auge dafuer', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const bookmark = actionsOf(fixture).find(a => a.getAttribute('aria-label') === 'tournamentDirectory.bookmark');
    expect(bookmark).withContext('Merken-Knopf').toBeTruthy();
    expect(bookmark!.querySelector('mat-icon')!.textContent!.trim()).toBe('bookmark_add');
    expect(actionsOf(fixture).some(a => (a.getAttribute('aria-label') ?? '').startsWith('tournaments.actions.subscribe'))).toBeFalse();
    expect(icons(fixture)).not.toContain('notifications');
    expect(icons(fixture).some(i => i.startsWith('visibility'))).toBeFalse();
  });

  it('zeigt ein gemerktes Turnier mit vollem Lesezeichen und „Merken aufheben"', async () => {
    const fixture = await render({ active: true, activeUntil: '2026-09-09T18:30:00' }, undefined, {
      subscriptions: [{ id: 9, crawlerTournamentId: '4711', tournamentName: 'Schach Tirol Open', subscribedAt: '', tournamentDbId: null, eventDate: null }],
    });
    const remove = actionsOf(fixture).find(a => a.getAttribute('aria-label') === 'tournamentDirectory.bookmarkRemove');
    expect(remove).withContext('Merken-aufheben-Knopf').toBeTruthy();
    expect(remove!.querySelector('mat-icon')!.textContent!.trim()).toBe('bookmark');
    expect(icons(fixture).some(i => i.startsWith('visibility'))).toBeFalse();
  });

  /** UI-Dichte-Regel: vorher fuenf gleich gewichtete mat-raised-button. Jetzt 1 primaer, 3 sekundaer, 1 Textlink. */
  it('stuft die Aktionen: Merken primaer, drei umrandet, Chess-Results als Textlink', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const actions = actionsOf(fixture);
    const has = (cls: string) => actions.filter(a => a.classList.contains(cls));
    expect(has('mat-mdc-raised-button').length).withContext('keine erhabenen Knoepfe mehr').toBe(0);
    expect(has('mat-mdc-unelevated-button').map(a => a.getAttribute('aria-label'))).toEqual(['tournamentDirectory.bookmark']);
    expect(has('mat-mdc-outlined-button').map(a => a.getAttribute('aria-label')))
      .toEqual(['tournaments.actions.monitor', 'tournaments.actions.refresh', 'tournaments.actions.share']);
    const link = actions.find(a => a.getAttribute('aria-label') === 'tournaments.actions.chessResults')!;
    expect(link.classList.contains('mat-mdc-button')).toBeTrue();
    expect(link.classList.contains('mat-mdc-outlined-button')).toBeFalse();
  });

  // ----- Handy (UX-042) -------------------------------------------------------

  /** Viewport des Test-Dokuments (das Karma-iframe) auf Handybreite stellen; Rueckgabe = zuruecksetzen (Muster turnier-navbar.spec). */
  async function narrowViewport(width: number): Promise<() => void> {
    const frame = window.frameElement as HTMLElement | null;
    if (!frame) pending('Karma laeuft nicht im iframe — der Viewport laesst sich nicht verstellen');
    const prev = frame!.style.width;
    frame!.style.width = `${width}px`;
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    expect(window.innerWidth).withContext('Viewport nicht verstellt').toBeLessThanOrEqual(width);
    return () => { frame!.style.width = prev; };
  }

  /**
   * Am Handy waren alle fuenf Aktionen nackte Symbolkreise — Glocke („Abonnieren") und Auge („Beobachten") nicht zu
   * unterscheiden; die Gruppenleiste endete abgeschnitten ohne Hinweis, dass sie scrollt.
   */
  it('am Handy behalten Merken und Beobachten ihren Text, die Gruppenleiste blendet am Rand aus', async () => {
    const restore = await narrowViewport(360);
    try {
      const fixture = await render({ active: false, activeUntil: null }, rally);
      const labelShown = (aria: string) => {
        const btn = actionsOf(fixture).find(a => a.getAttribute('aria-label') === aria)!;
        return getComputedStyle(btn.querySelector('.btn-label')!).display !== 'none';
      };
      expect(labelShown('tournamentDirectory.bookmark')).withContext('Merken mit Text').toBeTrue();
      expect(labelShown('tournaments.actions.monitor')).withContext('Beobachten mit Text').toBeTrue();
      // Die eindeutigen Symbole bleiben Kreise (Tooltip + aria-label tragen den Text).
      expect(labelShown('tournaments.actions.refresh')).toBeFalse();
      expect(labelShown('tournaments.actions.share')).toBeFalse();
      expect(labelShown('tournaments.actions.chessResults')).toBeFalse();

      const strip = (fixture.nativeElement as HTMLElement).querySelector('.group-switch') as HTMLElement;
      const style = getComputedStyle(strip);
      const mask = style.getPropertyValue('mask-image') || style.getPropertyValue('-webkit-mask-image');
      expect(mask).withContext('Rand blendet aus').toContain('linear-gradient');
    } finally {
      restore();
    }
  });

  // ----- Gruppen derselben Veranstaltung -----------------------------------

  const rally: TournamentGroup[] = [
    { chessResultsId: '1503214', label: 'Gruppe A', current: false },
    { chessResultsId: '1503215', label: 'Gruppe B', current: false },
    { chessResultsId: '1503219', label: 'Mädchen', current: false },
    { chessResultsId: '4711', label: 'Schnellschach', current: true },
  ];

  /** Vier Gruppen am selben Rallye-Tag: eine Leiste, die eigene ist gewaehlt. */
  it('zeigt die Gruppen der Veranstaltung als Umschalter, die eigene gewaehlt', async () => {
    const fixture = await render({ active: false, activeUntil: null }, rally);
    const toggles = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.group-switch mat-button-toggle'));

    expect(toggles.map(t => t.textContent?.trim())).toEqual(['Gruppe A', 'Gruppe B', 'Mädchen', 'Schnellschach']);
    expect(toggles[3].classList.contains('mat-button-toggle-checked')).toBeTrue();
  });

  /** Ein Klick oeffnet die andere Gruppe — auf demselben Reiter —, die Leiste bleibt bis dahin bei der eigenen. */
  it('oeffnet eine andere Gruppe auf demselben Reiter', async () => {
    const fixture = await render({ active: false, activeUntil: null }, rally);
    const open = spyOn(TestBed.inject(OpenTournamentService), 'open');
    fixture.componentInstance.selectedTabIndex = 2;

    const button = (fixture.nativeElement as HTMLElement).querySelectorAll('.group-switch mat-button-toggle button')[1] as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(open).toHaveBeenCalledWith('1503215', 'pairings');
    const checked = (fixture.nativeElement as HTMLElement).querySelector('.group-switch .mat-button-toggle-checked');
    expect(checked?.textContent?.trim()).toBe('Schnellschach');
  });

  it('zeigt ohne Gruppen keine Leiste', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    expect((fixture.nativeElement as HTMLElement).querySelector('.group-switch')).toBeNull();
  });

  // ----- Aktualisieren, waehrend schon ein Auftrag laeuft (I2-009) ----------

  /**
   * Runden-Monitor, Nachtabruf oder ein anderer Nutzer haben den Crawl schon angestossen — der
   * Crawler antwortet 409. Das hiess bisher „Aktualisierung konnte nicht gestartet werden",
   * obwohl die frischen Daten eine Minute spaeter da waren.
   */
  it('meldet „läuft schon" statt eines Fehlschlags, wenn der Crawl bereits läuft (409)', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const info = spyOn(TestBed.inject(SnackbarService), 'info');
    const c = fixture.componentInstance;

    c.refresh();
    expect(c.refreshing).toBeTrue();
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' })
      .flush({ error: "A crawl job for '4711' is already running." }, { status: 409, statusText: 'Conflict' });

    expect(c.refreshing).toBeFalse();
    expect(info).toHaveBeenCalledWith('tournaments.detail.refreshAlreadyRunning');
    expect(info).not.toHaveBeenCalledWith('tournaments.detail.refreshStartFailed');
    http.verify();
  });

  /** Nennt der Crawler die Nummer des laufenden Auftrags, wird er verfolgt wie ein eigener. */
  it('verfolgt bei 409 mit Auftragsnummer den laufenden Auftrag', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const info = spyOn(TestBed.inject(SnackbarService), 'info');
    const c = fixture.componentInstance;
    jasmine.clock().install();
    try {
      c.refresh();
      http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' })
        .flush({ message: 'already running', jobId: 42 }, { status: 409, statusText: 'Conflict' });
      expect(c.refreshing).toBeTrue();
      expect(info).not.toHaveBeenCalled();

      jasmine.clock().tick(2000);
      http.expectOne('/api/tournaments/crawl/42').flush({ id: 42, status: 'Running' });
      expect(c.refreshing).toBeTrue();
    } finally {
      c.ngOnDestroy();
      jasmine.clock().uninstall();
    }
    http.verify();
  });

  it('meldet andere Fehler beim Starten weiter als Fehlschlag', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const info = spyOn(TestBed.inject(SnackbarService), 'info');
    const c = fixture.componentInstance;

    c.refresh();
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' })
      .flush({ error: 'Crawl queue is full. Try again later.' }, { status: 429, statusText: 'Too Many Requests' });

    expect(c.refreshing).toBeFalse();
    expect(info).toHaveBeenCalledWith('tournaments.detail.refreshStartFailed');
    http.verify();
  });

  // ----- Abo: eine Kennung je Turnier (A5-001) ------------------------------

  const sub = (id: number, crawlerTournamentId: string): Subscription => ({
    id, crawlerTournamentId, tournamentName: 'Schach Tirol Open', subscribedAt: '', tournamentDbId: null, eventDate: null,
  });

  /**
   * Die Route traegt die Crawler-DB-Id (4711), der Server speichert das Abo aber unter der
   * chess-results-Nummer — vorher erkannte die Seite es dann nicht und zeigte „nicht gemerkt".
   */
  it('erkennt ein Abo unter der chess-results-Nummer, obwohl die Route die DB-Id traegt', async () => {
    const fixture = await render({ active: false, activeUntil: null }, undefined,
      { chessResultsId: '1234567', subscriptions: [sub(9, '1234567')] });
    expect(fixture.componentInstance.subscription?.id).toBe(9);
  });

  /** Ein Alt-Abo steht noch unter der DB-Id aus der Route — das gilt weiter. */
  it('erkennt ein Alt-Abo unter der DB-Id der Route', async () => {
    const fixture = await render({ active: false, activeUntil: null }, undefined,
      { chessResultsId: '1234567', subscriptions: [sub(3, '4711')] });
    expect(fixture.componentInstance.subscription?.id).toBe(3);
  });

  it('nimmt kein fremdes Abo', async () => {
    const fixture = await render({ active: false, activeUntil: null }, undefined,
      { chessResultsId: '1234567', subscriptions: [sub(5, '7654321')] });
    expect(fixture.componentInstance.subscription).toBeNull();
  });

  // ----- Favoriten: kein vorgetaeuschtes Speichern (W3 F6-004) ---------------

  const kollege = { snr: 7, name: 'Kollege' } as TournamentPlayer;
  const verein = { snr: 3, name: 'SK Schwaz' } as TournamentTeam;

  /** Erfolg: Stern an, die Bestaetigung erst mit der Serverantwort. */
  it('bestaetigt einen Spieler-Favoriten erst nach der Serverantwort', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const snackbar = TestBed.inject(SnackbarService);
    const quick = spyOn(snackbar, 'quick');
    const c = fixture.componentInstance;

    c.toggleFavorite(kollege);
    expect(c.isFavorite(kollege)).toBeTrue();
    expect(quick).not.toHaveBeenCalled();

    http.expectOne(r => r.method === 'POST' && r.url === '/api/tournament-favorites').flush({ id: 1, playerSnr: 7 });
    expect(c.isFavorite(kollege)).toBeTrue();
    expect(quick).toHaveBeenCalledWith('tournaments.favorites.added');
  });

  /** Netzfehler beim Hinzufuegen: Stern springt zurueck, Warnung statt „hinzugefuegt". */
  it('nimmt den Spieler-Stern zurueck und warnt, wenn das Hinzufuegen scheitert', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const snackbar = TestBed.inject(SnackbarService);
    const quick = spyOn(snackbar, 'quick');
    const warn = spyOn(snackbar, 'warn');
    const c = fixture.componentInstance;

    c.toggleFavorite(kollege);
    http.expectOne(r => r.method === 'POST' && r.url === '/api/tournament-favorites')
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(c.isFavorite(kollege)).toBeFalse();
    expect(c.hasFavorites).toBeFalse();
    expect(warn).toHaveBeenCalledWith('tournaments.favorites.saveFailed');
    expect(quick).not.toHaveBeenCalled();
  });

  /** 409 = war schon Favorit: der Server hat den gewuenschten Stand, der Stern bleibt. */
  it('behandelt 409 beim Hinzufuegen als Erfolg', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
    const c = fixture.componentInstance;

    c.toggleFavorite(kollege);
    http.expectOne(r => r.method === 'POST' && r.url === '/api/tournament-favorites')
      .flush({ message: 'Already favorited.' }, { status: 409, statusText: 'Conflict' });

    expect(c.isFavorite(kollege)).toBeTrue();
    expect(warn).not.toHaveBeenCalled();
  });

  /** 500 beim Entfernen eines Team-Favoriten: der Stern kommt wieder. */
  it('stellt den Team-Stern wieder her, wenn das Entfernen scheitert', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
    const c = fixture.componentInstance;
    c.favoriteTeamSnrs = new Set([3]);

    c.toggleTeamFavorite(verein);
    expect(c.isTeamFavorite(verein)).toBeFalse();
    http.expectOne(r => r.method === 'DELETE' && r.url === '/api/tournament-favorites/by-team/4711/3')
      .flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });

    expect(c.isTeamFavorite(verein)).toBeTrue();
    expect(warn).toHaveBeenCalledWith('tournaments.favorites.saveFailed');
  });

  /** Filter „nur Favoriten": bleibt an, aber der Nutzer erfaehrt, dass er nicht gespeichert ist. */
  it('warnt, wenn der Favoriten-Filter nicht gespeichert werden kann', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const http = TestBed.inject(HttpTestingController);
    const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
    const c = fixture.componentInstance;

    c.onFavoritesToggle(true);
    http.expectOne(r => r.method === 'PUT' && r.url === '/api/tournament-favorites/settings/4711')
      .flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });

    expect(c.showFavoritesOnly).toBeTrue();
    expect(warn).toHaveBeenCalledWith('tournaments.favorites.filterSaveFailed');
  });

  // ----- Kein Turnier: Fehlerkarte statt leerer Seite (Codereview F6-013) -----

  /** Wie `render`, aber das Turnier selbst antwortet mit `status`. */
  async function renderFailing(status: number): Promise<ComponentFixture<TournamentDetailComponent>> {
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '4711' }), queryParams: {} } } },
      ],
    }).compileComponents();
    // Diese Specs pruefen das ANGEMELDETE Verhalten (Gaeste: eigene Tests, seit 0.643.0 ohne Anmeldung offen).
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); // ngOnInit
    http.expectOne('/api/tournament-favorites?tournamentId=4711').flush([]);
    http.expectOne('/api/tournament-favorites/settings/4711').flush({ showFavoritesOnly: false });
    http.expectOne('/api/tournaments/4711').flush({ message: 'boom' }, { status, statusText: 'x' });
    http.expectOne('/api/subscriptions').flush([]);
    http.expectOne('/api/tournament-monitors/4711').flush({ active: false, activeUntil: null, lastKnownRounds: 0 });
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt bei einem Serverfehler eine Fehlerkarte mit „Erneut versuchen" und Weg zum Kalender', async () => {
    const fixture = await renderFailing(500);
    const http = TestBed.inject(HttpTestingController);
    const page = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.loadFailure).toBe('error');
    expect(page.textContent).toContain('tournaments.detail.loadTournamentFailed');
    expect(page.querySelector('.load-failed a[href="/tournaments/calendar"]'))
      .withContext('Weg zum Kalender fehlt').toBeTruthy();

    const retry = page.querySelector<HTMLButtonElement>('.load-failed button');
    expect(retry).withContext('Erneut-versuchen-Knopf fehlt').toBeTruthy();
    retry!.click();
    http.expectOne('/api/tournaments/4711').flush({
      id: 1, name: 'Schach Tirol Open', chessResultsId: '4711', location: null, date: null, totalRounds: 0, knownRounds: 0, createdAt: '', updatedAt: '',
    });
    http.expectOne('/api/tournaments/4711/players').flush([]);
    http.expectOne('/api/tournaments/4711/teams').flush([]);
    fixture.detectChanges();

    expect(fixture.componentInstance.loadFailure).toBeNull();
    expect(page.querySelector('.load-failed')).toBeNull();
    expect(page.textContent).toContain('Schach Tirol Open');
    http.verify();
  });

  it('sagt bei 404, dass es das Turnier hier nicht gibt, und verweist auf den Kalender', async () => {
    const fixture = await renderFailing(404);
    const page = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.loadFailure).toBe('notFound');
    expect(page.textContent).toContain('tournaments.detail.notFound');
    expect(page.textContent).not.toContain('tournaments.detail.loadTournamentFailed');
    expect(page.querySelector('.load-failed a[href="/tournaments/calendar"]')).toBeTruthy();
    TestBed.inject(HttpTestingController).verify();
  });

  // ----- Favoriten-Ansicht: alle drei Tabellen nach jedem Laden neu (W5 F6-024) -----

  /** Vorher frischte das Laden der Spieler nur die Spielerliste auf — die Teamliste blieb auf dem Stand ohne Spieler. */
  it('Teams vor Spielern geladen: das Team des favorisierten Spielers steht trotzdem in der Teamliste', async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '4711' }), queryParams: {} } } },
      ],
    }).compileComponents();
    // Diese Specs pruefen das ANGEMELDETE Verhalten (Gaeste: eigene Tests, seit 0.643.0 ohne Anmeldung offen).
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    const http = TestBed.inject(HttpTestingController);
    const c = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit
    http.expectOne('/api/tournament-favorites?tournamentId=4711').flush([{ id: 1, playerSnr: 1 }]);
    http.expectOne('/api/tournament-favorites/settings/4711').flush({ showFavoritesOnly: true });
    http.expectOne('/api/tournaments/4711').flush({
      id: 1, name: 'Mannschaftsmeisterschaft', chessResultsId: '4711', location: null, date: null, totalRounds: 0, knownRounds: 0, createdAt: '', updatedAt: '',
    });
    http.expectOne('/api/subscriptions').flush([]);
    http.expectOne('/api/tournament-monitors/4711').flush({ active: false, activeUntil: null, lastKnownRounds: 0 });

    http.expectOne('/api/tournaments/4711/teams').flush([{ snr: 10, name: 'Red' }, { snr: 11, name: 'Blue' }]);
    expect(c.displayedTeams).toEqual([]);
    http.expectOne('/api/tournaments/4711/players').flush([
      { snr: 1, name: 'Alice', teamName: 'Red' }, { snr: 2, name: 'Bob', teamName: 'Red' }, { snr: 3, name: 'Carol', teamName: 'Blue' },
    ]);

    expect(c.displayedPlayers.map(p => p.name)).toEqual(['Alice', 'Bob']);
    expect(c.displayedTeams.map(t => t.name)).toEqual(['Red']);
  });
});
