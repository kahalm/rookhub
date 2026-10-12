import { TestBed } from '@angular/core/testing';
import { KidsStarsStore } from './kids-stars.store';

describe('KidsStarsStore', () => {
  const KEY = 'rh-kids-stars-v1';
  beforeEach(() => localStorage.removeItem(KEY));
  afterEach(() => localStorage.removeItem(KEY));

  it('öffnet Stufe für Stufe und merkt sich die geschafften', () => {
    const store = TestBed.inject(KidsStarsStore);
    expect(store.isOpen(1)).toBeTrue();
    expect(store.isOpen(2)).toBeFalse();
    expect(store.current()).toBe(1);
    store.complete(1);
    expect(store.isOpen(2)).toBeTrue();
    expect(store.done()).toBe(1);
    expect(store.current()).toBe(2);
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ done: [1] });
  });

  it('liest den gespeicherten Stand und überlebt Unsinn', () => {
    localStorage.setItem(KEY, JSON.stringify({ done: [1, 2, 'x', -3] }));
    expect(TestBed.inject(KidsStarsStore).done()).toBe(2);
    TestBed.resetTestingModule();
    localStorage.setItem(KEY, '{kaputt');
    expect(TestBed.inject(KidsStarsStore).done()).toBe(0);
  });

  it('schaltet per Link eine Stufe frei, geschafft öffnet sie die nächste', () => {
    const store = TestBed.inject(KidsStarsStore);
    expect(store.unlock(17)).toBeTrue();
    expect(store.isOpen(17)).toBeTrue();
    expect(store.isOpen(16)).toBeFalse();
    expect(store.isOpen(18)).toBeFalse();
    store.complete(17);
    expect(store.isOpen(18)).toBeTrue();
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ done: [17], unlocked: [17] });
    expect(store.unlock(999)).toBeFalse();
    TestBed.resetTestingModule();
    expect(TestBed.inject(KidsStarsStore).isOpen(17)).toBeTrue();
  });
});
