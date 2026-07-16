import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
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
import { SnackbarService } from './core/snackbar.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { ThemeService } from './core/theme.service';
import { DISCORD_INVITE_URL } from './core/community';

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

  it('exposes the Discord community invite for the footer link', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance.discordUrl).toBe(DISCORD_INVITE_URL);
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
      const reloadSpy = spyOn<any>(fixture.componentInstance, 'reloadApp');

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
      const reloadSpy = spyOn<any>(fixture.componentInstance, 'reloadApp');

      unrecoverable.next({ reason: 'hash mismatch' });
      await fixture.componentInstance.swRecovery;

      expect(reloadSpy).not.toHaveBeenCalled();
    });
  });
});
