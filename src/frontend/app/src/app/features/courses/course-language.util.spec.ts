import {
  effectiveLanguage, labelOr, languagesFromLines, languagesFromOverview, mergeLanguages, normLang,
  offlineLanguageStale, translationKey,
} from './course-language.util';

describe('course-language.util', () => {
  describe('Vorgabe der Sprachwahl (effectiveLanguage)', () => {
    const langs = ['en', 'de', 'fr'];   // Quelle zuerst

    it('nimmt die gewünschte Sprache, wenn der Kurs sie hat', () => {
      expect(effectiveLanguage('de', langs)).toBe('de');
      expect(effectiveLanguage('FR ', langs)).toBe('fr');
    });

    it('fällt aufs Original (die Quelle) zurück, wenn der Kurs sie nicht hat', () => {
      expect(effectiveLanguage('hr', langs)).toBe('en');
      expect(effectiveLanguage(null, langs)).toBe('en');
      expect(effectiveLanguage('', langs)).toBe('en');
    });

    it('die Quelle selbst ist das Original', () => {
      expect(effectiveLanguage('en', langs)).toBe('en');
      expect(translationKey('en', langs)).toBe('');
    });

    it('ohne bekannte Sprachen gibt es keine wirksame Sprache', () => {
      expect(effectiveLanguage('de', [])).toBeNull();
      expect(translationKey('de', [])).toBe('');
    });

    it('translationKey: Übersetzung = ihr Kürzel, sonst leer', () => {
      expect(translationKey('de', langs)).toBe('de');
      expect(translationKey('hr', langs)).toBe('');
    });
  });

  describe('Sprachlisten', () => {
    it('mergeLanguages: Quelle zuerst, Rest alphabetisch, ohne Doppelte', () => {
      expect(mergeLanguages([], ['en', 'fr', 'de'])).toEqual(['en', 'de', 'fr']);
      expect(mergeLanguages(['en', 'de'], ['en', 'fr'])).toEqual(['en', 'de', 'fr']);
      expect(mergeLanguages(['en', 'de'], null)).toEqual(['en', 'de']);
      expect(mergeLanguages(['en', 'de'], [])).toEqual(['en', 'de']);
    });

    it('mergeLanguages: eine neue Quelle gewinnt, die alte fällt heraus', () => {
      expect(mergeLanguages(['en', 'de'], ['nl', 'de'])).toEqual(['nl', 'de']);
    });

    it('languagesFromLines vereinigt die Linien', () => {
      expect(languagesFromLines([
        { commentLanguages: ['en', 'de'] },
        { commentLanguages: null },
        { commentLanguages: ['en', 'hr'] },
        {},
      ])).toEqual(['en', 'de', 'hr']);
      expect(languagesFromLines(undefined)).toEqual([]);
    });

    it('languagesFromOverview: Quelle + Sprachen mit mindestens einer übersetzten Linie', () => {
      expect(languagesFromOverview({
        sourceLanguage: 'en',
        languages: [
          { language: 'fr', linesTranslated: 3 },
          { language: 'de', linesTranslated: 1881 },
          { language: 'it', linesTranslated: 0 },
          { language: 'en', linesTranslated: 5 },   // Satz in der Quellsprache (nach Korrektur) — kein Extra
        ],
      })).toEqual(['en', 'de', 'fr']);
      expect(languagesFromOverview({ sourceLanguage: null, languages: [{ language: 'de', linesTranslated: 4 }] }))
        .toEqual([]);
      expect(languagesFromOverview(null)).toEqual([]);
    });

    it('normLang', () => {
      expect(normLang(' DE ')).toBe('de');
      expect(normLang('')).toBeNull();
      expect(normLang(undefined)).toBeNull();
    });
  });

  describe('Anzeige: label ?? original', () => {
    it('nimmt das Label, sonst das Original', () => {
      expect(labelOr('Königsindisch', 'King\'s Indian')).toBe('Königsindisch');
      expect(labelOr(null, 'King\'s Indian')).toBe('King\'s Indian');
      expect(labelOr(undefined, null)).toBeNull();
      expect(labelOr('  ', 'Original')).toBe('Original');   // leeres Label zählt nicht
    });
  });

  describe('Offline-Kopie: passt ihre Sprache noch?', () => {
    it('keine Kopie → kein Hinweis', () => {
      expect(offlineLanguageStale(null, 'de', ['en', 'de'])).toBeFalse();
    });

    it('Kopie in der gewählten Übersetzung → passt', () => {
      expect(offlineLanguageStale({ lang: 'de', langs: ['en', 'de'] }, 'de', ['en', 'de'])).toBeFalse();
    });

    it('Wahl gewechselt → Hinweis', () => {
      expect(offlineLanguageStale({ lang: 'de', langs: ['en', 'de'] }, 'en', ['en', 'de'])).toBeTrue();
      expect(offlineLanguageStale({ lang: 'de', langs: ['en', 'de', 'fr'] }, 'fr', ['en', 'de', 'fr'])).toBeTrue();
    });

    it('mit einer Sprache geholt, die der Kurs nicht hat = Original — passt zu einer anderen solchen Wahl', () => {
      expect(offlineLanguageStale({ lang: 'fr', langs: ['en', 'de'] }, 'hr', ['en', 'de'])).toBeFalse();
    });

    it('alte Kopie ohne Angabe gilt als Original', () => {
      expect(offlineLanguageStale({ lang: null, langs: [] }, 'de', ['en', 'de'])).toBeTrue();
      expect(offlineLanguageStale({ lang: null, langs: [] }, 'hr', ['en', 'de'])).toBeFalse();
    });

    it('kam die gewählte Übersetzung erst NACH dem Herunterladen dazu, veraltet die Kopie', () => {
      expect(offlineLanguageStale({ lang: 'de', langs: ['en'] }, 'de', ['en', 'de'])).toBeTrue();
    });
  });
});
