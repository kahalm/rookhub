import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { FavoritesComponent } from './favorites.component';
import { FavoritePuzzle, FavoritesService } from '../../core/favorites.service';
import { SnackbarService } from '../../core/snackbar.service';

const STD: FavoritePuzzle = {
  id: 1, puzzleId: 42, source: 'Standard', rating: 1500, themes: 'fork pin', title: null,
  fen: 'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 0 1', moves: 'b8c6 f1b5', createdAt: '2026-06-30',
};
const BOOK: FavoritePuzzle = {
  id: 2, puzzleId: 77, source: 'Book', rating: 2000, themes: 'endgame', title: 'Kapitel 3',
  fen: '8/8/8/8/8/8/8/K6k w - - 0 1', moves: 'a1b1', createdAt: '2026-06-30',
};

function setup(overrides: any = {}) {
  const service: any = {
    list: jasmine.createSpy('list').and.returnValue(of([])),
    remove: jasmine.createSpy('remove').and.returnValue(of(false)),
    ...overrides,
  };
  const router: any = { navigate: jasmine.createSpy('navigate') };
  TestBed.configureTestingModule({
    imports: [FavoritesComponent],
    providers: [
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: FavoritesService, useValue: service },
      { provide: Router, useValue: router },
      { provide: SnackbarService, useValue: { warn: jasmine.createSpy('warn') } },
    ],
  });
  TestBed.overrideComponent(FavoritesComponent, { set: { template: '' } });
  const fixture = TestBed.createComponent(FavoritesComponent);
  return { c: fixture.componentInstance, fixture, service, router };
}

describe('FavoritesComponent', () => {
  it('lädt die Favoriten beim Init', () => {
    const { fixture, c, service } = setup({ list: jasmine.createSpy('list').and.returnValue(of([STD, BOOK])) });
    fixture.detectChanges(); // ngOnInit
    expect(service.list).toHaveBeenCalled();
    expect(c.favorites.length).toBe(2);
    expect(c.loading).toBeFalse();
  });

  it('replay eines Standard-Puzzles geht auf den Solver-Deep-Link', () => {
    const { c, router } = setup();
    c.replay(STD);
    expect(router.navigate).toHaveBeenCalledWith(['/puzzles', 42]);
  });

  it('replay eines Buch-Puzzles hängt ?single=1 an', () => {
    const { c, router } = setup();
    c.replay(BOOK);
    expect(router.navigate).toHaveBeenCalledWith(['/puzzles/book', 77], { queryParams: { single: 1 } });
  });

  it('analyze öffnet den Analysemodus mit Fen+Moves (Standard: Spieler ist die Gegenseite des Zugrechts)', () => {
    const { c, router } = setup();
    c.analyze(STD); // FEN am Zug: schwarz → Spieler weiß
    const args = router.navigate.calls.mostRecent().args;
    expect(args[0]).toEqual(['/analysis']);
    expect(args[1].queryParams.fen).toBe(STD.fen);
    expect(args[1].queryParams.moves).toBe('b8c6,f1b5');
    expect(args[1].queryParams.orientation).toBe('white');
    expect(args[1].queryParams.from).toBe('/favorites');
  });

  it('analyze eines Buch-Puzzles nimmt das Zugrecht als Ausrichtung', () => {
    const { c, router } = setup();
    c.analyze(BOOK); // FEN am Zug: weiß → Spieler weiß
    expect(router.navigate.calls.mostRecent().args[1].queryParams.orientation).toBe('white');
  });

  it('remove entfernt den Eintrag aus der Liste', () => {
    const { c, service } = setup({ remove: jasmine.createSpy('remove').and.returnValue(of(false)) });
    c.favorites = [STD, BOOK];
    c.remove(BOOK);
    expect(service.remove).toHaveBeenCalledWith('book', 77);
    expect(c.favorites.map(f => f.id)).toEqual([1]);
  });

  it('themeList splittet und begrenzt auf 6', () => {
    const { c } = setup();
    expect(c.themeList('a b,c  d')).toEqual(['a', 'b', 'c', 'd']);
  });
});
