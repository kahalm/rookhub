import { fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ClubClient } from '../../core/club-api.service';
import { ClubMatch, ClubPairing, LeagueScanState } from '../../core/club.models';
import { ScanHeaderForm } from './scan-header-form';

const MATCH: ClubMatch = {
  white: { league: true, ambiguous: false, name: 'Oberschmid, Patrik', fide: '900', club: true, candidates: [] },
  black: { league: false, ambiguous: false, name: null, fide: null, club: false, candidates: [] },
};
const PAIRING: ClubPairing = { id: 5, label: 'Runde 3 (12.01.2025)', white: 'Moser, Axel', whiteFide: '11', black: 'Hengl, Philip',
  blackFide: '22', result: '1-0', whiteOwnClub: true, blackOwnClub: false, exact: true };

describe('ScanHeaderForm', () => {
  let api: jasmine.SpyObj<Pick<ClubClient, 'match' | 'pairings'>>;
  const form = (pairingsEnabled = true) => new ScanHeaderForm({ api, anon: () => 'Testdorf', clubName: () => 'Testdorf', pairingsEnabled });

  beforeEach(() => {
    api = jasmine.createSpyObj('ClubClient', ['match', 'pairings']);
    api.match.and.resolveTo(MATCH);
    api.pairings.and.resolveTo([PAIRING]);
  });

  it('übernimmt die Kopfdaten, gleicht ab und ersetzt die Vereinsseite', fakeAsync(() => {
    const f = form();
    f.fill({ white: 'Oberschmid', black: 'Xy', date: null, result: '½', event: 'Simultan', ownerSide: 'black' } as unknown as LeagueScanState);
    flushMicrotasks();
    expect(f.result()).toBe('1/2-1/2');
    expect(f.event()).toBe('Simultan');
    expect(f.replace('white')()).toBeTrue();                 // Spieler des Vereins
    expect(f.replace('black')()).toBeTrue();                 // meine Seite
    expect(f.matchText('black')).toBe('wird „Testdorf“');
    expect(f.problem()).toBe('Nach dem Ersetzen bleibt kein bekannter Gegner übrig — so wird die Partie nicht angenommen.');
    tick(400);
    flushMicrotasks();
    expect(f.pairingId()).toBe(5);                           // eindeutiger Treffer vorgewählt
    f.dispose();
  }));

  it('eine gewählte Paarung bringt Spieler und Jahr', fakeAsync(() => {
    const f = form();
    f.pairings.set([PAIRING]);
    f.choosePairing('5');
    expect(f.name('white')()).toBe('Moser, Axel');
    expect(f.fide('black')()).toBe('22');
    expect(f.year()).toBe(2025);
    expect(f.preview()).toBe('2025 · Testdorf – Hengl, Philip · Ergebnis offen');
    tick(400);
    f.dispose();
  }));

  it('ohne Paarungs-Vorschläge (Korrektur) wird nichts geholt', fakeAsync(() => {
    const f = form(false);
    f.setName('white', 'Moser');
    tick(400);
    flushMicrotasks();
    expect(api.match).toHaveBeenCalled();
    tick(400);
    expect(api.pairings).not.toHaveBeenCalled();
  }));
});
