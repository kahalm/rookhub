import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
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

  // W5 F5-017: scheitert das Laden, stand dort „kein Ziel" und leere Auswertungen, als gaebe es nichts.
  it('Ladefehler → Fehlerhinweis statt der Reiter', async () => {
    await TestBed.configureTestingModule({
      imports: [TrainingGoalsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TrainingGoalsComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    // forkJoin bricht die uebrigen Anfragen ab, sobald die erste scheitert.
    for (const r of http.match(() => true)) if (!r.cancelled) r.flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.loadError).toBeTrue();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('mat-tab-group')).toBeNull();
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

/** UI-Sweep r3: „Wochenziel Spielen" und „noch offen" klebten zusammen („Spielennoch offen"). */
describe('TrainingGoalsComponent Wochenziel Spielen', () => {
  it('trennt „noch offen" sichtbar vom Namen', async () => {
    await TestBed.configureTestingModule({
      imports: [TrainingGoalsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TrainingGoalsComponent);
    const comp = fixture.componentInstance;
    spyOn(comp, 'reload');
    const goal: any = { dailyMinutes: 0, playGames: 3, weeklyDaysTarget: 0 };
    comp.goal = goal;
    comp.today = {
      goal, daily: { targetMinutes: 0, doneSeconds: 0, met: false }, bySource: {}, byTheme: {},
      play: { targetGames: 3, doneGames: 1, met: false }, status: 'none', weekDaysMet: 0, weeklyDaysTarget: 0,
    } as any;
    comp.loading = false;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const name: HTMLElement | null = fixture.nativeElement.querySelector('.cat-weekly .cat-name');
    expect(name).not.toBeNull();
    const tag = name!.querySelector('.weekly-tag');
    expect(tag).not.toBeNull();
    expect(tag!.querySelector('.weekly-sep')?.textContent).toBe('·');
    expect(getComputedStyle(tag!.querySelector('.weekly-sep')!).marginLeft).not.toBe('0px');
  });
});

/** UI-Sweep z1 (z1-rk-goals-reset-umbruch): Aktionszeile bricht zwischen, nicht in den Knöpfen um. */
describe('TrainingGoalsComponent Aktionszeile', () => {
  it('lässt die Zeile umbrechen, aber nicht den Knopftext', () => {
    const css = ((TrainingGoalsComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.actions[^{]*\{[^}]*flex-wrap: wrap/);
    expect(css).toMatch(/\.actions[^{]* button[^{]*\{ ?white-space: nowrap/);
  });
});

/** UI-Sweep d3 (d3-rk-trainingsziele-tag-vs-woche): ein Tagesziel (Trainingszeit) + Wochenziele, nicht „je Kategorie“. */
describe('TrainingGoalsComponent Texte Tag/Woche', () => {
  it('Feld nennt Minuten pro Tag', async () => {
    await TestBed.configureTestingModule({
      imports: [TrainingGoalsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TrainingGoalsComponent);
    const comp = fixture.componentInstance;
    spyOn(comp, 'reload');
    const goal: any = { dailyMinutes: 20, playGames: 3, weeklyDaysTarget: 4 };
    comp.goal = goal;
    comp.today = {
      goal, daily: { targetMinutes: 20, doneSeconds: 0, met: false }, bySource: {}, byTheme: {},
      play: { targetGames: 3, doneGames: 1, met: false }, status: 'none', weekDaysMet: 0, weeklyDaysTarget: 4,
    } as any;
    comp.loading = false;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const labels = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.goal-fields mat-label'))
      .map(l => l.textContent ?? '');
    expect(labels.length).toBe(3);
    expect(labels[0]).toContain('trainingGoals.minPerDay');
    expect(labels[1]).toContain('trainingGoals.gamesPerWeek');
  });

  it('Untertitel und Leerhinweis versprechen keine Tagesziele je Kategorie (de/en/hr/hu)', async () => {
    for (const lang of ['de', 'en', 'hr', 'hu']) {
      let res = await fetch(`/i18n/${lang}.json`).catch(() => null);
      if (!res?.ok) res = await fetch(`/base/i18n/${lang}.json`);
      expect(res!.ok).withContext(`${lang}.json`).toBeTrue();
      const t = (await res!.json()).trainingGoals;
      expect(t.minPerDay).withContext(lang).toBeTruthy();
      expect(t.intro).withContext(lang).not.toMatch(/je Kategorie|per category|po kategoriji|kategóriánként/i);
      expect(t.noGoalHint).withContext(lang).not.toMatch(/Tagesziele|daily targets|dnevne ciljeve|napi céljaidat/i);
    }
  });
});
