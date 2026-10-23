import { of, throwError } from 'rxjs';
import { CourseEbookRefs } from './course-ebook-refs';

describe('CourseEbookRefs', () => {
  const ref = (lineId: number, page: number) => ({ lineId, ebookId: 1, ebookTitle: 'B', diagramId: page, page, label: null });

  it('je Linie gilt der erste Verweis; ohne Quelle leer, Fehler still', () => {
    const r = new CourseEbookRefs();
    r.load(of([ref(1, 10), ref(1, 20), ref(2, 30)]));
    expect(r.get(1)!.page).toBe(10);
    expect(r.get(2)!.page).toBe(30);
    expect(r.get(3)).toBeNull();
    expect(r.get(null)).toBeNull();
    r.load(null);
    expect(r.get(1)).toBeNull();
    r.load(throwError(() => new Error('x')));
    expect(r.get(2)).toBeNull();
  });
});
