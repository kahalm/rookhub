import { of, throwError } from 'rxjs';
import { MarkSet } from './mark-set';

describe('MarkSet', () => {
  it('starts empty and behaves like a Set', () => {
    const marks = new MarkSet<number>();
    expect(marks.size).toBe(0);
    expect(marks.has(1)).toBeFalse();
    expect([...marks]).toEqual([]);
  });

  it('replace swaps the whole state', () => {
    const marks = new MarkSet<string>();
    marks.replace(['a', 'b']);
    expect(marks.size).toBe(2);
    expect(marks.has('a')).toBeTrue();
    marks.replace([]);
    expect(marks.size).toBe(0);
  });

  it('toggle marks optimistically and persists the target state', () => {
    const marks = new MarkSet<number>();
    const persist = jasmine.createSpy('persist').and.returnValue(of({ marked: true }));

    expect(marks.toggle(7, persist)).toBeTrue();

    expect(marks.has(7)).toBeTrue();
    expect(persist).toHaveBeenCalledWith(7, true);
  });

  it('toggle unmarks an already marked key', () => {
    const marks = new MarkSet<number>();
    marks.replace([7]);
    const persist = jasmine.createSpy('persist').and.returnValue(of({ marked: false }));

    expect(marks.toggle(7, persist)).toBeFalse();

    expect(marks.has(7)).toBeFalse();
    expect(persist).toHaveBeenCalledWith(7, false);
  });

  /** Der Punkt der Klasse: eine verweigerte Speicherung darf die Checkbox nicht anders stehen
   *  lassen als den Server. */
  it('rolls the mark back when the server refuses', () => {
    const marks = new MarkSet<string>();
    marks.toggle('k1', () => throwError(() => new Error('500')));
    expect(marks.has('k1')).toBeFalse();
  });

  it('rolls an unmark back as well', () => {
    const marks = new MarkSet<string>();
    marks.replace(['k1']);
    marks.toggle('k1', () => throwError(() => new Error('500')));
    expect(marks.has('k1')).toBeTrue();
  });

  it('touches only the toggled key on a rollback', () => {
    const marks = new MarkSet<string>();
    marks.replace(['a', 'b']);
    marks.toggle('c', () => throwError(() => new Error('500')));
    expect([...marks].sort()).toEqual(['a', 'b']);
  });
});
