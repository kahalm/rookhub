import { of } from 'rxjs';
import { CourseListComponent } from './course-list.component';
import { CourseListItem } from './course.service';

/**
 * Reihenfolge-Logik der Kursübersicht: angefangene Bücher (lastActivityAt gesetzt) nach vorn,
 * nach letzter Verwendung absteigend; noch nicht angefangene danach, alphabetisch.
 */
describe('CourseListComponent sorting', () => {
  function item(over: Partial<CourseListItem>): CourseListItem {
    return {
      bookId: 0, fileName: 'x.pgn', displayName: 'X', difficulty: null, rating: null,
      tags: null, description: null, puzzleCount: 10, solvedCount: 0, progressPercent: 0,
      lastMode: null, lastActivityAt: null, isOwned: false, ...over,
    };
  }

  function buildWith(items: CourseListItem[]): CourseListComponent {
    const courseService = { getCourses: () => of(items) } as any;
    const comp = new CourseListComponent(courseService, {} as any, {} as any);
    comp.loadCourses();
    return comp;
  }

  it('puts started courses first, most recently used on top', () => {
    const comp = buildWith([
      item({ bookId: 1, displayName: 'Beta',  lastActivityAt: '2026-06-01T10:00:00Z' }),
      item({ bookId: 2, displayName: 'Alpha', lastActivityAt: null }),
      item({ bookId: 3, displayName: 'Gamma', lastActivityAt: '2026-06-10T10:00:00Z' }),
    ]);

    expect(comp.courses.map(c => c.bookId)).toEqual([3, 1, 2]);
  });

  it('orders not-started courses alphabetically after started ones', () => {
    const comp = buildWith([
      item({ bookId: 1, displayName: 'Zulu',    lastActivityAt: null }),
      item({ bookId: 2, displayName: 'Charlie', lastActivityAt: null }),
      item({ bookId: 3, displayName: 'Mike',    lastActivityAt: '2026-06-05T10:00:00Z' }),
    ]);

    expect(comp.courses.map(c => c.bookId)).toEqual([3, 2, 1]);
  });

  it('keeps the started-first order within the owned/public split', () => {
    const comp = buildWith([
      item({ bookId: 1, displayName: 'Pub-A', isOwned: false, lastActivityAt: null }),
      item({ bookId: 2, displayName: 'Pub-B', isOwned: false, lastActivityAt: '2026-06-02T10:00:00Z' }),
      item({ bookId: 3, displayName: 'Own-A', isOwned: true,  lastActivityAt: null }),
      item({ bookId: 4, displayName: 'Own-B', isOwned: true,  lastActivityAt: '2026-06-03T10:00:00Z' }),
    ]);

    expect(comp.publicCourses.map(c => c.bookId)).toEqual([2, 1]);
    expect(comp.chessableCourses.map(c => c.bookId)).toEqual([4, 3]);
  });
});
