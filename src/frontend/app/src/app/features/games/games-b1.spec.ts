import { LOCALE_ID, Type } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { OverlayContainer } from '@angular/cdk/overlay';
import { GamesListComponent } from './games-list.component';
import { GameReviewComponent } from './game-review.component';
import { GameEditComponent } from './game-edit.component';
import { SharedGameComponent } from './shared-game.component';

/**
 * UI-Sweep b1, Gruppe Partien: Regeln als CSS-Text (Karma läuft 1 400 px breit, Media-Queries greifen dort nicht).
 */
function css(cmp: Type<unknown>): string {
  return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
    .replace(/\s+/g, ' ').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/--%NS%/g, '--');
}

function block(all: string, query: string): string {
  const start = all.indexOf(query);
  if (start < 0) return '';
  let depth = 0;
  for (let i = all.indexOf('{', start); i < all.length; i++) {
    if (all[i] === '{') depth++;
    if (all[i] === '}' && --depth === 0) return all.slice(start, i + 1);
  }
  return '';
}

describe('UI-Sweep b1 — Partie-Seite (SharedGameComponent)', () => {
  const all = css(SharedGameComponent);

  it('b1-rk-partie-quelle-pgn: Partiekopf zeigt die Quelle übersetzt statt des Rohschlüssels „pgn"', async () => {
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: false, has: () => false } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'tok' }), data: {} } } }],
    }).compileComponents();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('en', { games: { source: { pgn: 'PGN upload', scoresheet: 'Scoresheet' } } });
    t.use('en');
    const fixture = TestBed.createComponent(SharedGameComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush({
      source: 'pgn', white: 'a', black: 'b', result: '0-1', ownerSide: null,
      pgn: '[White "a"]\n[Black "b"]\n\n1. e4 c5 0-1', createdAt: '2026-07-16T00:00:00Z',
    });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.meta .source')!.textContent!.trim()).toBe('PGN upload');
  });

  it('b1-rk-vereinspartie-sperrkarte-ultrawide: Sperr-/Fehlerkarte bleibt bei der Seitenbreite', () => {
    expect(all).toMatch(/\.shared-page > \.empty-state, ?\.shared-page > \.load-card \{[^}]*max-width: min\(var\(--page-max-width, 1240px\), 96vw\); margin-inline: auto/);
  });

  it('b1-rk-partie-grund-unter-knopf: Hülle so breit wie der Knopf, Grund bricht ausgewogen ohne eigene Breite', () => {
    expect(all).toMatch(/\.analyze-wrap \{[^}]*min-width: 220px; max-width: 280px/);
    expect(all).toMatch(/\.analyze-no-engine \{[^}]*width: 0; min-width: 100%; text-wrap: balance/);
  });

  it('b1-rk-partie-zugliste-schatten: Zugliste mit Weiterscroll-Schatten in der Kartenfarbe (local + scroll)', () => {
    const moves = all.match(/\.moves-section \{[^}]*\}/)![0];
    expect(moves).toContain('var(--mat-sys-surface-container-low) 70%) center bottom / 100% 32px no-repeat local');
    expect(moves).toMatch(/radial-gradient\(farthest-side at 50% 100%, color-mix\(in srgb, currentColor 30%, transparent\), transparent\) center bottom \/ 100% 14px no-repeat scroll/);
  });

  it('b1-rk-partie-zugliste-schatten (Nachprüfung): .moves-section scrollt selbst — die Zugliste darin als Block ohne feste Höhe', () => {
    expect(all).toMatch(/\.moves-section > app-move-list(\[[^\]]*\])? \{ display: block; \}/);
    expect(all).toMatch(/\.moves-section \{[^}]*overflow: auto/);
  });

  it('b1-rk-partie-grund-unter-knopf (Nachprüfung, seit d5 abgelöst): am Tablet keine Hülle mehr, ⋮ ohne Sonderabstand', () => {
    const tablet = block(all, '@media (min-width: 769px) and (max-width: 1279.98px)');
    expect(tablet).toContain('.analyze-wrap { display: contents; }');
    expect(tablet).not.toContain('.analyze-wrap + .game-menu');
  });
});

describe('UI-Sweep b1 — Gespeicherte Partien (GamesListComponent)', () => {
  it('b1-rk-breitstufe-zeilen-auseinander: ab 2400 px wachsen die Zahlenspalten mit, darunter bleibt das feste Raster', () => {
    const all = css(GamesListComponent);
    const wide = block(all, '@media (min-width: 2400px)');
    expect(wide).toContain('grid-template-columns: 78px minmax(0, 1.4fr) minmax(72px, 0.5fr) minmax(84px, 0.5fr) minmax(48px, 0.5fr) minmax(104px, 0.5fr) 88px');
    expect(all.replace(wide, '')).toContain('grid-template-columns: 78px minmax(0, 1fr) 72px 84px 48px 104px 88px');
  });

  it('b1-rk-games-menue-loeschen-rot: „Löschen" im ⋮ trägt .delete-item (rotes Symbol), kein color="warn"', async () => {
    await TestBed.configureTestingModule({
      imports: [GamesListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(GamesListComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(req => req.method === 'GET' && req.url.startsWith('/api/games') && !req.url.startsWith('/api/games/'))
      .flush([{ id: 4, source: 'pgn', white: 'a', black: 'b', result: '1-0', moveCount: 3, shareToken: 't', createdAt: '2026-07-16T00:00:00Z' }]);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.actions button[mat-icon-button]') as HTMLElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    const items = Array.from(overlay.querySelectorAll('.mat-mdc-menu-item')) as HTMLElement[];
    const last = items[items.length - 1];
    expect(last.classList).toContain('delete-item');
    expect(last.querySelector('mat-icon')!.getAttribute('color')).toBeNull();
  });
});

describe('UI-Sweep b1 — Auswertung (GameReviewComponent)', () => {
  const all = css(GameReviewComponent);

  it('b1-rk-auswertung-hell-kontrast: aktive Schalter in --rh-success, Nullen mit 55 %', () => {
    expect(all).toMatch(/\.toggle\.on \{ ?opacity: 1; color: var\(--rh-success\)/);
    expect(all).not.toContain('#81b64c');
    expect(all).toMatch(/\.summary \.count\.zero \{ ?color: color-mix\(in srgb, currentColor 55%, transparent\)/);
  });
});

describe('UI-Sweep b1 — Partie korrigieren (GameEditComponent)', () => {
  it('b1-rk-edit-ergebnis-regression: Ergebnis-Spalte am PC mindestens 128 px', () => {
    const pc = block(css(GameEditComponent), '@media (min-width: 1101px)');
    expect(pc).toContain('minmax(128px, 0.9fr)');
    expect(pc).toContain('minmax(0, 1.4fr)');
    expect(pc).not.toContain('minmax(112px');
  });
});

describe('UI-Sweep b1 — Datum in der App-Sprache (GameEditComponent)', () => {
  beforeAll(() => registerLocaleData(localeDe));

  it('b1-rk-edit-datum-us-format: Material-Datumsfeld zeigt „05.06.2026" in Deutsch, kein natives type=date mehr', async () => {
    await TestBed.configureTestingModule({
      imports: [GameEditComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }), { provide: LOCALE_ID, useValue: 'de' },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }), data: {} } } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(GameEditComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/games/5').flush({
      id: 5, source: 'lichess', white: 'A', black: 'B', result: '*', moveCount: 1, shareToken: 't', createdAt: '2026-09-25T00:00:00Z',
      pgn: '[Date "2026.06.05"]\n[White "A"]\n[Black "B"]\n[Result "*"]\n\n1. e4 *',
    });
    fixture.detectChanges();
    // ngModel schreibt den Wert erst nach einem Mikrotakt ins Feld (whenStable wartet hier auf Hintergrund-Abrufe).
    await new Promise(r => setTimeout(r));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('input[type="date"]')).toBeNull();
    const input = el.querySelector('app-iso-date-field input') as HTMLInputElement;
    expect(input.value).toBe('05.06.2026');
    // Getippt in der deutschen Reihenfolge → zurück als "YYYY-MM-DD", Partie gilt als geändert.
    input.value = '16.07.2026';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.componentInstance.header.date).toBe('2026-07-16');
    expect(fixture.componentInstance.dirty()).toBeTrue();
  });
});
