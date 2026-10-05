import { addTag, distinctTags, filterByTag, MAX_TAG_LENGTH, MAX_TAGS } from './tags.util';

describe('tags.util', () => {
  const games = [{ id: 1, tags: ['Endspiel', 'lehrreich'] }, { id: 2, tags: ['endspiel'] }, { id: 3, tags: [] }, { id: 4 }];

  it('listet jeden Tag einmal, unabhängig von der Schreibweise', () => {
    expect(distinctTags(games)).toEqual(['Endspiel', 'lehrreich']);
  });

  it('filtert ohne Groß-/Kleinschreibung; leer lässt alles', () => {
    expect(filterByTag(games, 'ENDSPIEL').map(g => g.id)).toEqual([1, 2]);
    expect(filterByTag(games, 'lehrreich').map(g => g.id)).toEqual([1]);
    expect(filterByTag(games, '').length).toBe(4);
    expect(filterByTag(games, 'nix')).toEqual([]);
  });

  it('fügt bereinigt hinzu und lehnt Leeres, Doppeltes und Überzähliges ab', () => {
    expect(addTag(['a'], '  Eröffnung   neu ,')).toEqual(['a', 'Eröffnung neu']);
    expect(addTag(['Eröffnung'], 'eröffnung')).toBeNull();
    expect(addTag([], '  ,  ')).toBeNull();
    expect(addTag(Array.from({ length: MAX_TAGS }, (_, i) => 't' + i), 'x')).toBeNull();
    expect(addTag([], 'x'.repeat(100))![0].length).toBe(MAX_TAG_LENGTH);
  });
});
