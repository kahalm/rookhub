import { courseTagList, DESCRIPTION_MAX_CHARS, shortDescription } from './course-detail-text.util';

/** UI-Sweep v1: Kursbeschreibung zugeklappt (v1-rk-kursseite-beschreibung-vorlauf-handy), Schlagwörter als Liste
 *  (v1-rk-kursseite-schlagwoerter-ohne-leerzeichen). */
describe('shortDescription', () => {
  it('kurze Beschreibung bleibt ganz (kein „Mehr")', () => {
    expect(shortDescription(null)).toBeNull();
    expect(shortDescription('Zehn kommentierte Meisterpartien.')).toBeNull();
  });

  it('lange Beschreibung wird am Wortende gekürzt und endet mit „…"', () => {
    const long = 'Zehn vollständig kommentierte Meisterpartien aus Capablancas „Chess Fundamentals" (1921, gemeinfrei). '
      + 'Die Kommentare stammen aus dem Buch und wurden behutsam ins Deutsche übertragen; jede Partie zeigt ein Motiv.';
    const s = shortDescription(long)!;
    expect(s.length).toBeLessThanOrEqual(DESCRIPTION_MAX_CHARS + 2);
    expect(s.endsWith(' …')).toBeTrue();
    expect(long.startsWith(s.slice(0, -2))).toBeTrue();
    expect(long.charAt(s.length - 2)).withContext('am Wortende geschnitten').toMatch(/[\s,;:.–-]/);
  });

  it('mehr als drei Zeilen werden auf drei gekürzt', () => {
    expect(shortDescription('a\nb\nc\nd')).toBe('a\nb\nc …');
  });
});

describe('courseTagList', () => {
  it('trennt den rohen Komma-String in einzelne Schlagwörter', () => {
    expect(courseTagList('Meisterpartien,kommentiert, gemeinfrei')).toEqual(['Meisterpartien', 'kommentiert', 'gemeinfrei']);
  });

  it('ohne Leereinträge, Dubletten und das technische „personal"', () => {
    expect(courseTagList(' ,personal,Taktik,,taktik ')).toEqual(['Taktik']);
    expect(courseTagList(null)).toEqual([]);
  });
});
