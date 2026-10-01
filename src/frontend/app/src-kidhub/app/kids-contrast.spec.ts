import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { of, Subject } from 'rxjs';
import { KidHubAppComponent } from './app.component';
import { KidsApiService, KidsLevelDetail } from './core/kids-api.service';
import { KidsHomeComponent } from './features/home/kids-home.component';
import { LevelMapComponent } from './features/levels/level-map.component';
import { LevelPlayComponent } from './features/levels/level-play.component';
import { EndlessPlayComponent } from './features/endless/endless-play.component';
import { KidsPuzzleComponent } from './shared/kids-puzzle.component';
import { readCoords } from '@rh/testing/board-coords';

type Rgba = [number, number, number, number];

function parseColor(css: string): Rgba {
  const m = /^rgba?\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)(?:,\s*([\d.]+))?\)$/.exec(css);
  if (!m) throw new Error(`keine Farbe: ${css}`);
  return [Number(m[1]), Number(m[2]), Number(m[3]), m[4] === undefined ? 1 : Number(m[4])];
}

/** `top` (mit Deckkraft) ueber dem deckenden `base`. */
function over(top: Rgba, base: Rgba): Rgba {
  const a = top[3];
  return [0, 1, 2].map(i => top[i] * a + base[i] * (1 - a)).concat(1) as Rgba;
}

function luminance([r, g, b]: Rgba): number {
  const lin = (c: number) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

function contrast(a: Rgba, b: Rgba): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Hintergrund von `selector` aus der Regel „nur mit Maus" derselben Komponente wie `host`. */
function mouseOnlyBackground(host: HTMLElement, selector: string): string {
  const scope = host.getAttributeNames().find(a => a.startsWith('_ngcontent-'));
  for (const sheet of Array.from(document.styleSheets)) {
    let rules: CSSRuleList;
    try { rules = sheet.cssRules; } catch { continue; }
    for (const rule of Array.from(rules)) {
      if (!(rule instanceof CSSMediaRule) || !rule.conditionText.includes('pointer: fine')) continue;
      for (const inner of Array.from(rule.cssRules)) {
        if (inner instanceof CSSStyleRule && inner.selectorText.includes(`${selector}[${scope}]`)) {
          return inner.style.backgroundColor;
        }
      }
    }
  }
  throw new Error(`Regel fuer ${selector} nicht gefunden`);
}

/**
 * Kontrast der gruenen Knoepfe mit Schrift auf der Kinderseite (Codereview 2026-09-29, UX-029). Weisse Schrift
 * auf --kid-green (#2fb35f) hatte 2,72:1 — zu wenig selbst fuer grosse Schrift (3:1). Betroffen waren auch die
 * Knoepfe, die erst nach dem Loesen erscheinen („Weiter ▶" nach jeder Aufgabe) und die deshalb kein Crawl sah.
 * Gerendert wird die echte Seite (Huelle + Seite) in jedem Zustand, in dem so ein Knopf steht; gemessen werden
 * die berechneten Farben, nicht die Quelltexte.
 */
describe('KidHub: Kontrast der Knoepfe mit Schrift', () => {
  const detail: KidsLevelDetail = {
    level: 1, theme: 'mate1',
    puzzles: [{ id: 1, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8' }],
  };
  let f: ComponentFixture<KidHubAppComponent>;

  beforeEach(async () => {
    localStorage.setItem('rookhub_lang', 'de');
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService',
      ['level', 'levels', 'languageHint', 'courses', 'endlessBatch']);
    api.level.and.returnValue(of(detail));
    api.levels.and.returnValue(of([{ level: 1, theme: 'mate1', puzzleCount: 1 }, { level: 2, theme: 'promote', puzzleCount: 1 }]));
    api.languageHint.and.returnValue(of({ country: null, language: null }));
    api.courses.and.returnValue(of([]));
    api.endlessBatch.and.returnValue(of([{ id: 7, fen: detail.puzzles[0].fen, moves: detail.puzzles[0].moves, rating: 700 }]));
    TestBed.configureTestingModule({
      imports: [KidHubAppComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideRouter([
          { path: '', pathMatch: 'full', component: KidsHomeComponent },
          { path: 'levels', component: LevelMapComponent },
          { path: 'levels/:level', component: LevelPlayComponent },
          { path: 'endless', component: EndlessPlayComponent },
        ]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        {
          provide: SwUpdate,
          useValue: {
            isEnabled: false, versionUpdates: new Subject<unknown>(),
            unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false),
          },
        },
      ],
    });
    f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
  });

  afterEach(() => { localStorage.removeItem('rookhub_lang'); localStorage.removeItem('rh-kids-progress-v1'); });

  async function go(url: string): Promise<void> {
    await TestBed.inject(Router).navigateByUrl(url);
    f.detectChanges();
    f.detectChanges();
  }

  function el(selector: string): HTMLElement {
    const found = (f.nativeElement as HTMLElement).querySelector<HTMLElement>(selector);
    expect(found).withContext(`${selector} fehlt`).not.toBeNull();
    return found!;
  }

  /** Schrift gegen Grund des Knopfs: 4,5:1 (WCAG AA fuer normalen Text — „Registrieren" ist nicht gross). */
  function expectReadable(button: HTMLElement, label: string): void {
    const style = getComputedStyle(button);
    const bg = parseColor(style.backgroundColor);
    expect(bg[3]).withContext(`${label}: Grund deckend`).toBe(1);
    const ratio = contrast(over(parseColor(style.color), bg), bg);
    expect(ratio).withContext(`${label}: ${style.color} auf ${style.backgroundColor}`).toBeGreaterThanOrEqual(4.5);
  }

  it('Startseite: „Registrieren" und „Los geht\'s"', async () => {
    await go('/');
    expectReadable(el('.acct.primary'), 'Registrieren');
    expectReadable(el('.go'), 'Los geht\'s');
  });

  it('nach dem Loesen: „Weiter ▶" samt Leertasten-Hinweis', async () => {
    await go('/levels/1');
    f.debugElement.query(By.directive(KidsPuzzleComponent)).componentInstance.status.set('solved');
    f.detectChanges();
    const next = el('.next');
    expectReadable(next, 'Weiter');
    // Der Hinweis „Leertaste" liegt halbdurchsichtig auf dem Knopf — nur mit Maus (hover/pointer: fine), und das
    // meldet der Testbrowser nicht: deshalb der Grund aus der Regel selbst.
    const bg = parseColor(getComputedStyle(next).backgroundColor);
    const chip = over(parseColor(mouseOnlyBackground(next, '.key')), bg);
    const text = parseColor(getComputedStyle(el('.next .key')).color);
    expect(contrast(over(text, chip), chip)).withContext('Leertaste').toBeGreaterThanOrEqual(4.5);
  });

  it('Stufe geschafft: „Naechste Stufe ▶"', async () => {
    await go('/levels/1');
    f.debugElement.query(By.directive(LevelPlayComponent)).componentInstance.finished.set(3);
    f.detectChanges();
    expectReadable(el('.btn.primary'), 'Naechste Stufe');
  });

  it('Endlos vorbei: „↻ Nochmal"', async () => {
    await go('/endless');
    f.debugElement.query(By.directive(EndlessPlayComponent)).componentInstance.over.set(true);
    f.detectChanges();
    expectReadable(el('.btn.primary'), 'Nochmal');
  });

  // UX-060: Die Kinderseite spielt fest im Thema „blue". Die Rangziffern hatten die Farbe fuer das jeweils andere
  // Feld (2/4/6/8 hell auf hell, unsichtbar) und waren 9 px klein bei 0,8 Deckkraft.
  it('Brett-Koordinaten: 11 px, deckend, jede in der Farbe fuer IHR Feld (UX-060)', async () => {
    await go('/levels/1');
    const wrap = el('app-puzzle-board .cg-wrap');
    for (const coords of Array.from(wrap.querySelectorAll<HTMLElement>('coords'))) {
      const style = getComputedStyle(coords);
      expect(parseFloat(style.fontSize)).withContext(`${coords.className}: Schriftgroesse`).toBeGreaterThanOrEqual(11);
      expect(Number(style.opacity)).withContext(`${coords.className}: Deckkraft`).toBe(1);
    }
    const readings = readCoords(wrap, '#d4e3ed', '#5882a1');
    expect(readings.length).toBe(16);
    for (const r of readings) {
      expect(r.own).withContext(`${r.label} auf ${r.square}: ${r.own.toFixed(2)}:1, andere Feldfarbe ${r.other.toFixed(2)}:1`)
        .toBeGreaterThan(r.other);
    }
  });

  it('Stufenkarte: der Rahmen der aktuellen Stufe hebt sich ab (3:1, WCAG 1.4.11)', async () => {
    await go('/levels');
    const outline = parseColor(getComputedStyle(el('.level.current')).outlineColor);
    const page = parseColor(getComputedStyle(f.nativeElement as HTMLElement).backgroundColor);
    const card = parseColor(getComputedStyle(el('.level.current')).backgroundColor);
    expect(contrast(outline, page)).withContext('gegen den Seitengrund').toBeGreaterThanOrEqual(3);
    expect(contrast(outline, card)).withContext('gegen die Kachel').toBeGreaterThanOrEqual(3);
  });
});
