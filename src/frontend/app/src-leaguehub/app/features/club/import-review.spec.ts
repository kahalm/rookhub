import { ClubPreview, PreviewSide, SideMatch } from '../../core/club.models';
import { ImportReview, included, needsLook, optionalGame, reviewStatus } from './import-review';

const M = (x: Partial<SideMatch> = {}): SideMatch =>
  ({ league: false, ambiguous: false, name: null, fide: null, club: false, candidates: [], ...x });
const S = (raw: string, m: SideMatch, x: Partial<PreviewSide> = {}): PreviewSide =>
  ({ raw, elo: null, match: m, owner: false, replace: m.club, ...x });

const PREVIEW: ClubPreview = {
  truncated: false,
  games: [
    { index: 1, year: 2024, result: '1-0', event: null, plies: 40, opening: '1.e4 c5', error: null, duplicate: false,
      white: S('Oberschmid, Patrik', M({ league: true, name: 'Oberschmid, Patrik', fide: '900', club: true }), { owner: true }),
      black: S('Hengl, Philip', M({ league: true, name: 'Hengl, Philip', fide: '222' })) },
    { index: 2, year: 2024, result: '0-1', event: null, plies: 30, opening: '', error: null, duplicate: false,
      white: S('Nobody', M()), black: S('Somebody', M()) },
    { index: 3, year: null, result: '*', event: null, plies: 0, opening: '', error: 'illegal', duplicate: false,
      white: S('A', M()), black: S('B', M()) },
    { index: 4, year: 2024, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: true,
      white: S('Hengl, Philip', M({ league: true, name: 'Hengl, Philip', fide: '222' })), black: S('X', M()) },
    { index: 5, year: 2024, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: false,
      white: S('Huber, F.', M({ league: true, ambiguous: true, candidates: [
        { name: 'Huber, Franz', fide: '1', teams: ['Absam'], club: false }, { name: 'Huber, Florian', fide: '2', teams: ['Hall'], club: false }] })),
      black: S('Binder', M({ league: true, name: 'Binder, Moriz', fide: '111', club: true })) },
  ],
};

describe('ImportReview', () => {
  it('Status je Partie: dieselbe Regel wie am Server', () => {
    const r = new ImportReview(PREVIEW, true);
    const status = r.games().map(g => reviewStatus(g).reason);
    expect(status).toEqual([null, 'noLeaguePlayer', 'illegal', 'duplicate', null]);
    expect(r.counts()).toEqual({ total: 5, take: 2, skip: 3, unknown: 2, optional: 0 });
  });

  it('ohne Häkchen wird nichts ersetzt; mit Häkchen Schwaz-Spieler und ich', () => {
    expect(new ImportReview(PREVIEW, false).games()[0].white.replace).toBeFalse();
    const r = new ImportReview(PREVIEW, true);
    expect(r.games()[0].white.replace).toBeTrue();
    expect(r.games()[4].black.replace).toBeTrue();
  });

  it('Spieler korrigieren: ein Kandidat macht die Partie eindeutig, ein getippter Name wird übernommen', () => {
    const r = new ImportReview(PREVIEW, true);
    r.choosePerson(5, 'white', { name: 'Huber, Franz', fide: '1', teams: ['Absam'], club: false });
    expect(r.games()[4].white).toEqual(jasmine.objectContaining({ name: 'Huber, Franz', fide: '1', ambiguous: false, changed: true }));
    r.setTyped(2, 'white', 'Hengl Philip', M({ league: true, name: 'Hengl, Philip', fide: '222' }));
    expect(included(r.games()[1])).toBeTrue();
    expect(r.counts().take).toBe(3);
  });

  it('abwählen und beide Seiten ersetzt → nicht importiert; die Entscheidungen gehen nur für übernommene raus', () => {
    const r = new ImportReview(PREVIEW, true);
    r.toggleInclude(5);
    r.setReplace(1, 'black', true);
    expect(reviewStatus(r.games()[0]).reason).toBe('onlyOwnClub');
    expect(r.decisions()).toEqual([]);
    r.setReplace(1, 'black', false);
    expect(r.decisions()).toEqual([{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '222', replace: false } }]);
  });

  it('Gegner nur in der Megabase: „nicht in Liga", übernehmbar, aber nicht vorgewählt; anhaken nimmt sie auf', () => {
    const r = new ImportReview({ truncated: false, games: [
      { index: 1, year: 2025, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: false,
        white: S('Oberschmid, Patrik', M({ league: true, name: 'Oberschmid, Patrik', fide: '900', club: true }), { owner: true }),
        black: S('Bodrov, Timofey', M({ mega: true, name: 'Bodrov, Timofey', fide: '14131781' })) },
    ] }, true);
    const g = r.games()[0];
    expect(g.black).toEqual(jasmine.objectContaining({ mega: true, league: false, fide: '14131781' }));
    expect(reviewStatus(g)).toEqual({ importable: true, reason: null });
    expect(optionalGame(g)).toBeTrue();
    expect(included(g)).toBeFalse();
    expect(needsLook(g.black)).toBeFalse();                              // erkannt, nur eben nicht in der Liga
    expect(r.counts()).toEqual(jasmine.objectContaining({ take: 0, unknown: 0, optional: 1 }));
    r.toggleInclude(1);
    expect(r.decisions()).toEqual([{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '14131781', replace: false } }]);
  });

  it('einen Spieler aus der Megabase wählen macht die Partie übernehmbar UND wählt sie aus', () => {
    const r = new ImportReview(PREVIEW, true);
    expect(reviewStatus(r.games()[1]).reason).toBe('noLeaguePlayer');
    r.choosePerson(2, 'black', { name: 'Hengl, Peter', fide: '777', teams: [], club: false, league: false, source: 'mega' });
    const g = r.games()[1];
    expect(g.black).toEqual(jasmine.objectContaining({ mega: true, league: false, fide: '777', changed: true }));
    expect(included(g)).toBeTrue();
    expect(r.decisions().find(d => d.index === 2)?.black).toEqual({ name: 'Hengl, Peter', fide: '777', replace: false });
  });

  it('eine Korrektur gilt für jede andere Seite mit demselben PGN-Namen, die noch niemand angefasst hat', () => {
    const g = (index: number, white: PreviewSide, black: PreviewSide) =>
      ({ index, year: 2025, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: false, white, black });
    const me = () => S('Oberschmid, Patrik', M({ league: true, name: 'Oberschmid, Patrik', fide: '900', club: true }), { owner: true });
    const r = new ImportReview({ truncated: false, games: [
      g(1, me(), S('Kostic', M())), g(2, S('KOSTIC ', M()), me()), g(3, me(), S('Kostic', M())), g(4, me(), S('Huber', M())),
    ] }, true);
    r.toggleInclude(3);                                                                // Häkchen selbst angefasst
    const n = r.choosePerson(1, 'black', { name: 'Kostic, Vladimir', fide: '901482', teams: [], club: false, league: false, source: 'mega' });
    expect(n).toBe(2);
    expect(r.games()[1].white).toEqual(jasmine.objectContaining({ name: 'Kostic, Vladimir', fide: '901482', mega: true, changed: true }));
    expect(r.games()[2].black.fide).toBe('901482');
    expect(r.games()[3].black.changed).toBeFalse();                                    // anderer Name: bleibt
    expect(included(r.games()[0])).toBeTrue();                                         // gewählt = importieren
    expect(included(r.games()[1])).toBeFalse();                                        // übernommen: Vorgabe „nicht in Liga"
    expect(r.games()[2].excluded).toBeTrue();                                          // selbst angefasst (abgewählt): bleibt
    // schon selbst gesetzte Seiten überschreibt eine spätere Korrektur nicht
    expect(r.choosePerson(2, 'white', { name: 'Kostic, Milan', fide: '42', teams: ['Absam'], club: false })).toBe(0);
  });

  it('eine gemerkte Zuordnung vom Server steht als alias an der Seite', () => {
    const r = new ImportReview({ truncated: false, games: [
      { index: 1, year: 2025, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: false,
        white: S('Andi S.', M({ league: true, alias: true, name: 'Schnabl, Andreas Dr.', fide: '333' })),
        black: S('Hengl, Philip', M({ league: true, name: 'Hengl, Philip', fide: '222' })) },
    ] }, true);
    expect(r.games()[0].white).toEqual(jasmine.objectContaining({ alias: true, league: true, fide: '333' }));
    expect(needsLook(r.games()[0].white)).toBeFalse();
  });

  it('nur über den Nachnamen zugeordnet braucht einen Blick', () => {
    const r = new ImportReview({ truncated: false, games: [
      { index: 1, year: 2025, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: false,
        white: S('Kostic', M({ league: true, name: 'Kostic, Milan', fide: '42', lastNameOnly: true })),
        black: S('Hengl, Philip', M({ league: true, name: 'Hengl, Philip', fide: '222' })) },
    ] }, true);
    expect(needsLook(r.games()[0].white)).toBeTrue();
    expect(included(r.games()[0])).toBeTrue();
  });

  it('Filter „nicht importiert" und „nicht erkannt"', () => {
    const r = new ImportReview(PREVIEW, true);
    r.filter.set('skipped');
    expect(r.visible().map(g => g.game.index)).toEqual([2, 3, 4]);
    r.filter.set('new');
    expect(r.visible().map(g => g.game.index)).toEqual([1, 2, 3, 5]);   // Partie 4 ist schon da
    r.filter.set('unknown');
    expect(r.visible().map(g => g.game.index)).toEqual([2, 5]);
    expect(r.filterCounts()).toEqual({ all: 5, skipped: 3, new: 4, unknown: 2 });
  });
});
