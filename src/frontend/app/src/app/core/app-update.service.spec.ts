import { DestroyRef, Injector, runInInjectionContext } from '@angular/core';
import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { TranslateService } from '@ngx-translate/core';
import { Subject } from 'rxjs';
import { AppUpdateService, SW_CHECK_TIMEOUT_MS } from './app-update.service';
import { ClientLogService } from './client-log.service';
import { SnackbarService } from './snackbar.service';

/**
 * Der Hinweis auf eine neue Fassung. Ohne ihn laeuft ein offener Tab nach einem Deploy
 * unbegrenzt auf der ALTEN Fassung weiter — der Service Worker liefert sie aus seinem
 * Zwischenspeicher aus, und zwar ohne jedes Anzeichen.
 */
describe('AppUpdateService', () => {
  let versionUpdates: Subject<{ type: string }>;
  let unrecoverable: Subject<{ reason: string }>;
  let action: Subject<void>;
  let shown: { message: string; action?: string; duration?: number }[];
  let checks: number;

  function setup(isEnabled = true): AppUpdateService {
    versionUpdates = new Subject();
    unrecoverable = new Subject();
    action = new Subject();
    shown = [];
    checks = 0;

    TestBed.configureTestingModule({
      providers: [
        {
          provide: SwUpdate,
          useValue: {
            isEnabled, versionUpdates, unrecoverable,
            checkForUpdate: () => { checks++; return Promise.resolve(false); },
          },
        },
        {
          provide: SnackbarService,
          useValue: {
            show: (message: string, opts: { action?: string; duration?: number }) => {
              shown.push({ message, ...opts });
              return { onAction: () => action };
            },
          },
        },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: ClientLogService, useValue: { report: () => {} } },
      ],
    });
    return TestBed.inject(AppUpdateService);
  }

  /** Anhaengen mit einem echten DestroyRef, damit `takeUntilDestroyed` gilt. */
  function start(service: AppUpdateService): void {
    const injector = TestBed.inject(Injector);
    runInInjectionContext(injector, () => service.start(TestBed.inject(DestroyRef)));
  }

  it('bietet bei einer neuen Fassung das Neuladen an — und zwar bleibend', () => {
    const service = setup();
    start(service);

    versionUpdates.next({ type: 'VERSION_READY' });

    expect(shown.length).toBe(1);
    expect(shown[0].message).toBe('app.updateAvailable');
    expect(shown[0].action).toBe('app.reload');
    // duration 0: ein nach drei Sekunden verschwundener Hinweis ist derselbe wie keiner.
    expect(shown[0].duration).toBe(0);
  });

  it('laedt neu, wenn der Hinweis angetippt wird', () => {
    const service = setup();
    const reload = spyOn<any>(service, 'reloadApp');
    start(service);
    versionUpdates.next({ type: 'VERSION_READY' });

    action.next();

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('sieht von sich aus nach — beim Start und wenn der Tab zurueckkommt', () => {
    // Der Service Worker prueft von allein nur beim (Neu-)Start; ohne dieses Nachsehen merkt
    // eine lange offene Seite ein Deploy nie.
    const service = setup();
    start(service);
    expect(checks).toBe(1);

    document.dispatchEvent(new Event('visibilitychange'));

    expect(checks).toBe(2);
  });

  it('tut nichts, solange es keinen Service Worker gibt', () => {
    const service = setup(false);
    start(service);

    versionUpdates.next({ type: 'VERSION_READY' });

    expect(checks).toBe(0);
    expect(shown.length).toBe(0);
  });

  it('meldet eine gescheiterte Installation, ohne den Nutzer zu behelligen', () => {
    const service = setup();
    const clientLog = TestBed.inject(ClientLogService);
    const report = spyOn(clientLog, 'report');
    start(service);

    versionUpdates.next({ type: 'VERSION_INSTALLATION_FAILED', error: 'hash' } as never);

    expect(report).toHaveBeenCalledWith('sw_install_failed', 'hash');
    expect(shown.length).withContext('Telemetrie gehoert nicht auf den Bildschirm').toBe(0);
  });
});

/** U1: sofortige Prüfung für die Nicht-gefunden-Seite — Service Worker und Server-Changelog. */
describe('AppUpdateService.checkNow / activateAndReload (U1)', () => {
  function make(sw: { isEnabled: boolean; found?: boolean; check?: () => Promise<boolean> } | null, server: string | null,
                own = '0.935.0', controls = true) {
    const activateUpdate = jasmine.createSpy('activateUpdate').and.resolveTo(true);
    const providers: unknown[] = [
      { provide: SnackbarService, useValue: { show: () => ({ onAction: () => new Subject() }) } },
      { provide: TranslateService, useValue: { instant: (k: string) => k } },
      { provide: ClientLogService, useValue: { report: () => {} } },
    ];
    if (sw) providers.push({ provide: SwUpdate, useValue: {
      isEnabled: sw.isEnabled, versionUpdates: new Subject(), unrecoverable: new Subject(),
      checkForUpdate: sw.check ?? (() => Promise.resolve(!!sw.found)), activateUpdate,
    } });
    TestBed.configureTestingModule({ providers: providers as never[] });
    const s = TestBed.inject(AppUpdateService) as any;
    s.currentVersion = own;
    s.serverVersion = () => Promise.resolve(server);
    s.replaceLocation = jasmine.createSpy('replaceLocation');
    s.reloadApp = jasmine.createSpy('reloadApp');
    s.swControlsTab = () => controls;
    return { s: s as AppUpdateService & { replaceLocation: jasmine.Spy; reloadApp: jasmine.Spy }, activateUpdate };
  }

  it('Server-Changelog neuer als die eigene Fassung → verfügbar mit Nummer', async () => {
    const { s } = make({ isEnabled: true, found: false }, '0.936.0');
    expect(await s.checkNow()).toEqual({ available: true, version: '0.936.0' });
  });

  it('gleiche Fassung, Service Worker findet nichts → nicht verfügbar', async () => {
    const { s } = make({ isEnabled: true, found: false }, '0.935.0');
    expect(await s.checkNow()).toEqual({ available: false, version: null });
  });

  it('Service Worker findet eine Fassung, Changelog unlesbar → verfügbar ohne Nummer', async () => {
    const { s } = make({ isEnabled: true, found: true }, null);
    expect(await s.checkNow()).toEqual({ available: true, version: null });
  });

  it('ohne Service-Worker-Anbieter (LeagueHub): nur das Changelog zählt, Aktualisieren = harter Reload', async () => {
    const { s } = make(null, '0.936.0');
    expect(await s.checkNow()).toEqual({ available: true, version: '0.936.0' });
    await s.activateAndReload();
    expect(s.reloadApp).toHaveBeenCalledTimes(1);
    expect(s.replaceLocation).not.toHaveBeenCalled();
  });

  // UI-Sweep 2026-11-10 (d10-rk-404-ladekreis-haengt-ohne-sw-steuerung)
  it('Service Worker steuert den Tab nicht: checkForUpdate wird gar nicht gefragt, nur das Changelog zählt', async () => {
    const check = jasmine.createSpy('checkForUpdate').and.returnValue(new Promise<boolean>(() => {}));
    const { s } = make({ isEnabled: true, check }, '0.935.0', '0.935.0', false);
    expect(await s.checkNow()).toEqual({ available: false, version: null });
    expect(check).not.toHaveBeenCalled();
  });

  it('checkForUpdate hängt: nach höchstens 3 s ist das Ergebnis da', fakeAsync(() => {
    const { s } = make({ isEnabled: true, check: () => new Promise<boolean>(() => {}) }, '0.936.0');
    let result: unknown;
    void s.checkNow().then(r => (result = r));
    tick(SW_CHECK_TIMEOUT_MS - 1);
    expect(result).toBeUndefined();
    tick(1);
    flushMicrotasks();
    expect(SW_CHECK_TIMEOUT_MS).toBeLessThanOrEqual(3000);
    expect(result).toEqual({ available: true, version: '0.936.0' });
  }));

  it('mit Service Worker: erst aktivieren, dann dieselbe Adresse frisch laden', async () => {
    const { s, activateUpdate } = make({ isEnabled: true, found: true }, '0.936.0');
    await s.activateAndReload();
    expect(activateUpdate).toHaveBeenCalledTimes(1);
    expect(s.replaceLocation).toHaveBeenCalledOnceWith(document.location.href);
    expect(s.reloadApp).not.toHaveBeenCalled();
  });
});
