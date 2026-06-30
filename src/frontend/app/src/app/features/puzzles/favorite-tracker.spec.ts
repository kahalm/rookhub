import { of } from 'rxjs';
import { FavoriteTracker } from './favorite-tracker';

function makeService(overrides: any = {}) {
  return {
    contains: jasmine.createSpy('contains').and.returnValue(of(false)),
    add: jasmine.createSpy('add').and.returnValue(of(true)),
    remove: jasmine.createSpy('remove').and.returnValue(of(false)),
    ...overrides,
  } as any;
}

describe('FavoriteTracker', () => {
  it('refresh holt den Server-Status für das aktuelle Puzzle (eingeloggt)', () => {
    const svc = makeService({ contains: jasmine.createSpy('contains').and.returnValue(of(true)) });
    const t = new FavoriteTracker(svc, 'standard', () => 5, () => 5, () => true);
    t.refresh();
    expect(svc.contains).toHaveBeenCalledWith('standard', 5);
    expect(t.currentIsFavorite).toBeTrue();
    expect(t.lastIsFavorite).toBeTrue(); // aktuelles == letztes → synchron
  });

  it('refresh ist ein No-op, wenn nicht eingeloggt', () => {
    const svc = makeService();
    const t = new FavoriteTracker(svc, 'standard', () => 5, () => 5, () => false);
    t.refresh();
    expect(svc.contains).not.toHaveBeenCalled();
    expect(t.currentIsFavorite).toBeFalse();
  });

  it('toggleCurrent fügt hinzu, wenn nicht favorisiert', () => {
    const svc = makeService();
    const t = new FavoriteTracker(svc, 'book', () => 9, () => null, () => true);
    t.toggleCurrent();
    expect(svc.add).toHaveBeenCalledWith('book', 9);
    expect(svc.remove).not.toHaveBeenCalled();
    expect(t.currentIsFavorite).toBeTrue();
  });

  it('toggleCurrent entfernt, wenn bereits favorisiert', () => {
    const svc = makeService();
    const t = new FavoriteTracker(svc, 'standard', () => 9, () => null, () => true);
    t.currentIsFavorite = true;
    t.toggleCurrent();
    expect(svc.remove).toHaveBeenCalledWith('standard', 9);
    expect(t.currentIsFavorite).toBeFalse();
  });

  it('toggleLast synchronisiert das aktuelle Flag, wenn aktuelles == letztes Puzzle', () => {
    const svc = makeService();
    const t = new FavoriteTracker(svc, 'standard', () => 7, () => 7, () => true);
    t.toggleLast();
    expect(svc.add).toHaveBeenCalledWith('standard', 7);
    expect(t.lastIsFavorite).toBeTrue();
    expect(t.currentIsFavorite).toBeTrue();
  });

  it('rollt bei Serverfehler den optimistischen Zustand zurück', () => {
    const svc = makeService({ add: jasmine.createSpy('add').and.returnValue({ subscribe: (h: any) => { (h.error ?? h)(); return { unsubscribe() {} }; } }) });
    const t = new FavoriteTracker(svc, 'standard', () => 3, () => null, () => true);
    t.toggleCurrent();
    expect(t.currentIsFavorite).toBeFalse(); // optimistisch true → Rollback auf false
  });

  it('toggle ist ein No-op ohne Puzzle-Id', () => {
    const svc = makeService();
    const t = new FavoriteTracker(svc, 'standard', () => null, () => null, () => true);
    t.toggleCurrent();
    expect(svc.add).not.toHaveBeenCalled();
  });
});
