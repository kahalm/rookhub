import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
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

/**
 * „Spielzeit aktualisieren" hat serverseitig eine Sperrfrist je Plattform (Codereview 2026-09-29, F5-002): kam der
 * letzte Abruf gerade erst, antwortet der Server `synced: false` + Restzeit — das darf nicht als „aktualisiert" erscheinen.
 */
describe('TrainingGoalsComponent Spielzeit-Abgleich', () => {
  function make(response: { synced: boolean; retryAfterSeconds?: number }) {
    const service: any = { syncPlay: () => of(response) };
    const snackbar = jasmine.createSpyObj('SnackbarService', ['info', 'success', 'warn']);
    const translate: any = { instant: (k: string, p?: Record<string, unknown>) => (p ? `${k}:${JSON.stringify(p)}` : k) };
    const comp = new TrainingGoalsComponent(service, snackbar, translate, {} as any, {} as any);
    const reload = spyOn(comp, 'reload');
    return { comp, snackbar, reload };
  }

  it('meldet bei der Sperrfrist die Restzeit statt „aktualisiert" und lädt nicht neu', () => {
    const { comp, snackbar, reload } = make({ synced: false, retryAfterSeconds: 130 });
    comp.syncPlayTime();
    expect(snackbar.info).toHaveBeenCalledWith('trainingGoals.syncCooldown:{"minutes":3}');
    expect(snackbar.success).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
    expect(comp.syncingPlay).toBeFalse();
  });

  it('abgefragt: wie bisher „aktualisiert" und neu laden', () => {
    const { comp, snackbar, reload } = make({ synced: true });
    comp.syncPlayTime();
    expect(snackbar.success).toHaveBeenCalledWith('trainingGoals.syncDone');
    expect(snackbar.info).not.toHaveBeenCalled();
    expect(reload).toHaveBeenCalled();
  });
});
