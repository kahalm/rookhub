import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TrainingGoalsComponent } from './training-goals.component';

describe('TrainingGoalsComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [TrainingGoalsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TrainingGoalsComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

/**
 * Reiter-Zustand: ?tab= wird beim Öffnen übernommen und beim Wechsel zurückgeschrieben
 * (Deep-Link + Reload-fest, ohne History-Eintrag je Klick).
 */
describe('TrainingGoalsComponent Reiter', () => {
  function make(tab: string | null) {
    const route: any = { snapshot: { queryParamMap: { get: (k: string) => (k === 'tab' ? tab : null) } } };
    const router: any = { navigate: jasmine.createSpy('navigate') };
    const service: any = {};
    const comp = new TrainingGoalsComponent(service, {} as any, { instant: (k: string) => k } as any, route, router);
    return { comp, router };
  }

  it('übernimmt den Reiter aus ?tab= (unbekannt/fehlend → erster)', () => {
    const a = make('log');
    (a.comp as any).initTabFromUrl();
    expect(a.comp.tabIndex).toBe(2);

    const b = make('chessable');
    (b.comp as any).initTabFromUrl();
    expect(b.comp.tabIndex).toBe(3);

    const c = make('gibtsnicht');
    (c.comp as any).initTabFromUrl();
    expect(c.comp.tabIndex).toBe(0);

    const d = make(null);
    (d.comp as any).initTabFromUrl();
    expect(d.comp.tabIndex).toBe(0);
  });

  it('schreibt den Reiterwechsel in die URL; der erste Reiter räumt ?tab= wieder ab', () => {
    const { comp, router } = make(null);
    comp.onTabChange(1);
    expect(comp.tabIndex).toBe(1);
    expect(router.navigate.calls.mostRecent().args[1].queryParams).toEqual({ tab: 'history' });
    expect(router.navigate.calls.mostRecent().args[1].replaceUrl).toBeTrue();

    comp.onTabChange(0);
    expect(router.navigate.calls.mostRecent().args[1].queryParams).toEqual({ tab: null });
  });
});
