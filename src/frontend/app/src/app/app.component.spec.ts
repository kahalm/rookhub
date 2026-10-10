import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { Router } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';
import { TranslateService } from '@ngx-translate/core';
import { AppComponent } from './app.component';
import { LocaleService } from './core/locale.service';
import { AuthService } from './core/auth.service';
import { MenuService } from './core/menu.service';
import { DiscordLinkService } from './core/discord-link.service';
import { OfflineQueueService } from './core/offline-queue.service';
import { OfflinePrefetchService } from './core/offline-prefetch.service';
import { PwaInstallService } from './core/pwa-install.service';
import { ClientLogService } from './core/client-log.service';
import { AppUpdateService } from './core/app-update.service';
import { SnackbarService } from './core/snackbar.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { ThemeService } from './core/theme.service';
import { DISCORD_INVITE_URL } from './core/community';
import { environment } from '../environments/environment';

// Sichert das v0.181.1-Refactoring ab: die langlebigen Root-Subscriptions
// (router.events, swUpdate.versionUpdates/unrecoverable) hängen jetzt an
// takeUntilDestroyed → nach dem Zerstören der Komponente bleibt kein Observer
// auf den Quell-Streams zurück.
describe('AppComponent lifecycle', () => {
  let routerEvents: Subject<unknown>;
  let versionUpdates: Subject<unknown>;
  let unrecoverable: Subject<unknown>;

  beforeEach(() => {
    routerEvents = new Subject<unknown>();
    versionUpdates = new Subject<unknown>();
    unrecoverable = new Subject<unknown>();

    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: Router, useValue: { events: routerEvents } },
        { provide: SwUpdate, useValue: { isEnabled: true, versionUpdates, unrecoverable, checkForUpdate: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {} } },
        { provide: AuthService, useValue: { isLoggedIn: false, isAdmin: false, isImpersonating: false } },
        { provide: MenuService, useValue: {} },
        { provide: DiscordLinkService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: OfflineQueueService, useValue: {} },
        { provide: OfflinePrefetchService, useValue: { prefetchAll: () => {} } },
        { provide: ClientLogService, useValue: { report: () => {} } },
        { provide: StockfishService, useValue: {} },
        { provide: AnalysisEngineService, useValue: {} },
        { provide: ThemeService, useValue: {} },
        { provide: PwaInstallService, useValue: { isAndroid: false, isInstalled: () => false } },
      ],
    });
    // Template + dessen Imports entfernen → nur die Constructor-/Lifecycle-Logik testen.
    TestBed.overrideComponent(AppComponent, { set: { template: '', imports: [] } });
  });

  it('verlinkt jeden Schnellstart-Eintrag in seinen Modus (Matt-Kurs sequenziell)', () => {
    // Der Schnellstart erscheint direkt nach der Registrierung — dort ist „wo klicke ich jetzt?"
    // die eigentliche Frage, deshalb ist jeder Eintrag ein Link und keine bloße Beschreibung.
    const fixture = TestBed.createComponent(AppComponent);
    const items = fixture.componentInstance.quickstartItems;

    expect(items.map(i => i.key)).toEqual(['random', 'mate', 'endless', 'guess', 'daily', 'weekly']);
    expect(items.map(i => i.link)).toEqual([
      '/puzzles',
      `/courses/${AppComponent.MateCourseBookId}/sequential`,
      '/puzzles/endless',
      '/guess',
      '/puzzles/daily/today',
      '/weekly',
    ]);
    // Sequenziell ist Absicht: die Mattaufgaben stehen im Kurs nach Schwierigkeit.
    expect(items.find(i => i.key === 'mate')!.link).toContain('/sequential');
    expect(items.every(i => i.icon.length > 0)).toBeTrue();
  });

  it('tears down its root-level subscriptions on destroy', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges(); // ngOnInit → legt die Subscriptions an

    expect(routerEvents.observed).toBe(true);
    expect(versionUpdates.observed).toBe(true);
    expect(unrecoverable.observed).toBe(true);

    fixture.destroy();

    expect(routerEvents.observed).toBe(false);
    expect(versionUpdates.observed).toBe(false);
    expect(unrecoverable.observed).toBe(false);
  });

  it('reports VERSION_INSTALLATION_FAILED to the client log (sw_install_failed)', () => {
    const clientLog = TestBed.inject(ClientLogService);
    const reportSpy = spyOn(clientLog, 'report');
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    versionUpdates.next({ type: 'VERSION_INSTALLATION_FAILED', error: 'Hash mismatch (cacheBustedFetchFromNetwork)' });

    expect(reportSpy).toHaveBeenCalledWith('sw_install_failed', 'Hash mismatch (cacheBustedFetchFromNetwork)');
  });

  // Anti-Eviction (angular/angular#36539): beim Start wird persistenter Storage angefordert;
  // nur eine Ablehnung wird gemeldet, bereits persistenter Storage fragt nicht erneut an.
  describe('persistent storage request', () => {
    function makeWithStorage(storage: Partial<StorageManager> | undefined) {
      const fixture = TestBed.createComponent(AppComponent);
      spyOn<any>(fixture.componentInstance, 'storageManager').and.returnValue(storage);
      fixture.detectChanges(); // ngOnInit → requestPersistentStorage
      return fixture;
    }

    it('requests persist() and reports a denial to the client log', async () => {
      const clientLog = TestBed.inject(ClientLogService);
      const reportSpy = spyOn(clientLog, 'report');
      const persist = jasmine.createSpy('persist').and.resolveTo(false);
      const fixture = makeWithStorage({ persisted: () => Promise.resolve(false), persist });

      await fixture.componentInstance.storagePersist;

      expect(persist).toHaveBeenCalled();
      expect(reportSpy).toHaveBeenCalledWith('storage_persist_denied');
    });

    it('skips persist() when storage is already persistent and stays silent on grant', async () => {
      const clientLog = TestBed.inject(ClientLogService);
      const reportSpy = spyOn(clientLog, 'report');
      const persist = jasmine.createSpy('persist').and.resolveTo(true);
      const fixture = makeWithStorage({ persisted: () => Promise.resolve(true), persist });

      await fixture.componentInstance.storagePersist;

      expect(persist).not.toHaveBeenCalled();
      expect(reportSpy).not.toHaveBeenCalled();
    });
  });

  // Prod-Vorfall 2026-07-15: UNRECOVERABLE_STATE → blinder reload() heilte nichts und die App
  // hing in einer Endlos-Reload-Schleife. Der Handler räumt jetzt SW+Caches weg und lädt pro
  // Tab-Session höchstens EINMAL neu (sessionStorage-Guard) — und meldet das Event via ClientLog.
  describe('service worker recovery (unrecoverable)', () => {
    const GUARD_KEY = 'rookhub_sw_recovery_reload';

    afterEach(() => sessionStorage.removeItem(GUARD_KEY));

    it('reports, sets the guard and reloads exactly once', async () => {
      sessionStorage.removeItem(GUARD_KEY);
      const clientLog = TestBed.inject(ClientLogService);
      const reportSpy = spyOn(clientLog, 'report');
      const fixture = TestBed.createComponent(AppComponent);
      fixture.detectChanges();
      const reloadSpy = spyOn<any>(TestBed.inject(AppUpdateService), 'reloadApp');

      unrecoverable.next({ reason: 'hash mismatch' });
      await fixture.componentInstance.swRecovery; // async-Selbstheilung deterministisch abwarten

      expect(reportSpy).toHaveBeenCalledWith('sw_unrecoverable', 'hash mismatch');
      expect(sessionStorage.getItem(GUARD_KEY)).toBe('1');
      expect(reloadSpy).toHaveBeenCalledTimes(1);
    });

    it('does NOT reload again when the guard is already set (no reload loop)', async () => {
      sessionStorage.setItem(GUARD_KEY, '1');
      const fixture = TestBed.createComponent(AppComponent);
      fixture.detectChanges();
      const reloadSpy = spyOn<any>(TestBed.inject(AppUpdateService), 'reloadApp');

      unrecoverable.next({ reason: 'hash mismatch' });
      await fixture.componentInstance.swRecovery;

      expect(reloadSpy).not.toHaveBeenCalled();
    });
  });
});

describe('AppComponent App-Vollbild', () => {
  let current: Element | null;

  beforeEach(() => {
    current = null;
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: Router, useValue: { events: new Subject<unknown>() } },
        { provide: SwUpdate, useValue: { isEnabled: false, versionUpdates: new Subject<unknown>(), unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {} } },
        { provide: AuthService, useValue: { isLoggedIn: false, isAdmin: false, isImpersonating: false } },
        { provide: MenuService, useValue: {} },
        { provide: DiscordLinkService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: OfflineQueueService, useValue: {} },
        { provide: OfflinePrefetchService, useValue: { prefetchAll: () => {} } },
        { provide: ClientLogService, useValue: { report: () => {} } },
        { provide: StockfishService, useValue: {} },
        { provide: AnalysisEngineService, useValue: {} },
        { provide: ThemeService, useValue: { init: () => {} } },
        { provide: PwaInstallService, useValue: { captureBeforeInstallPrompt: () => {} } },
      ],
    });
    TestBed.overrideComponent(AppComponent, { set: { template: `
      @if (appFullscreen) { <button class="app-fs-exit" (click)="exitAppFullscreen()"></button> }
    ` } });
    spyOnProperty(document, 'fullscreenElement', 'get').and.callFake(() => current);
  });

  it('blendet Kopf-/Fußleiste nur im DOKUMENT-Vollbild aus (Host-Klasse), nicht beim Brett-Vollbild', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.appFullscreen).toBeFalse();

    current = document.createElement('div');            // nur ein Brett im Vollbild
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(fixture.componentInstance.appFullscreen).toBeFalse();

    current = document.documentElement;                  // die ganze GUI
    document.dispatchEvent(new Event('fullscreenchange'));
    fixture.detectChanges();
    expect(fixture.componentInstance.appFullscreen).toBeTrue();
    expect((fixture.nativeElement as HTMLElement).classList).toContain('app-fullscreen');
    expect(fixture.nativeElement.querySelector('.app-fs-exit')).not.toBeNull();
  });

  // UI-Review footer-short: die Hülle ist eine Spalte über die ganze Fensterhöhe, <main> nimmt den Rest — die
  // Fußzeile sitzt damit auch unter einer kurzen Seite am unteren Rand.
  it('hält die Fußzeile auch unter kurzem Inhalt am unteren Fensterrand', () => {
    TestBed.overrideComponent(AppComponent, { set: { template: `<main><p>kurz</p></main><footer class="ft">f</footer>` } });
    const fixture = TestBed.createComponent(AppComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(getComputedStyle(host).display).toBe('flex');
    expect(getComputedStyle(host).flexDirection).toBe('column');
    const footer = host.querySelector('.ft') as HTMLElement;
    expect(Math.round(footer.getBoundingClientRect().bottom)).toBeGreaterThanOrEqual(window.innerHeight - 1);
    host.remove();
  });

  it('der schwebende Knopf verlässt das Vollbild', () => {
    const exit = spyOn(document, 'exitFullscreen').and.returnValue(Promise.resolve());
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    current = document.documentElement;
    document.dispatchEvent(new Event('fullscreenchange'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.app-fs-exit') as HTMLButtonElement).click();

    expect(exit).toHaveBeenCalled();
  });
});

// F1-002: ein ?dl=-Link verknüpft ein angemeldetes Konto nie ohne Rückfrage — der Token bindet nur die
// Discord-ID, nicht den Empfänger; ein fremder Link verknüpfte sonst still das Discord-Konto des Absenders.
describe('AppComponent Discord-Link (?dl=)', () => {
  let routerEvents: Subject<unknown>;
  let auth: { isLoggedIn: boolean; isAdmin: boolean; isImpersonating: boolean };
  let discordLink: { confirmAndLink: jasmine.Spy; link: jasmine.Spy; stash: jasmine.Spy };
  let snackbar: { warn: jasmine.Spy; info: jasmine.Spy };
  let originalUrl: string;

  beforeEach(() => {
    originalUrl = window.location.pathname + window.location.search;
    routerEvents = new Subject<unknown>();
    auth = { isLoggedIn: true, isAdmin: false, isImpersonating: false };
    discordLink = {
      confirmAndLink: jasmine.createSpy('confirmAndLink').and.returnValue(of('declined')),
      link: jasmine.createSpy('link').and.returnValue(of({})),
      stash: jasmine.createSpy('stash'),
    };
    snackbar = { warn: jasmine.createSpy('warn'), info: jasmine.createSpy('info') };
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: Router, useValue: { events: routerEvents } },
        { provide: SwUpdate, useValue: { isEnabled: false, versionUpdates: new Subject<unknown>(), unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {} } },
        { provide: AuthService, useValue: auth },
        { provide: MenuService, useValue: {} },
        { provide: DiscordLinkService, useValue: discordLink },
        { provide: SnackbarService, useValue: snackbar },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: OfflineQueueService, useValue: {} },
        { provide: OfflinePrefetchService, useValue: { prefetchAll: () => {} } },
        { provide: ClientLogService, useValue: { report: () => {} } },
        { provide: StockfishService, useValue: {} },
        { provide: AnalysisEngineService, useValue: {} },
        { provide: ThemeService, useValue: {} },
        { provide: PwaInstallService, useValue: { isAndroid: false, isInstalled: () => false } },
      ],
    });
    TestBed.overrideComponent(AppComponent, { set: { template: '', imports: [] } });
  });

  afterEach(() => window.history.replaceState({}, '', originalUrl));

  function openWith(query: string): void {
    window.history.replaceState({}, '', window.location.pathname + query);
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();          // ngOnInit → abonniert router.events
    routerEvents.next({});
  }

  it('angemeldet: fragt nach (confirmAndLink) statt sofort zu verknüpfen, und nimmt dl aus der URL', () => {
    openWith('?dl=body.sig&x=1');
    expect(discordLink.confirmAndLink).toHaveBeenCalledOnceWith('body.sig');
    expect(discordLink.link).not.toHaveBeenCalled();
    expect(window.location.search).toBe('?x=1');
  });

  it('anonym: merkt vor, ohne zu fragen oder zu verknüpfen', () => {
    auth.isLoggedIn = false;
    openWith('?dl=body.sig');
    expect(discordLink.stash).toHaveBeenCalledOnceWith('body.sig');
    expect(discordLink.confirmAndLink).not.toHaveBeenCalled();
    expect(discordLink.link).not.toHaveBeenCalled();
    expect(snackbar.warn).toHaveBeenCalledWith('profile.discord.stashed');
  });
});
