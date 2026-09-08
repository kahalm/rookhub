import { DestroyRef, Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { TranslateService } from '@ngx-translate/core';
import { Subject } from 'rxjs';
import { AppUpdateService } from './app-update.service';
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
