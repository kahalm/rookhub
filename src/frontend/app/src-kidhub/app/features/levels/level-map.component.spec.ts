import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Event, NavigationEnd, provideRouter, Router, Scroll } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject, throwError } from 'rxjs';
import { KidsLevel } from '../../core/kids-api.service';
import { LevelMapComponent, NUDGE_MS, SCROLL_FROM_LEVEL } from './level-map.component';
import { KidsApiService } from '../../core/kids-api.service';
import { contrast, parseColor } from '@rh/testing/contrast';

describe('LevelMapComponent', () => {
  const KEY = 'rh-kids-progress-v1';
  let api: jasmine.SpyObj<KidsApiService>;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['levels']);
    TestBed.configureTestingModule({
      imports: [LevelMapComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  /** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ein Kind, das nicht liest, kam nicht weiter. */
  it('Ladefehler: Fehlerkachel mit „Nochmal", das die Stufen neu holt', () => {
    api.levels.and.returnValues(
      throwError(() => new Error('500')),
      of([{ level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'promote', puzzleCount: 10 }]),
    );
    const f = TestBed.createComponent(LevelMapComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();
    expect(el.querySelector('.grid')).toBeNull();

    again!.click();
    f.detectChanges();

    expect(api.levels).toHaveBeenCalledTimes(2);
    expect(el.querySelector('kid-error')).toBeNull();
    expect(el.querySelectorAll('.grid li').length).toBe(2);
  });

  /** Codereview 2026-09-29, F7-012: gesperrte Stufen waren ein span ohne Handler — ein Tipp bewirkte nichts, der
   *  Hinweis „Schaff zuerst die Stufe davor!" erschien nur beim Direktaufruf der Adresse. */
  it('Tipp auf eine gesperrte Stufe: sie wackelt und der Hinweis erscheint, kurz darauf ist er wieder weg', fakeAsync(() => {
    api.levels.and.returnValue(of([{ level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'promote', puzzleCount: 10 }]));
    const f = TestBed.createComponent(LevelMapComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const locked = el.querySelector<HTMLButtonElement>('button.level.locked');
    expect(locked).withContext('gesperrte Stufe als Knopf').not.toBeNull();
    expect(locked!.getAttribute('aria-disabled')).toBe('true');
    expect(el.querySelector('[role="status"]')?.textContent?.trim()).toBe('');

    locked!.click();
    f.detectChanges();
    expect(locked!.classList).toContain('nudge');
    expect(el.querySelector('[role="status"] .toast')?.textContent).toContain('kids.levels.lockedHint');

    tick(NUDGE_MS);
    f.detectChanges();
    expect(locked!.classList).not.toContain('nudge');
    expect(el.querySelector('.toast')).toBeNull();
  }));

  /** UI-Sweep 2026-10-10 (k-locked-contrast): Nummer, Schloss und Name gesperrter Stufen waren hellgrau auf fast Weiss. */
  it('gesperrte Stufe: Text mit 4,5:1, Schloss als Abzeichen, Thema bleibt sichtbar, Kachel nicht durchsichtig', () => {
    api.levels.and.returnValue(of([{ level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'promote', puzzleCount: 10 }]));
    const f = TestBed.createComponent(LevelMapComponent);
    f.detectChanges();
    const locked = (f.nativeElement as HTMLElement).querySelector<HTMLElement>('button.level.locked')!;
    const style = getComputedStyle(locked);
    expect(Number(style.opacity)).toBe(1);
    const bg = parseColor(style.backgroundColor);
    for (const part of ['.num', '.name']) {
      const color = parseColor(getComputedStyle(locked.querySelector(part)!).color);
      expect(contrast(color, bg)).withContext(part).toBeGreaterThanOrEqual(4.5);
    }
    const badge = locked.querySelector<HTMLElement>('.lock')!;
    expect(badge.textContent).toContain('🔒');
    expect(getComputedStyle(badge).position).toBe('absolute');
    expect(locked.querySelector('.icon')!.textContent).not.toContain('🔒');
  });
});

/**
 * Codereview 2026-09-29, UX-063: am Handy ist die Karte 3 251 px lang (40 Stufen, 2 Spalten) und blieb beim Oeffnen oben —
 * bei Fortschritt bis Stufe 24 lag die aktuelle Stufe 25 bei y = 1 951 px, sichtbar waren nur die fertigen Stufen 1–10.
 */
describe('LevelMapComponent — Sprung zur aktuellen Stufe', () => {
  const KEY = 'rh-kids-progress-v1';
  const LADDER: KidsLevel[] = Array.from({ length: 40 }, (_, i) => ({ level: i + 1, theme: 'mate1', puzzleCount: 10 }));
  let calls: string[];

  /** Stufen 1 … `done` geschafft. */
  function progressUpTo(done: number): void {
    const levels = Object.fromEntries(Array.from({ length: done }, (_, i) => [i + 1, { stars: 3, runIndex: 0, runMistakes: 0 }]));
    localStorage.setItem(KEY, JSON.stringify({ levels, courses: {} }));
  }

  function setup(): void {
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['levels']);
    api.levels.and.returnValue(of(LADDER));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'levels', component: LevelMapComponent }]),
        provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api },
      ],
    });
    calls = [];
    spyOn(Element.prototype, 'scrollIntoView').and.callFake(function (this: Element) {
      calls.push('current:' + this.querySelector('.num')?.textContent);
    });
  }
  afterEach(() => localStorage.removeItem(KEY));

  it('Stufe 25 offen: die Karte springt beim Oeffnen zu ihr', () => {
    progressUpTo(24);
    setup();
    const f = TestBed.createComponent(LevelMapComponent);
    f.detectChanges();
    expect(calls).toEqual(['current:25']);
    expect((Element.prototype.scrollIntoView as jasmine.Spy).calls.mostRecent().args[0]).toEqual({ block: 'center' });

    f.detectChanges();
    expect(calls).withContext('nur einmal je Oeffnen').toEqual(['current:25']);
  });

  it(`aktuelle Stufe unter ${SCROLL_FROM_LEVEL}: kein Sprung, sie steht ohnehin oben`, () => {
    progressUpTo(SCROLL_FROM_LEVEL - 2);
    setup();
    TestBed.createComponent(LevelMapComponent).detectChanges();
    expect(calls).toEqual([]);
  });

  /** Der Router stellt nach jeder Navigation an den Seitenanfang (`scrollPositionRestoration: 'top'`) und meldet das
   *  mit `Scroll` — ein Sprung davor waere gleich wieder weg. (Den Scroller selbst startet erst das echte Bootstrap.) */
  it('per Navigation geoeffnet: springt erst nach dem Scroll-Ereignis des Routers', async () => {
    progressUpTo(24);
    setup();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/levels', LevelMapComponent);
    harness.detectChanges();
    expect(calls).withContext('vor dem Scroll-Ereignis').toEqual([]);

    (TestBed.inject(Router).events as Subject<Event>).next(new Scroll(new NavigationEnd(1, '/levels', '/levels'), null, null));
    harness.detectChanges();
    expect(calls).toEqual(['current:25']);
  });
});
