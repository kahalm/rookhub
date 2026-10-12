import { courseTagList } from './course-detail-text.util';

/** UI-Sweep v1: Kursbeschreibung zugeklappt (v1-rk-kursseite-beschreibung-vorlauf-handy), Schlagwörter als Liste
 *  (v1-rk-kursseite-schlagwoerter-ohne-leerzeichen). */
describe('courseTagList', () => {
  it('trennt den rohen Komma-String in einzelne Schlagwörter', () => {
    expect(courseTagList('Meisterpartien,kommentiert, gemeinfrei')).toEqual(['Meisterpartien', 'kommentiert', 'gemeinfrei']);
  });

  it('ohne Leereinträge, Dubletten und das technische „personal"', () => {
    expect(courseTagList(' ,personal,Taktik,,taktik ')).toEqual(['Taktik']);
    expect(courseTagList(null)).toEqual([]);
  });
});
