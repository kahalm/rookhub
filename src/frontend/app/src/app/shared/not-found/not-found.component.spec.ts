import { TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Component } from '@angular/core';
import { RouterLink, Routes, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { StaleRouteReloadService } from '../../core/stale-route-reload.service';
import { NotFoundComponent } from './not-found.component';
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

  it('während der Prüfung der Ladekreis statt Text', async () => {
    const { el, update } = await render(routes, updateStub(new Promise<AppUpdateCheck>(() => {})));
    expect(update.checkNow).toHaveBeenCalledTimes(1);
    expect(el.querySelector('mat-spinner')).not.toBeNull();
    expect(el.querySelector('.nf-checking')?.getAttribute('aria-label')).toBe('common.loading');
    expect(el.querySelector('h1')).toBeNull();
  });

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
