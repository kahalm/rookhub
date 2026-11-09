import { TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Component } from '@angular/core';
import { RouterLink, Routes, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { StaleRouteReloadService } from '../../core/stale-route-reload.service';
import { NotFoundComponent } from './not-found.component';

@Component({ standalone: true, template: '' })
class StubComponent {}

/** Hinweisseite für unbekannte Adressen (UX-026): Text plus ein Weg weiter, kein stummer Sprung. */
function render(routes: Routes): HTMLElement & { links: string[] } {
  TestBed.configureTestingModule({
    imports: [NotFoundComponent],
    providers: [provideRouter(routes), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
  });
  const fixture = TestBed.createComponent(NotFoundComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement & { links: string[] };
  el.links = fixture.debugElement.queryAll(By.directive(RouterLink))
    .map(de => de.injector.get(RouterLink).urlTree?.toString() ?? '');
  return el;
}

describe('NotFoundComponent', () => {
  it('sagt, dass es die Adresse nicht (mehr) gibt, und führt zur Startseite', () => {
    const el = render([{ path: '', component: StubComponent }]);
    // Im Test sind keine Übersetzungen geladen — die rohen Keys zeigen, WAS dasteht.
    expect(el.querySelector('h1')?.textContent).toContain('app.notFound.title');
    expect(el.textContent).toContain('app.notFound.text');
    expect(el.links).toEqual(['/']);
  });

  it('bietet die Hilfe nur, wo die App eine Hilfeseite hat (RookHub ja, LeagueHub nein)', () => {
    const el = render([{ path: '', component: StubComponent }, { path: 'help', component: StubComponent }]);
    expect(el.links).toEqual(['/', '/help']);
  });

  describe('Suche nach einer neueren Fassung', () => {
    function setup(canTry: boolean, result: Promise<boolean>) {
      const reloader = {
        canTry: () => canTry,
        reloadIfNewer: jasmine.createSpy('reloadIfNewer').and.returnValue(result),
      };
      TestBed.configureTestingModule({
        imports: [NotFoundComponent],
        providers: [
          provideRouter([{ path: '', component: StubComponent }]), provideNoopAnimations(),
          provideTranslateService({ fallbackLang: 'en' }),
          { provide: StaleRouteReloadService, useValue: reloader },
        ],
      });
      const fixture = TestBed.createComponent(NotFoundComponent);
      fixture.detectChanges();
      return { fixture, reloader, el: fixture.nativeElement as HTMLElement };
    }

    it('ohne Versuch: sofort der Text, kein Ladekreis', () => {
      const { el, reloader } = setup(false, Promise.resolve(false));
      expect(el.querySelector('mat-spinner')).toBeNull();
      expect(el.querySelector('h1')).not.toBeNull();
      expect(reloader.reloadIfNewer).not.toHaveBeenCalled();
    });

    it('während der Suche Ladekreis statt Text, ohne neue Fassung danach der Text', fakeAsync(() => {
      let finish!: (reloading: boolean) => void;
      const { fixture, el } = setup(true, new Promise<boolean>(resolve => (finish = resolve)));
      expect(el.querySelector('mat-spinner')).not.toBeNull();
      expect(el.querySelector('h1')).toBeNull();

      finish(false);
      flushMicrotasks();
      fixture.detectChanges();
      expect(el.querySelector('mat-spinner')).toBeNull();
      expect(el.querySelector('h1')).not.toBeNull();
    }));

    it('wird neu geladen: der Ladekreis bleibt, kein Aufblitzen des Textes', fakeAsync(() => {
      const { fixture, el } = setup(true, Promise.resolve(true));
      flushMicrotasks();
      fixture.detectChanges();
      expect(el.querySelector('mat-spinner')).not.toBeNull();
      expect(el.querySelector('h1')).toBeNull();
    }));

    it('meldet dem Dienst, wenn die Seite verlassen wurde', () => {
      const { fixture, reloader } = setup(true, new Promise<boolean>(() => undefined));
      const cancelled = reloader.reloadIfNewer.calls.mostRecent().args[0] as () => boolean;
      expect(cancelled()).toBeFalse();
      fixture.destroy();
      expect(cancelled()).toBeTrue();
    });
  });
});
