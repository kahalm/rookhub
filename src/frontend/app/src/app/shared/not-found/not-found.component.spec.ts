import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Component } from '@angular/core';
import { RouterLink, Routes, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { STALE_ROUTE_RELOAD_KEY, StaleRouteReloadService } from '../../core/stale-route-reload.service';
import { ClientLogService } from '../../core/client-log.service';
import { SnackbarService } from '../../core/snackbar.service';
import { NOT_FOUND_CHECK_CAP_MS, NotFoundComponent } from './not-found.component';
import { AppUpdateCheck, AppUpdateService } from '../../core/app-update.service';

@Component({ standalone: true, template: '' })
class StubComponent {}

/** Ersatz für die Versionsprüfung (U1, Stufe 2): Ergebnis bestimmt der Test. */
function updateStub(result: Promise<AppUpdateCheck> = Promise.resolve({ available: false, version: null })) {
  return {
    checkNow: jasmine.createSpy('checkNow').and.returnValue(result),
    activateAndReload: jasmine.createSpy('activateAndReload').and.resolveTo(),
  };
}

/** Ersatz für das einmalige Neuladen (Stufe 1); Vorgabe: kein Versuch (wie ohne Service Worker). */
function reloaderStub(canTry = false, result: Promise<boolean> = Promise.resolve(false)) {
  return {
    canTry: () => canTry,
    reloadIfNewer: jasmine.createSpy('reloadIfNewer').and.returnValue(result),
  };
}

function setup(routes: Routes, update = updateStub(), reloader = reloaderStub()) {
  TestBed.configureTestingModule({
    imports: [NotFoundComponent],
    providers: [provideRouter(routes), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      { provide: AppUpdateService, useValue: update },
      { provide: StaleRouteReloadService, useValue: reloader }],
  });
  const fixture = TestBed.createComponent(NotFoundComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const links = () => fixture.debugElement.queryAll(By.directive(RouterLink))
    .map(de => de.injector.get(RouterLink).urlTree?.toString() ?? '');
  return { fixture, el, links, update, reloader };
}

/** Hinweisseite für unbekannte Adressen (UX-026): Text plus ein Weg weiter, kein stummer Sprung. */
async function render(routes: Routes, update = updateStub(), reloader = reloaderStub()) {
  const r = setup(routes, update, reloader);
  await r.fixture.whenStable();
  r.fixture.detectChanges();
  return r;
}

describe('NotFoundComponent', () => {
  it('sagt, dass es die Adresse nicht (mehr) gibt, und führt zur Startseite', async () => {
    const { el, links } = await render([{ path: '', component: StubComponent }]);
    // Im Test sind keine Übersetzungen geladen — die rohen Keys zeigen, WAS dasteht.
    expect(el.querySelector('h1')?.textContent).toContain('app.notFound.title');
    expect(el.textContent).toContain('app.notFound.text');
    expect(links()).toEqual(['/']);
  });

  it('bietet die Hilfe nur, wo die App eine Hilfeseite hat (RookHub ja, LeagueHub nein)', async () => {
    const { links } = await render([{ path: '', component: StubComponent }, { path: 'help', component: StubComponent }]);
    expect(links()).toEqual(['/', '/help']);
  });
});

/** U1: die Seite prüft beim Anzeigen sofort die Server-Version. */
describe('NotFoundComponent Versionsprüfung (U1)', () => {
  const routes: Routes = [{ path: '', component: StubComponent }];

  // fakeAsync statt whenStable: die Gesamtgrenze (UI-Sweep 2026-11-10) ist ein offener Timer, whenStable wartete auf ihn.
  it('während der Prüfung der Ladekreis statt Text', fakeAsync(() => {
    const { fixture, el, update } = setup(routes, updateStub(new Promise<AppUpdateCheck>(() => {})));
    flushMicrotasks();
    fixture.detectChanges();
    expect(update.checkNow).toHaveBeenCalledTimes(1);
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    expect(el.querySelector('.nf-checking')?.getAttribute('aria-label')).toBe('common.loading');
    expect(el.querySelector('h1')).toBeNull();
    fixture.destroy();
  }));

  it('neuere Version: Hinweis mit Nummer und „Aktualisieren“, der Knopf aktiviert und lädt neu', async () => {
    const { el, fixture, update } = await render(routes, updateStub(Promise.resolve({ available: true, version: '0.999.0' })));
    expect(el.querySelector('.nf-update')?.textContent).toContain('app.notFound.updateText');
    expect(el.textContent).not.toContain('app.notFound.text');
    const btn = el.querySelector<HTMLButtonElement>('button.nf-reload')!;
    expect(btn.textContent).toContain('app.notFound.update');
    btn.click();
    fixture.detectChanges();
    expect(update.activateAndReload).toHaveBeenCalledTimes(1);
    expect(btn.disabled).toBeTrue();
  });

  it('Service Worker meldet eine Fassung ohne bekannte Nummer: Hinweis ohne Nummer', async () => {
    const { el } = await render(routes, updateStub(Promise.resolve({ available: true, version: null })));
    expect(el.querySelector('.nf-update')?.textContent).toContain('app.notFound.updateTextPlain');
  });

  it('keine neuere Version → normale Seite ohne Knopf', async () => {
    const { el } = await render(routes);
    expect(el.textContent).toContain('app.notFound.text');
    expect(el.querySelector('button.nf-reload')).toBeNull();
  });

  // UI-Sweep 2026-11-10 (d10-rk-404-ladekreis-haengt-ohne-sw-steuerung): ein hängender Ladekreis ist ein Fehler.
  it('Prüfung hängt → nach der Gesamtgrenze (≤ 6 s) trotzdem Titel, Text und „Zur Startseite“', fakeAsync(() => {
    const { fixture, el, links } = setup(routes, updateStub(new Promise<AppUpdateCheck>(() => {})),
      reloaderStub(true, new Promise<boolean>(() => {})));
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    tick(NOT_FOUND_CHECK_CAP_MS);
    fixture.detectChanges();
    expect(NOT_FOUND_CHECK_CAP_MS).toBeLessThanOrEqual(6000);
    expect(el.querySelector('mat-spinner')).toBeNull();
    expect(el.querySelector('section.not-found h1')?.textContent).toContain('app.notFound.title');
    expect(el.textContent).toContain('app.notFound.text');
    expect(links()).toEqual(['/']);
    fixture.destroy();
  }));

  it('Prüfung scheitert → normale Seite', async () => {
    const { el } = await render(routes, updateStub(Promise.reject(new Error('offline'))));
    expect(el.textContent).toContain('app.notFound.text');
    expect(el.querySelector('button.nf-reload')).toBeNull();
  });

});

/** Stufe 1 (0.940.0): einmal automatisch auf die neuere Fassung neu laden; Stufe 2 nur, wenn das nicht geschieht. */
describe('NotFoundComponent einmaliges Neuladen (Stufe 1)', () => {
  const routes: Routes = [{ path: '', component: StubComponent }];

  it('ohne Versuch (kein Service Worker, Adresse gesperrt): kein Neuladen, gleich die Versionsprüfung, dann der Text',
    async () => {
      const { el, reloader, update } = await render(routes);
      expect(reloader.reloadIfNewer).not.toHaveBeenCalled();
      expect(update.checkNow).toHaveBeenCalledTimes(1);
      expect(el.querySelector('mat-spinner')).toBeNull();
      expect(el.textContent).toContain('app.notFound.text');
    });

  it('während der Suche Ladekreis statt Text, ohne neue Fassung danach Stufe 2 und der Text', fakeAsync(() => {
    let finish!: (reloading: boolean) => void;
    const update = updateStub();
    const { fixture, el } = setup(routes, update, reloaderStub(true, new Promise<boolean>(resolve => (finish = resolve))));
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    expect(el.querySelector('h1')).toBeNull();
    expect(update.checkNow).not.toHaveBeenCalled();

    finish(false);
    flushMicrotasks();
    fixture.detectChanges();
    expect(update.checkNow).toHaveBeenCalledTimes(1);
    expect(el.querySelector('mat-spinner')).toBeNull();
    expect(el.textContent).toContain('app.notFound.text');
  }));

  it('nicht neu geladen, Server aber neuer: Hinweis mit „Aktualisieren“', fakeAsync(() => {
    const { fixture, el } = setup(routes, updateStub(Promise.resolve({ available: true, version: '0.999.0' })),
      reloaderStub(true, Promise.resolve(false)));
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('.nf-update')?.textContent).toContain('app.notFound.updateText');
    expect(el.querySelector('button.nf-reload')).not.toBeNull();
  }));

  it('wird neu geladen: der Ladekreis bleibt, kein Aufblitzen des Textes und keine Versionsprüfung', fakeAsync(() => {
    const update = updateStub(Promise.resolve({ available: true, version: '0.999.0' }));
    const { fixture, el } = setup(routes, update, reloaderStub(true, Promise.resolve(true)));
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    expect(el.querySelector('h1')).toBeNull();
    expect(update.checkNow).not.toHaveBeenCalled();
  }));

  it('meldet dem Dienst, wenn die Seite verlassen wurde, und prüft danach nicht mehr', fakeAsync(() => {
    let finish!: (reloading: boolean) => void;
    const update = updateStub();
    const { fixture, reloader } = setup(routes, update,
      reloaderStub(true, new Promise<boolean>(resolve => (finish = resolve))));
    const cancelled = reloader.reloadIfNewer.calls.mostRecent().args[0] as () => boolean;
    expect(cancelled()).toBeFalse();
    fixture.destroy();
    expect(cancelled()).toBeTrue();
    finish(false);
    flushMicrotasks();
    expect(update.checkNow).not.toHaveBeenCalled();
  }));
});

/**
 * Beide Stufen mit den ECHTEN Diensten: nur Netz (`fetch` auf version.json), Service Worker und harter Reload sind
 * ersetzt. Deckt LeagueHub ab (kein `SwUpdate`-Anbieter) und die schon einmal neu geladene Adresse.
 */
describe('NotFoundComponent mit echten Diensten (Stufe 1 + Fallback)', () => {
  const routes: Routes = [{ path: '', component: StubComponent }];
  const address = () => document.location.pathname + document.location.search;
  let appReload: jasmine.Spy;
  let staleReload: jasmine.Spy;

  beforeEach(() => {
    sessionStorage.removeItem(STALE_ROUTE_RELOAD_KEY);
    type Reloads = { reloadApp: () => void };
    appReload = spyOn(AppUpdateService.prototype as unknown as Reloads, 'reloadApp');
    staleReload = spyOn(StaleRouteReloadService.prototype as unknown as Reloads, 'reloadApp');
    // Im Testbrowser steuert kein Service Worker den Tab; die Fälle mit `swStub` tun so, als ob.
    type Controls = { swControlsTab: () => boolean };
    spyOn(StaleRouteReloadService.prototype as unknown as Controls, 'swControlsTab').and.returnValue(true);
    spyOn(AppUpdateService.prototype as unknown as Controls, 'swControlsTab').and.returnValue(true);
  });
  afterEach(() => sessionStorage.removeItem(STALE_ROUTE_RELOAD_KEY));

  function serverAnswers(body: unknown, status = 200) {
    return spyOn(window, 'fetch').and.callFake(() => Promise.resolve(new Response(JSON.stringify(body), { status })));
  }

  function swStub() {
    return {
      isEnabled: true,
      checkForUpdate: jasmine.createSpy('checkForUpdate').and.resolveTo(false),
      activateUpdate: jasmine.createSpy('activateUpdate').and.resolveTo(false),
    };
  }

  async function renderReal(sw: ReturnType<typeof swStub> | null) {
    TestBed.configureTestingModule({
      imports: [NotFoundComponent],
      providers: [provideRouter(routes), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: SnackbarService, useValue: { show: () => undefined } },
        { provide: ClientLogService, useValue: { report: () => undefined } },
        ...(sw ? [{ provide: SwUpdate, useValue: sw }] : [])],
    });
    const fixture = TestBed.createComponent(NotFoundComponent);
    const el = fixture.nativeElement as HTMLElement;
    // Bis der Ladekreis weg ist (echte Promises, kein fakeAsync wegen fetch/Response).
    for (let i = 0; i < 50; i++) {
      fixture.detectChanges();
      if (!el.querySelector('mat-spinner')) break;
      await new Promise(r => setTimeout(r, 0));
    }
    fixture.detectChanges();
    return { fixture, el };
  }

  it('kein Service Worker (LeagueHub), version.json neuer → Hinweis; der Knopf lädt einfach neu', async () => {
    const fetchSpy = serverAnswers({ version: '99999.0.0' });
    const { el, fixture } = await renderReal(null);
    expect(fetchSpy).toHaveBeenCalled();
    expect(el.querySelector('.nf-update')?.textContent).toContain('app.notFound.updateText');
    el.querySelector<HTMLButtonElement>('button.nf-reload')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(appReload).toHaveBeenCalledTimes(1);
    expect(staleReload).not.toHaveBeenCalled();
  });

  it('kein Service Worker, version.json fehlt → normale Seite, kein Absturz', async () => {
    serverAnswers({}, 404);
    const { el } = await renderReal(null);
    expect(el.textContent).toContain('app.notFound.text');
    expect(el.querySelector('button.nf-reload')).toBeNull();
  });

  it('Adresse schon einmal neu geladen, Server neuer → kein zweites Neuladen, sondern der Hinweis', async () => {
    sessionStorage.setItem(STALE_ROUTE_RELOAD_KEY, JSON.stringify([address()]));
    serverAnswers({ version: '99999.0.0' });
    const sw = swStub();
    const { el } = await renderReal(sw);
    expect(sw.activateUpdate).not.toHaveBeenCalled();
    expect(staleReload).not.toHaveBeenCalled();
    expect(el.querySelector('.nf-update')?.textContent).toContain('app.notFound.updateText');
  });

  it('Stufe 1 aktiviert nichts, Server neuer → Hinweis (die Sperre ist wieder frei)', async () => {
    serverAnswers({ version: '99999.0.0' });
    const sw = swStub();
    const { el } = await renderReal(sw);
    expect(sw.activateUpdate).toHaveBeenCalledTimes(1);
    expect(staleReload).not.toHaveBeenCalled();
    expect(el.querySelector('.nf-update')?.textContent).toContain('app.notFound.updateText');
    expect(JSON.parse(sessionStorage.getItem(STALE_ROUTE_RELOAD_KEY) ?? '[]')).not.toContain(address());
  });

  // UI-Sweep 2026-11-10 (d10-rk-404-ladekreis-haengt-ohne-sw-steuerung)
  it('Service Worker steuert den Tab nicht, checkForUpdate hängt → keine Suche, gleich die Meldung', async () => {
    (StaleRouteReloadService.prototype as unknown as { swControlsTab: jasmine.Spy }).swControlsTab.and.returnValue(false);
    (AppUpdateService.prototype as unknown as { swControlsTab: jasmine.Spy }).swControlsTab.and.returnValue(false);
    serverAnswers({}, 404);
    const sw = swStub();
    sw.checkForUpdate.and.returnValue(new Promise<boolean>(() => {}));
    const { el } = await renderReal(sw);
    expect(sw.checkForUpdate).not.toHaveBeenCalled();
    expect(el.querySelector('mat-spinner')).toBeNull();
    expect(el.textContent).toContain('app.notFound.text');
  });

  it('Stufe 1 lädt neu → Ladekreis bleibt, keine Versionsprüfung', async () => {
    const fetchSpy = serverAnswers({ version: '99999.0.0' });
    const sw = swStub();
    sw.activateUpdate.and.resolveTo(true);
    const { el } = await renderReal(sw);
    expect(staleReload).toHaveBeenCalledTimes(1);
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    expect(el.querySelector('h1')).toBeNull();
  });
});
