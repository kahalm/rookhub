import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { LineWhyComponent } from './line-why.component';
import { LineWhy, lineWhyText, whyMoveLabel } from './line-why.util';
import { BookContextCardComponent } from './book-context-card.component';

/** „Warum diese Aufgabe“ (1.001.0): Satz aus dem sprachneutralen Vermerk, mit den echten de/en-Texten. */
describe('LineWhy (Vermerk „Warum diese Aufgabe“)', () => {
  let de: Record<string, unknown>;
  let en: Record<string, unknown>;
  const load = async (lang: string) => {
    const res = await fetch(`/i18n/${lang}.json`).then(r => (r.ok ? r : fetch(`/base/public/i18n/${lang}.json`)))
      .then(r => (r.ok ? r : fetch(`/base/i18n/${lang}.json`)));
    return res.json();
  };
  beforeAll(async () => { de = await load('de'); en = await load('en'); });

  /** Übersetzer über eine Sprachdatei — `{{x}}` wie ngx-translate. */
  const t = (dict: Record<string, unknown>) => (key: string, params?: Record<string, unknown>) => {
    const v = key.split('.').reduce<unknown>((o, k) => (o as Record<string, unknown> | undefined)?.[k], dict);
    if (typeof v !== 'string') return key;
    return v.replace(/\{\{\s*(\w+)\s*\}\}/g, (_, p) => String(params?.[p] ?? ''));
  };
  const base: LineWhy = { ply: 41, white: false, reason: 'missedTactic', movePly: 41, moveSan: 'Nb6', theme: 'fork', match: 'strict' };

  it('Zugnummer wie im Titel, Figuren in der Sprache', () => {
    expect(whyMoveLabel(41, 'Nb6', 'de')).toBe('21…Sb6');
    expect(whyMoveLabel(40, 'Qh5', 'en')).toBe('21.Qh5');
    expect(whyMoveLabel(62, null, 'de')).toBe('32.');
  });

  it('verpasste Gabel mit gleicher Signatur: Stelle + Muster mit Figuren', () => {
    expect(lineWhyText({ ...base, piece: 'N', targets: 'KQ' }, t(de), 'de')).toBe(
      'Passt zu 21…Sb6 in der Partie: Schwarz hat dort das Motiv „Gabel“ verpasst. Diese Aufgabe hat dasselbe Muster: Springer gabelt König und Dame.');
    expect(lineWhyText({ ...base, piece: 'N', targets: 'KQ' }, t(en), 'en')).toBe(
      'Matches 21…Nb6 in the game: Black missed the motif “Fork” there. This exercise has the same pattern: knight forks king and queen.');
  });

  it('zugelassen, Fesselung im Buchdiagramm; Familie; nur Thema', () => {
    const pin = lineWhyText({ ...base, reason: 'allowedTactic', white: true, movePly: 41, moveSan: 'Qd2', theme: 'pin', book: true, piece: 'B', targets: 'NK' }, t(de), 'de')!;
    expect(pin).toContain('Passt zu 21…Dd2 in der Partie: Dieser Zug ließ für Weiß das Motiv „Fesselung“ zu.');
    expect(pin).toContain('Dieses Buchdiagramm hat dasselbe Muster: Läufer fesselt Springer an König.');
    expect(lineWhyText({ ...base, match: 'family' }, t(de), 'de')).toContain('Diese Aufgabe hat dasselbe Motiv mit anderen Figuren.');
    expect(lineWhyText({ ...base, match: 'theme', matchTheme: 'fork' }, t(de), 'de')).toContain('Diese Aufgabe hat nur das gleiche Thema (Gabel).');
  });

  it('Matt: verpasst/gespielt/Drohung, Mattbild streng/ähnlich, weitere Stellen', () => {
    const mate: LineWhy = { ply: 10, white: true, reason: 'missedMate', movePly: 10, moveSan: 'Kb1', mateIn: 2, match: 'strict', also: [20, 31] };
    expect(lineWhyText(mate, t(de), 'de')).toBe(
      'Passt zu 6.Kb1 in der Partie: Weiß hat dort ein Matt in 2 verpasst. Diese Aufgabe endet mit demselben Mattbild. Dasselbe Bild kam auch bei 11., 16… vor.');
    expect(lineWhyText({ ...mate, reason: 'playedMate', match: 'family', also: null }, t(de), 'de')).toBe(
      'Passt zu 6.Kb1 in der Partie: Ab dort setzte Weiß matt (Matt in 2). Diese Aufgabe endet mit einem ähnlichen Mattbild.');
    expect(lineWhyText({ ...mate, reason: 'mateThreat', match: 'theme', matchTheme: 'mateIn2', also: null }, t(de), 'de')).toContain(
      'Weiß hatte dort eine Mattdrohung (Matt in 2). Diese Aufgabe hat nur das gleiche Thema (Matt in 2).');
    expect(lineWhyText({ ...mate, reason: 'forcedMate', moveSan: null, also: null }, t(de), 'de')).toContain('Passt zu 6. in der Partie: Am Ende stand für Weiß ein Matt in 2');
  });

  it('nichts Verlässliches → kein Satz', () => {
    expect(lineWhyText(null, t(de), 'de')).toBeNull();
    expect(lineWhyText({ ...base, reason: 'neuerGrund' }, t(de), 'de')).toBeNull();
    expect(lineWhyText({ ...base, theme: null }, t(de), 'de')).toBeNull();
  });

  it('Komponente: „Warum diese Aufgabe:“-Zeile, ohne Vermerk nichts — auch in der Kontext-Karte des Lösers', async () => {
    TestBed.configureTestingModule({
      imports: [LineWhyComponent, BookContextCardComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', de as never);
    translate.use('de');

    const f = TestBed.createComponent(LineWhyComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelector('.line-why')).toBeNull();
    f.componentRef.setInput('why', { ...base, piece: 'N', targets: 'KQ' });
    f.detectChanges();
    const p = f.nativeElement.querySelector('.line-why') as HTMLElement;
    expect(p.querySelector('.line-why-label')?.textContent).toBe('Warum diese Aufgabe:');
    expect(p.textContent).toContain('Springer gabelt König und Dame');

    const card = TestBed.createComponent(BookContextCardComponent);
    card.componentRef.setInput('hasPuzzle', true);
    card.componentRef.setInput('state', 'AWAITING_USER_MOVE');
    card.componentRef.setInput('titleDisplay', '21… · Lichess AbCdE');
    card.componentRef.setInput('why', base);
    card.detectChanges();
    const line = card.nativeElement.querySelector('.context-card .ctx-text .line-why') as HTMLElement;
    expect(line.textContent).toContain('Schwarz hat dort das Motiv „Gabel“ verpasst.');
    // leise: Theme-Rolle, kleiner als der Titel
    const size = (el: Element) => parseFloat(getComputedStyle(el).fontSize);
    expect(size(line)).toBeLessThan(size(card.nativeElement.querySelector('.meta-title')));
  });
});
