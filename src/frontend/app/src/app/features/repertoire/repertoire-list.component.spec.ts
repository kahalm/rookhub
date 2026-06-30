import { RepertoireListComponent } from './repertoire-list.component';

/**
 * Reiner Test des Such-Filters (filteredRepertoires) — Name + Beschreibung, case-insensitive.
 * Ohne TestBed: der Getter hängt nur an `repertoires` + `search`.
 */
describe('RepertoireListComponent search filter', () => {
  function make(): RepertoireListComponent {
    const comp = new RepertoireListComponent({} as any, {} as any, {} as any, {} as any);
    comp.repertoires = [
      { id: 1, name: 'Sicilian Najdorf', description: 'Sharp lines', kind: 0, fileCount: 2, isPublic: false },
      { id: 2, name: 'London System', description: 'Solid setup', kind: 1, fileCount: 1, isPublic: false },
      { id: 3, name: 'Caro-Kann', description: 'vs e4 (najdorf-free)', kind: 0, fileCount: 1, isPublic: false },
    ] as any;
    return comp;
  }

  it('returns all repertoires when the search is empty', () => {
    const comp = make();
    expect(comp.filteredRepertoires.length).toBe(3);
  });

  it('matches on the name, case-insensitive', () => {
    const comp = make();
    comp.search = 'LONDON';
    expect(comp.filteredRepertoires.map(r => r.id)).toEqual([2]);
  });

  it('also matches on the description', () => {
    const comp = make();
    comp.search = 'najdorf';
    expect(comp.filteredRepertoires.map(r => r.id).sort()).toEqual([1, 3]);
  });

  it('returns nothing for a non-matching query', () => {
    const comp = make();
    comp.search = 'zzz';
    expect(comp.filteredRepertoires.length).toBe(0);
  });
});
