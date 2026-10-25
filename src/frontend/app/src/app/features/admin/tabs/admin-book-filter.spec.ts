import { emptyBookFilters, filterBooks, hasActiveBookFilters } from './admin-book-filter';

/** Reine Filterlogik des Bücher-Tabs (Zerlegung Z30) — ohne TestBed. */
describe('admin-book-filter', () => {
  const books = [
    { id: 1, displayName: 'Endgame', fileName: 'e.pgn', tags: null, kind: 'Puzzle', difficulty: 'Easy', minElo: 1000, maxElo: 1500, puzzleCount: 50, forDaily: true, forRandom: false, forBlind: false, isPublic: true, forKids: false, accessGroupIds: [4] },
    { id: 2, displayName: 'Tactics', fileName: 't.pgn', tags: null, kind: 'Study', difficulty: 'Hard', minElo: 2000, maxElo: 2400, puzzleCount: 500, forDaily: false, forRandom: true, forBlind: false, isPublic: false, forKids: true, accessGroupIds: [] },
  ] as any[];
  const ids = (search: string, patch: object = {}) => filterBooks(books, search, { ...emptyBookFilters(), ...patch }).map(b => b.id);

  it('globale Suche über Name, Dateiname und Tags (case-insensitive)', () => {
    const list = [
      { id: 1, displayName: 'Endgame Essentials', fileName: 'endgame.pgn', tags: 'endgame' },
      { id: 2, displayName: 'Tactics Trainer', fileName: 'tactics.pgn', tags: 'fork,pin' },
    ] as any[];
    const f = emptyBookFilters();
    expect(filterBooks(list, '', f).length).toBe(2);
    expect(filterBooks(list, 'endGAME', f).map(b => b.id)).toEqual([1]);
    expect(filterBooks(list, 'pin', f).map(b => b.id)).toEqual([2]);
    expect(filterBooks(list, 'nope', f).length).toBe(0);
    expect(filterBooks(list, '  ', f).length).toBe(2);   // nur Leerzeichen = keine Suche
  });

  it('die Suche findet Bücher über den Besitzernamen', () => {
    const list = [{ id: 1, displayName: 'Kurs', fileName: 'a.pgn', ownerName: 'alice' }, { id: 2, displayName: 'Kurs', fileName: 'b.pgn', ownerName: 'bob' }] as any[];
    expect(filterBooks(list, 'bob', emptyBookFilters()).map(b => b.id)).toEqual([2]);
  });

  it('Spaltenfilter: Art, Drei-Zustand, Gruppe, Spannen — UND-verknüpft', () => {
    expect(ids('', { kind: 'Puzzle' })).toEqual([1]);
    expect(ids('', { public: 'no' })).toEqual([2]);
    expect(ids('', { daily: 'yes' })).toEqual([1]);
    expect(ids('', { random: 'yes' })).toEqual([2]);
    expect(ids('', { blind: 'yes' })).toEqual([]);
    expect(ids('', { group: 'none' })).toEqual([2]);
    expect(ids('', { group: 4 })).toEqual([1]);
    expect(ids('', { puzzlesMin: 100 })).toEqual([2]);
    expect(ids('', { puzzlesMax: 100 })).toEqual([1]);
    expect(ids('', { eloMax: 1600 })).toEqual([1]);
    expect(ids('', { eloMin: 1500 })).toEqual([2]);
    expect(ids('', { difficulty: 'hard' })).toEqual([2]);
    expect(ids('', { name: 'T.PGN' })).toEqual([2]);
    expect(ids('', { kind: 'Puzzle', public: 'no' })).toEqual([]);
    expect(ids('tactics', { kind: 'Study' })).toEqual([2]);
  });

  it('Elo-Spanne: Bücher ohne Grenze fallen bei gesetztem Filter heraus', () => {
    const list = [{ id: 9, displayName: 'x', minElo: null, maxElo: null, puzzleCount: 1 }] as any[];
    expect(filterBooks(list, '', { ...emptyBookFilters(), eloMin: 0 }).length).toBe(0);
    expect(filterBooks(list, '', { ...emptyBookFilters(), eloMax: 3000 }).length).toBe(0);
  });

  it('hasActiveBookFilters: leer = nein, jedes gesetzte Feld = ja (auch eine 0)', () => {
    expect(hasActiveBookFilters(emptyBookFilters())).toBeFalse();
    expect(hasActiveBookFilters({ ...emptyBookFilters(), difficulty: 'hard' })).toBeTrue();
    expect(hasActiveBookFilters({ ...emptyBookFilters(), eloMin: 0 })).toBeTrue();
    expect(hasActiveBookFilters({ ...emptyBookFilters(), group: 'none' })).toBeTrue();
  });

  it('emptyBookFilters liefert jedes Mal ein neues Objekt', () => {
    expect(emptyBookFilters()).not.toBe(emptyBookFilters());
    expect(emptyBookFilters()).toEqual(emptyBookFilters());
  });
});
