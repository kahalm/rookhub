import { Subject, of } from 'rxjs';
import { LatestRequest } from './latest-request.util';

describe('LatestRequest', () => {
  it('ein neuer Lauf bricht den vorigen ab: dessen spaete Antwort kommt nicht mehr an', () => {
    const latest = new LatestRequest();
    const a$ = new Subject<string>();
    const b$ = new Subject<string>();
    const seen: string[] = [];
    latest.run(a$, { next: v => seen.push(v) });
    latest.run(b$, { next: v => seen.push(v) });
    expect(a$.observed).toBeFalse();
    b$.next('B');
    a$.next('A');
    expect(seen).toEqual(['B']);
  });

  it('cancel bricht ab, ohne next oder error zu rufen', () => {
    const latest = new LatestRequest();
    const a$ = new Subject<string>();
    const next = jasmine.createSpy('next');
    const error = jasmine.createSpy('error');
    latest.run(a$, { next, error });
    latest.cancel();
    a$.next('A');
    a$.error(new Error('x'));
    expect(next).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
    latest.cancel();   // zweimal ist harmlos
  });

  it('synchron fertige Quellen laufen normal durch', () => {
    const latest = new LatestRequest();
    const seen: number[] = [];
    latest.run(of(1), { next: v => seen.push(v) });
    latest.run(of(2), { next: v => seen.push(v) });
    expect(seen).toEqual([1, 2]);
  });
});
