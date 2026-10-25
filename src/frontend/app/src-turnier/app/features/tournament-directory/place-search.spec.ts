import { DestroyRef } from '@angular/core';
import { fakeAsync, tick } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { PlaceSearch } from './place-search';
import { GeoPlaceSuggestion } from './tournament-directory.model';

const HALLEIN: GeoPlaceSuggestion = { label: '5400 Hallein (AT)', country: 'AT', postalCode: '5400', lat: 47.68, lon: 13.1 };
const destroyRef = { onDestroy: () => () => undefined } as unknown as DestroyRef;

function setup(answer: (term: string) => Observable<GeoPlaceSuggestion[]>) {
  const asked: string[] = [];
  const search = new PlaceSearch(term => { asked.push(term); return answer(term); }, destroyRef);
  search.start();
  return { search, asked };
}

describe('PlaceSearch', () => {
  it('sucht erst ab zwei Zeichen und entprellt das Tippen', fakeAsync(() => {
    const { search, asked } = setup(() => of([HALLEIN]));

    search.input(' H ');
    expect(search.searching()).toBeFalse();
    search.input('Ha');
    search.input(' Hal ');
    expect(search.searching()).toBeTrue();
    tick(PlaceSearch.DebounceMs);

    expect(asked).toEqual(['Hal']);
    expect(search.suggestions()).toEqual([HALLEIN]);
    expect(search.searching()).toBeFalse();
    expect(search.noMatch()).toBeFalse();
  }));

  it('sagt „kein Ort gefunden" und, getrennt davon, „Suche gescheitert"', fakeAsync(() => {
    let fail = false;
    const { search } = setup(() => (fail ? throwError(() => new Error('500')) : of([])));

    search.input('Kleinstdorf');
    tick(PlaceSearch.DebounceMs);
    expect([search.noMatch(), search.failed()]).toEqual([true, false]);

    fail = true;
    search.input('Hallein');
    expect([search.noMatch(), search.failed()]).withContext('vor der Antwort abgeräumt').toEqual([false, false]);
    tick(PlaceSearch.DebounceMs);
    expect([search.noMatch(), search.failed(), search.searching()]).toEqual([false, true, false]);
  }));

  it('übersteht einen Fehler — die nächste Eingabe sucht wieder', fakeAsync(() => {
    let fail = true;
    const { search, asked } = setup(() => (fail ? throwError(() => new Error('500')) : of([HALLEIN])));
    search.input('Hallein');
    tick(PlaceSearch.DebounceMs);

    fail = false;
    search.input('Halleinx');
    tick(PlaceSearch.DebounceMs);

    expect(asked).toEqual(['Hallein', 'Halleinx']);
    expect(search.suggestions()).toEqual([HALLEIN]);
    expect(search.failed()).toBeFalse();
  }));

  it('fragt denselben Begriff erneut, wenn die Liste inzwischen leer ist', fakeAsync(() => {
    const { search, asked } = setup(() => of([HALLEIN]));
    search.input('ab');
    tick(PlaceSearch.DebounceMs);
    search.input('a');                      // leert die Liste
    search.input('ab');
    tick(PlaceSearch.DebounceMs);

    expect(asked).toEqual(['ab', 'ab']);
    expect(search.searching()).toBeFalse();
  }));

  it('lässt nur die jüngste Antwort gelten', fakeAsync(() => {
    const answers = new Map<string, Subject<GeoPlaceSuggestion[]>>();
    const { search } = setup(term => { const s = new Subject<GeoPlaceSuggestion[]>(); answers.set(term, s); return s; });
    search.input('Hal');
    tick(PlaceSearch.DebounceMs);
    search.input('Hallein');
    tick(PlaceSearch.DebounceMs);

    answers.get('Hal')!.next([{ ...HALLEIN, label: 'alt' }]);
    answers.get('Hallein')!.next([HALLEIN]);

    expect(search.suggestions()).toEqual([HALLEIN]);
  }));

  it('räumt Vorschläge und Hinweise ab, sobald ein Ort feststeht', fakeAsync(() => {
    const { search } = setup(() => of([]));
    search.input('Kleinstdorf');
    tick(PlaceSearch.DebounceMs);

    search.clear();

    expect([search.suggestions(), search.noMatch(), search.failed()]).toEqual([[], false, false]);
  }));
});
