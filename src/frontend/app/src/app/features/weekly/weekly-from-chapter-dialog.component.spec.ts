import { of } from 'rxjs';
import { WeeklyFromChapterDialogComponent } from './weekly-from-chapter-dialog.component';

describe('WeeklyFromChapterDialogComponent search filters', () => {
  let component: WeeklyFromChapterDialogComponent;

  beforeEach(() => {
    const ref = { close: () => {} } as any;
    const data = { date: '2026-07-10', time: '19:00' } as any;
    component = new WeeklyFromChapterDialogComponent(ref, data, {} as any, {} as any, {} as any, {} as any);
    component.books = [
      { bookId: 1, displayName: 'Tactics Booster', puzzleCount: 100 } as any,
      { bookId: 2, displayName: 'Endgame Essentials', puzzleCount: 50 } as any,
      { bookId: 3, displayName: 'Attacking Chess', puzzleCount: 30 } as any,
    ];
    component.chapters = [
      { index: 0, name: 'Pins', puzzleCount: 10 } as any,
      { index: 1, name: 'Forks', puzzleCount: 8 } as any,
      { index: 2, name: null, puzzleCount: 5 } as any,
    ];
  });

  it('returns all books when the filter is empty', () => {
    expect(component.filteredBooks.length).toBe(3);
  });

  it('filters books by displayName case-insensitively', () => {
    component.bookFilter = 'end';
    expect(component.filteredBooks.map(b => b.bookId)).toEqual([2]);
  });

  it('filters chapters by name and tolerates a null chapter name', () => {
    component.chapterFilter = 'for';
    expect(component.filteredChapters.map(c => c.index)).toEqual([1]);
  });

  it('resets the book filter when the panel closes', () => {
    component.bookFilter = 'end';
    component.onBookPanelToggle(false);
    expect(component.bookFilter).toBe('');
  });

  it('clears the chapter filter when the book changes', () => {
    component.chapterFilter = 'pin';
    component.bookId = null;
    component.onBookChange();
    expect(component.chapterFilter).toBe('');
  });
});

describe('WeeklyFromChapterDialogComponent Termin als UTC', () => {
  it('create schickt die eingegebene Wandzeit als UTC-ISO mit Z', () => {
    const weekly = { createFromChapter: jasmine.createSpy('createFromChapter').and.returnValue(of({ id: 9 })) };
    const ref = { close: jasmine.createSpy('close') };
    const c = new WeeklyFromChapterDialogComponent(ref as any, { date: '2026-07-10', time: '19:00' } as any,
      {} as any, weekly as any, {} as any, {} as any);
    c.bookId = 1;
    c.chapterIndex = 0;
    c.create();
    expect(weekly.createFromChapter.calls.mostRecent().args[2]).toBe(new Date(2026, 6, 10, 19, 0).toISOString());
    expect(ref.close).toHaveBeenCalled();
  });
});
