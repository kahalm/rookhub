import { ClubPreview, PreviewSide, SideMatch } from '../../core/club.models';
import { ImportReview, included, needsLook, optionalGame, pairingText, quickPicks, reviewStatus } from './import-review';

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
    expect(r.decisions()).toEqual([{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '222', replace: false }, leagueGameId: 0 }]);
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
    expect(r.decisions()).toEqual([{ index: 1, white: { name: null, fide: '900', replace: true }, black: { name: null, fide: '14131781', replace: false }, leagueGameId: 0 }]);
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

  it('Entwurf (0.595.0): der gespeicherte Stand stellt Korrekturen, Häkchen und die Vorgabe „ersetzen" wieder her', () => {
    const r = new ImportReview(PREVIEW, false);
    r.choosePerson(5, 'white', { name: 'Huber, Franz', fide: '1', teams: ['Absam'], club: false });
    r.toggleInclude(1);
    const back = ImportReview.restore(PREVIEW, r.snapshot(), true);         // die Vorgabe der Seite gilt nicht, der Stand schon
    expect(back.replaceClub).toBeFalse();
    expect(back.decisions()).toEqual(r.decisions());
    expect(back.games().find(g => g.game.index === 5)!.white.name).toBe('Huber, Franz');
    // Unlesbarer oder fremder Stand: einfach die Vorgaben.
    expect(ImportReview.restore(PREVIEW, '{kaputt', true).decisions()).toEqual(new ImportReview(PREVIEW, true).decisions());
  });

  it('Schnellauswahl ähnlicher Namen: nur an unerkannten, unangefassten Seiten; ein alter Entwurf behält sie (0.596.0)', () => {
    const philip = { name: 'Hengl, Philip', fide: '222', teams: ['Absam'], club: false };
    const preview: ClubPreview = { truncated: false, games: [
      { ...PREVIEW.games[1], white: S('Hengl, Phillip', M({ similar: [philip] })),
        black: S('Hengl, Philip', M({ league: true, name: 'Hengl, Philip', fide: '222', similar: [philip] })) },
    ] };
    const r = new ImportReview(preview, true);
    expect(quickPicks(r.games()[0].white)).toEqual([philip]);
    expect(quickPicks(r.games()[0].black)).toEqual([]);                               // erkannt: nichts vorzuschlagen
    r.setReplace(2, 'white', true);
    expect(quickPicks(r.games()[0].white)).toEqual([]);                               // „Schwaz" ersetzt: auch nicht
    r.setReplace(2, 'white', false);
    const old = JSON.parse(r.snapshot());
    delete old.games[0].white.similar;                                                // so sah ein Entwurf von 0.595.0 aus
    expect(quickPicks(ImportReview.restore(preview, JSON.stringify(old), true).games()[0].white)).toEqual([philip]);
    r.choosePerson(2, 'white', philip);
    expect(quickPicks(r.games()[0].white)).toEqual([]);
  });

  it('Entwurf wieder öffnen: nicht angefasste Seiten nehmen den frischen Abgleich, eigene Korrekturen bleiben (0.597.0)', () => {
    const forster = (m: SideMatch) => ({ ...PREVIEW.games[1], index: 1, white: S('Forster, Stephan', m), black: S('Lenk, Markus', m) });
    const cands = [{ name: 'Forster, Stephan', fide: '24649651', teams: ['SOG'], club: false },
                   { name: 'Forster, Stephan', fide: '24652091', teams: ['SOG'], club: false }];
    const old = new ImportReview({ truncated: false, games: [forster(M({ league: true, ambiguous: true, candidates: cands }))] }, true);
    old.setReplace(1, 'white', true);                                                 // auch „ersetzen" angefasst
    old.choosePerson(1, 'black', { name: 'Lenk, Markus', fide: null, teams: ['SOG'], club: false });
    const state = old.snapshot();
    const now: ClubPreview = { truncated: false, games: [forster(M({ league: true, name: 'Forster, Stephan', fide: '24652091' }))] };
    const r = ImportReview.restore(now, state, true).games()[0];
    expect(r.white).toEqual(jasmine.objectContaining({ ambiguous: false, fide: '24652091', replace: true }));  // frisch erkannt, Wahl bleibt
    expect(r.black).toEqual(jasmine.objectContaining({ name: 'Lenk, Markus', changed: true }));                // eigene Korrektur bleibt
  });
});

describe('ImportReview — Ligapaarung (0.678.0)', () => {
  const PAIR = { id: 42, label: '2026/27 · Landesliga · Runde 2 · Brett 4 (04.10.2026)', white: 'Hengl, Philip', whiteFide: '222',
    black: 'Oberschmid, Patrik', blackFide: '900', result: '1 - 0', whiteOwnClub: false, blackOwnClub: true, exact: true };
  const OTHER = { ...PAIR, id: 43, label: '2025/26 · Landesliga · Runde 7 · Brett 2 (01.03.2026)', exact: false };
  const P = (pairingId: number | null): ClubPreview => ({ truncated: false, games: [
    { index: 1, year: 2026, result: '1-0', event: null, plies: 40, opening: '', error: null, duplicate: false,
      white: S('Hengl', M()), black: S('Oberschmid', M({ league: true, name: 'Oberschmid, Patrik', fide: '900', club: true })),
      pairings: [PAIR, OTHER], pairingId },
  ] });

  it('die vorgewählte Paarung des Servers geht mit, „keine" als 0', () => {
    expect(new ImportReview(P(42), true).decisions()).toEqual([]);   // Weiß unbekannt, Schwarz ersetzt → nicht importierbar
    const r = new ImportReview(P(42), true);
    r.choosePerson(1, 'white', { name: 'Hengl, Philip', fide: '222', teams: [], club: false });
    expect(r.decisions()[0].leagueGameId).toBe(42);
    r.setPairing(1, null);
    expect(r.decisions()[0].leagueGameId).toBe(0);
  });

  it('eine gewählte Paarung setzt die nicht angefassten Spieler — Schwaz wird ersetzt — und nimmt die Partie auf', () => {
    const r = new ImportReview(P(null), true);
    r.setPairing(1, 42);
    const g = r.games()[0];
    expect(g.white).toEqual(jasmine.objectContaining({ name: 'Hengl, Philip', fide: '222', league: true, replace: false, changed: true }));
    expect(g.black).toEqual(jasmine.objectContaining({ fide: '900', club: true, replace: true }));
    expect(included(g)).toBeTrue();
    expect(r.decisions()[0]).toEqual(jasmine.objectContaining({ leagueGameId: 42 }));
  });

  it('der Entwurf merkt die Wahl; eine verschwundene Paarung fällt auf die Vorgabe zurück', () => {
    const r = new ImportReview(P(42), true);
    r.setPairing(1, 43);
    expect(ImportReview.restore(P(42), r.snapshot(), true).games()[0].pairingId).toBe(43);
    const gone: ClubPreview = { ...P(42), games: [{ ...P(42).games[0], pairings: [PAIR] }] };
    expect(ImportReview.restore(gone, r.snapshot(), true).games()[0].pairingId).toBe(42);
  });

  // 0.740.0: leeres Brett der laufenden Runde — Mannschaften statt Spieler, die Spieler der Partie bleiben
  it('leeres Brett der laufenden Runde: Text „noch nicht besetzt", Wahl lässt die Spieler stehen', () => {
    const OPEN = { ...PAIR, id: 50, label: '2026/27 · 1. Klasse · Runde 2 · Brett 5 (10.10.2026)', white: 'Schwaz', black: 'Freibauer Innsbruck',
      whiteFide: null, blackFide: null, open: true };
    expect(pairingText(OPEN)).toBe('2026/27 · 1. Klasse · Runde 2 · Brett 5 (10.10.2026) — Schwaz – Freibauer Innsbruck, noch nicht besetzt');
    const p: ClubPreview = { ...P(null), games: [{ ...P(null).games[0], pairings: [OPEN] }] };
    const r = new ImportReview(p, true);
    const before = r.games()[0].white;
    r.setPairing(1, 50);
    expect(r.games()[0].white).toEqual(before);
    expect(r.games()[0].pairingId).toBe(50);
  });

  it('Text der Auswahl: Spielplan-Namen, unsichere mit (?)', () => {
    expect(pairingText(PAIR)).toBe('2026/27 · Landesliga · Runde 2 · Brett 4 (04.10.2026) — Hengl, Philip – Oberschmid, Patrik');
    expect(pairingText(OTHER)).toContain('(?)');
  });
});
