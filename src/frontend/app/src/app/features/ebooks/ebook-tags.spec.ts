import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { EBOOK_TAG_KEYS, EbookTagLabelPipe, ebookTagKey, ebookTagLabel } from './ebook-tags';

/** Mehrsprachige Schlagwörter (0.877.0): gespeichert bleibt das deutsche Wort, angezeigt wird die Oberflächensprache. */
describe('ebookTagLabel', () => {
  let translate: TranslateService;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ebooks: { tags: { opening: 'Opening', heismanRecommendation: 'Heisman recommendation' } } });
    translate.setTranslation('de', { ebooks: { tags: { opening: 'Eröffnung', heismanRecommendation: 'Heisman-Empfehlung' } } });
    translate.setTranslation('fr', { common: { save: 'Enregistrer' } });
  });

  it('übersetzt ein bekanntes Schlagwort in de und en (Groß-/Kleinschreibung egal)', async () => {
    await firstValueFrom(translate.use('en'));
    expect(ebookTagLabel('Eröffnung', translate)).toBe('Opening');
    expect(ebookTagLabel('eröffnung', translate)).toBe('Opening');
    await firstValueFrom(translate.use('de'));
    expect(ebookTagLabel('Eröffnung', translate)).toBe('Eröffnung');
  });

  it('fällt in einer nicht gepflegten Sprache (fr) aufs Englische zurück', async () => {
    await firstValueFrom(translate.use('fr'));
    expect(ebookTagLabel('Eröffnung', translate)).toBe('Opening');
    expect(ebookTagLabel('Heisman-Empfehlung', translate)).toBe('Heisman recommendation');
  });

  it('zeigt freie Schlagwörter und Eigennamen von Listen/Preisen wörtlich', async () => {
    await firstValueFrom(translate.use('en'));
    for (const raw of ['Top 100', 'ChessDojo', 'ECF Book of the Year', 'r/chess Wiki', 'Mein eigenes Schlagwort']) {
      expect(ebookTagKey(raw)).withContext(raw).toBeNull();
      expect(ebookTagLabel(raw, translate)).toBe(raw);
    }
  });

  it('ohne geladenen Text (Kaltstart) das gespeicherte Wort statt des Schlüssels', async () => {
    await firstValueFrom(translate.use('en'));
    expect(ebookTagLabel('Taktik', translate)).toBe('Taktik');   // en kennt im Test nur opening/heisman
  });

  it('Pipe nutzt dieselbe Regel; die Karte deckt die häufigen Gattungen ab', async () => {
    await firstValueFrom(translate.use('en'));
    const pipe = TestBed.runInInjectionContext(() => new EbookTagLabelPipe());
    expect(pipe.transform('Eröffnung')).toBe('Opening');
    expect(pipe.transform(null)).toBe('');
    for (const tag of ['Eröffnung', 'Kommentierte Partien', 'Lehrbuch', 'Strategie', 'Taktik', 'Endspiel', 'Mittelspiel',
      'Spiele', 'Belletristik', 'Problemschach', 'Engine-Partiensammlung', 'QC: Beste des 20. Jh.', 'FIDE-Buchpreis Shortlist']) {
      expect(EBOOK_TAG_KEYS[tag]).withContext(tag).toMatch(/^ebooks\.tags\.[a-zA-Z0-9]+$/);
    }
  });

  it('jeder Schlüssel der Karte steht in en/de/hr/hu; de = das gespeicherte Schlagwort', async () => {
    const load = async (lang: string): Promise<Record<string, unknown>> => {
      for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
        const res = await fetch(url);
        if (res.ok) return res.json();
      }
      throw new Error(`${lang}.json nicht ladbar`);
    };
    const pick = (obj: Record<string, unknown>, key: string): unknown =>
      key.split('.').reduce<unknown>((o, k) => (o as Record<string, unknown> | undefined)?.[k], obj);
    for (const lang of ['en', 'de', 'hr', 'hu']) {
      const file = await load(lang);
      for (const [tag, key] of Object.entries(EBOOK_TAG_KEYS)) {
        const text = pick(file, key);
        expect(typeof text === 'string' && text.length > 0).withContext(`${lang}: ${key}`).toBeTrue();
        if (lang === 'de') expect(text).withContext(key).toBe(tag);
      }
    }
  });
});
