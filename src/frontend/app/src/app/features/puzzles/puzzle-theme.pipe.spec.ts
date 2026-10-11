import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { PuzzleThemePipe, humanizeThemeKey } from './puzzle-theme.pipe';

// UI-Sweep 2026-10-11 (r2-rp-themes-en): Themen-Schlüssel → übersetzter Name, Rückfall lesbar.
describe('PuzzleThemePipe', () => {
  it('humanizeThemeKey: CamelCase und Ziffern in Leerzeichen-Schreibweise', () => {
    expect(humanizeThemeKey('mateIn2')).toBe('Mate in 2');
    expect(humanizeThemeKey('discoveredCheck')).toBe('Discovered check');
    expect(humanizeThemeKey('attackingF2F7')).toBe('Attacking f2 f7');
    expect(humanizeThemeKey('short')).toBe('Short');
    expect(humanizeThemeKey('')).toBe('');
  });

  function make(): { pipe: PuzzleThemePipe; translate: TranslateService } {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { puzzleThemes: { mateIn2: 'Matt in 2', short: 'Kurz' } });
    translate.use('de');
    const pipe = TestBed.runInInjectionContext(() => new PuzzleThemePipe());
    return { pipe, translate };
  }

  it('übersetzt bekannte Themen', () => {
    const { pipe } = make();
    expect(pipe.transform('mateIn2')).toBe('Matt in 2');
    expect(pipe.transform('short')).toBe('Kurz');
  });

  it('fällt ohne Übersetzung auf die lesbare Schreibweise zurück', () => {
    const { pipe } = make();
    expect(pipe.transform('kingsideAttack')).toBe('Kingside attack');
  });

  it('leer bleibt leer', () => {
    const { pipe } = make();
    expect(pipe.transform(null)).toBe('');
  });
});
