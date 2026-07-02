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
      lastMode: null, lastActivityAt: null, isOwned: false, isPinned: false, ...over,
    };
  }

  function buildWith(items: CourseListItem[]): CourseListComponent {
    const courseService = { getCourses: () => of(items) } as any;
    const comp = new CourseListComponent(courseService, {} as any, {} as any, {} as any);
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

  describe('inProgressCourses', () => {
    function item2(over: Partial<CourseListItem>): CourseListItem {
      return {
        bookId: 0, fileName: 'x.pgn', displayName: 'X', difficulty: null, rating: null,
        tags: null, description: null, puzzleCount: 10, solvedCount: 0, progressPercent: 0,
        lastMode: null, lastActivityAt: null, isOwned: false, isPinned: false, ...over,
      };
    }

    it('zeigt nur begonnene, unfertige Kurse (≥1 gelöst, < gesamt)', () => {
      const courseService = { getCourses: () => of([
        item2({ bookId: 1, solvedCount: 3, lastActivityAt: '2026-06-01T10:00:00Z' }),  // in Arbeit
        item2({ bookId: 2, solvedCount: 0, lastActivityAt: null }),                    // nie begonnen
        item2({ bookId: 3, solvedCount: 10, lastActivityAt: '2026-06-02T10:00:00Z' }), // fertig
      ]) } as any;
      const comp = new CourseListComponent(courseService, {} as any, {} as any, {} as any);
      comp.loadCourses();
      expect(comp.inProgressCourses.map(c => c.bookId)).toEqual([1]);
    });

    it('zeigt angepinnte Kurse ganz oben in „In Arbeit" — auch wenn noch nichts gelöst wurde', () => {
      const courseService = { getCourses: () => of([
        item2({ bookId: 1, solvedCount: 3 }),                        // in Arbeit, nicht gepinnt
        item2({ bookId: 2, solvedCount: 0, isPinned: true }),         // gepinnt, noch nicht angefangen
        item2({ bookId: 3, solvedCount: 5, isPinned: true }),         // gepinnt + in Arbeit
        item2({ bookId: 4, solvedCount: 0, isPinned: false }),        // weder gepinnt noch angefangen
        item2({ bookId: 5, solvedCount: 10, isPinned: true }),        // gepinnt, aber schon fertig → NICHT in-progress
      ]) } as any;
      const comp = new CourseListComponent(courseService, {} as any, {} as any, {} as any);
      comp.loadCourses();
      // Angepinnte zuerst (2, 3), danach der begonnene Rest (1). BookId 4 (weder-noch) und 5 (fertig) draußen.
      expect(comp.inProgressCourses.map(c => c.bookId)).toEqual([2, 3, 1]);
    });

    it('verschwindet nach Reset (solvedCount=0, lastActivityAt bleibt gesetzt)', () => {
      const courseService = { getCourses: () => of([
        item2({ bookId: 1, solvedCount: 3, lastActivityAt: '2026-06-01T10:00:00Z' }),
      ]) } as any;
      const comp = new CourseListComponent(courseService, {} as any, {} as any, {} as any);
      comp.loadCourses();
      expect(comp.inProgressCourses.length).toBe(1);

      comp.courses[0].solvedCount = 0;   // Reset hat den Fortschritt geleert
      expect(comp.inProgressCourses.length).toBe(0);
    });

    it('filtert Kurse nach Suchtext (Titel, case-insensitive) über alle Sektionen', () => {
      const courseService = { getCourses: () => of([
        item2({ bookId: 1, displayName: 'Sicilian Defense', isOwned: true }),
        item2({ bookId: 2, displayName: 'French Defense', isOwned: true }),
      ]) } as any;
      const comp = new CourseListComponent(courseService, {} as any, {} as any, {} as any);
      comp.loadCourses();
      comp.search = 'SICIL';
      expect(comp.filtered.map(c => c.bookId)).toEqual([1]);
      expect(comp.chessableCourses.map(c => c.bookId)).toEqual([1]);
      comp.search = '';
      expect(comp.filtered.length).toBe(2);
    });
  });

  describe('upload/delete des eigenen Kurses', () => {
    function item3(over: Partial<CourseListItem>): CourseListItem {
      return {
        bookId: 0, fileName: 'x.pgn', displayName: 'X', difficulty: null, rating: null,
        tags: null, description: null, puzzleCount: 10, solvedCount: 0, progressPercent: 0,
        lastMode: null, lastActivityAt: null, isOwned: false, isPinned: false, ...over,
      };
    }
    const snackbar = { info: () => {} } as any;
    const translate = { instant: (k: string) => k } as any;

    it('hängt einen hochgeladenen Kurs an die Liste und meldet Zugriffsänderung', () => {
      const uploaded = item3({ bookId: 42, displayName: 'My Course', isOwned: true });
      const notify = jasmine.createSpy('notifyAccessChanged');
      const courseService = {
        getCourses: () => of([]),
        uploadCourse: jasmine.createSpy('uploadCourse').and.returnValue(of(uploaded)),
        notifyAccessChanged: notify,
      } as any;
      const comp = new CourseListComponent(courseService, snackbar, translate, {} as any);
      comp.loadCourses();

      const file = new File(['pgn'], 'my.pgn');
      comp.uploadCourseFile(file, 'My Course');

      expect(courseService.uploadCourse).toHaveBeenCalledWith(file, 'My Course');
      expect(comp.courses.map(c => c.bookId)).toEqual([42]);
      expect(comp.uploading).toBeFalse();
      expect(notify).toHaveBeenCalled();
    });

    it('öffnet den Upload-Dialog und lädt bei Bestätigung hoch', () => {
      const uploaded = item3({ bookId: 99, displayName: 'From Dialog', isOwned: true });
      const courseService = {
        getCourses: () => of([]),
        uploadCourse: jasmine.createSpy('uploadCourse').and.returnValue(of(uploaded)),
        notifyAccessChanged: () => {},
      } as any;
      const file = new File(['pgn'], 'd.pgn');
      const dialogRef = { afterClosed: () => of({ file, name: 'From Dialog' }) } as any;
      const dialog = { open: jasmine.createSpy('open').and.returnValue(dialogRef) } as any;
      const comp = new CourseListComponent(courseService, snackbar, translate, dialog);
      comp.loadCourses();
      comp.openUploadDialog();

      expect(dialog.open).toHaveBeenCalled();
      expect(courseService.uploadCourse).toHaveBeenCalledWith(file, 'From Dialog');
      expect(comp.courses.map(c => c.bookId)).toEqual([99]);
    });

    it('ignoriert einen abgebrochenen Upload-Dialog', () => {
      const courseService = {
        getCourses: () => of([]),
        uploadCourse: jasmine.createSpy('uploadCourse'),
        notifyAccessChanged: () => {},
      } as any;
      const dialogRef = { afterClosed: () => of(undefined) } as any;
      const dialog = { open: () => dialogRef } as any;
      const comp = new CourseListComponent(courseService, snackbar, translate, dialog);
      comp.loadCourses();
      comp.openUploadDialog();

      expect(courseService.uploadCourse).not.toHaveBeenCalled();
    });

    it('löscht einen eigenen Kurs nach Bestätigung aus der Liste', () => {
      spyOn(window, 'confirm').and.returnValue(true);
      const notify = jasmine.createSpy('notifyAccessChanged');
      const courseService = {
        getCourses: () => of([ item3({ bookId: 7, isOwned: true }) ]),
        deleteCourse: jasmine.createSpy('deleteCourse').and.returnValue(of(void 0)),
        notifyAccessChanged: notify,
      } as any;
      const comp = new CourseListComponent(courseService, snackbar, translate, {} as any);
      comp.loadCourses();
      comp.deleteCourse(comp.courses[0]);

      expect(courseService.deleteCourse).toHaveBeenCalledWith(7);
      expect(comp.courses.length).toBe(0);
      expect(notify).toHaveBeenCalled();
    });

    it('löscht nichts, wenn die Rückfrage abgelehnt wird', () => {
      spyOn(window, 'confirm').and.returnValue(false);
      const courseService = {
        getCourses: () => of([ item3({ bookId: 7, isOwned: true }) ]),
        deleteCourse: jasmine.createSpy('deleteCourse'),
        notifyAccessChanged: () => {},
      } as any;
      const comp = new CourseListComponent(courseService, snackbar, translate, {} as any);
      comp.loadCourses();
      comp.deleteCourse(comp.courses[0]);
      expect(courseService.deleteCourse).not.toHaveBeenCalled();
      expect(comp.courses.length).toBe(1);
    });
  });
});
