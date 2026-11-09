import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import {
  STALE_ROUTE_CHECK_TIMEOUT_MS, STALE_ROUTE_RELOAD_KEY, StaleRouteReloadService,
} from './stale-route-reload.service';

/** Einmal neu laden, wenn eine alte Fassung eine Adresse nicht kennt — und nie in einer Schleife. */
describe('StaleRouteReloadService', () => {
  let check: jasmine.Spy;
  let activate: jasmine.Spy;
  let reload: jasmine.Spy;

  function setup(sw: 'on' | 'off' | 'missing' = 'on'): StaleRouteReloadService {
    check = jasmine.createSpy('checkForUpdate').and.resolveTo(true);
    activate = jasmine.createSpy('activateUpdate').and.resolveTo(true);
    TestBed.configureTestingModule({
      providers: sw === 'missing' ? [] : [{
        provide: SwUpdate,
        useValue: { isEnabled: sw === 'on', checkForUpdate: check, activateUpdate: activate },
      }],
    });
    const service = TestBed.inject(StaleRouteReloadService);
    reload = spyOn(service as unknown as { reloadApp(): void }, 'reloadApp');
    return service;
  }

  /** Lässt den Ablauf durchlaufen; der Timer wird beim Ende aufgeräumt, nichts bleibt in der fakeAsync-Zone offen. */
  function run(service: StaleRouteReloadService, cancelled?: () => boolean): boolean | undefined {
    let result: boolean | undefined;
    void service.reloadIfNewer(cancelled).then(r => (result = r));
    flushMicrotasks();
    return result;
  }

  beforeEach(() => sessionStorage.removeItem(STALE_ROUTE_RELOAD_KEY));
  afterEach(() => sessionStorage.removeItem(STALE_ROUTE_RELOAD_KEY));

  it('neue Fassung: aktiviert und lädt genau einmal neu', fakeAsync(() => {
    const service = setup();
    expect(run(service)).toBeTrue();
    expect(activate).toHaveBeenCalledTimes(1);
    expect(reload).toHaveBeenCalledTimes(1);
  }));

  it('schon geladene Fassung (checkForUpdate meldet false): der Tab wird trotzdem umgestellt und neu geladen', fakeAsync(() => {
    const service = setup();
    check.and.resolveTo(false);
    expect(run(service)).toBeTrue();
    expect(reload).toHaveBeenCalledTimes(1);
  }));

  it('zweiter Aufruf derselben Adresse: kein Neuladen, keine Suche', fakeAsync(() => {
    const service = setup();
    run(service);
    check.calls.reset();
    reload.calls.reset();
    expect(service.canTry()).toBeFalse();
    expect(run(service)).toBeFalse();
    expect(check).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  }));

  it('eine andere Adresse hat ihren eigenen Versuch', fakeAsync(() => {
    sessionStorage.setItem(STALE_ROUTE_RELOAD_KEY, JSON.stringify(['/eine/andere/adresse']));
    const service = setup();
    expect(run(service)).toBeTrue();
    expect(reload).toHaveBeenCalledTimes(1);
  }));

  it('keine neue Fassung: kein Neuladen, Sperre wieder frei', fakeAsync(() => {
    const service = setup();
    check.and.resolveTo(false);
    activate.and.resolveTo(false);
    expect(run(service)).toBeFalse();
    expect(reload).not.toHaveBeenCalled();
    expect(service.canTry()).toBeTrue();
  }));

  it('Service Worker aus: nichts passiert', fakeAsync(() => {
    const service = setup('off');
    expect(service.canTry()).toBeFalse();
    expect(run(service)).toBeFalse();
    expect(check).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  }));

  it('ohne SwUpdate-Anbieter (LeagueHub): nichts passiert', fakeAsync(() => {
    const service = setup('missing');
    expect(service.canTry()).toBeFalse();
    expect(run(service)).toBeFalse();
    expect(reload).not.toHaveBeenCalled();
  }));

  it('Fehler bei der Suche: kein Neuladen, keine Aktivierung', fakeAsync(() => {
    const service = setup();
    check.and.rejectWith(new Error('offline'));
    expect(run(service)).toBeFalse();
    expect(activate).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
    expect(service.canTry()).toBeTrue();
  }));

  it('Fehler beim Aktivieren: kein Neuladen', fakeAsync(() => {
    const service = setup();
    activate.and.rejectWith(new Error('kaputt'));
    expect(run(service)).toBeFalse();
    expect(reload).not.toHaveBeenCalled();
  }));

  it('Zeitgrenze: eine hängende Suche gibt die Seite frei, später passiert nichts mehr', fakeAsync(() => {
    const service = setup();
    let finishCheck!: (found: boolean) => void;
    check.and.returnValue(new Promise<boolean>(resolve => (finishCheck = resolve)));
    let result: boolean | undefined;
    void service.reloadIfNewer().then(r => (result = r));

    tick(STALE_ROUTE_CHECK_TIMEOUT_MS - 1);
    expect(result).toBeUndefined();
    tick(1);
    expect(result).toBeFalse();

    finishCheck(true);
    flushMicrotasks();
    expect(activate).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  }));

  it('Seite inzwischen verlassen: nichts mehr auslösen', fakeAsync(() => {
    const service = setup();
    expect(run(service, () => true)).toBeFalse();
    expect(activate).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  }));

  describe('Speicher nicht nutzbar: lieber kein Neuladen', () => {
    it('Lesen wirft', fakeAsync(() => {
      const service = setup();
      spyOn(Storage.prototype, 'getItem').and.throwError('gesperrt');
      expect(service.canTry()).toBeFalse();
      expect(run(service)).toBeFalse();
      expect(reload).not.toHaveBeenCalled();
    }));

    it('Schreiben wirft: es wird gar nicht erst aktiviert', fakeAsync(() => {
      const service = setup();
      spyOn(Storage.prototype, 'setItem').and.throwError('voll');
      expect(run(service)).toBeFalse();
      expect(activate).not.toHaveBeenCalled();
      expect(reload).not.toHaveBeenCalled();
    }));

    it('Inhalt unbrauchbar', fakeAsync(() => {
      sessionStorage.setItem(STALE_ROUTE_RELOAD_KEY, '{kaputt');
      const service = setup();
      expect(service.canTry()).toBeFalse();
      expect(run(service)).toBeFalse();
      expect(reload).not.toHaveBeenCalled();
    }));
  });
});
