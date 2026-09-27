import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LeagueApiService } from '../core/league-api.service';
import { PlayerCard } from '../core/league.models';
import { PlayerCardComponent } from './player-card.component';

const CARD: PlayerCard = {
  fide: '1606921', name: 'Oberschmid, Patrik', n: 20, years: ['2019', '2026'], src: { Lumbra: 12, 'chess-results': 8 },
  white: { n: 9, first: [['e4', 7, 57], ['d4', 2, null]], lines: [['1.e4 c5 2.Nf3 d6 3.d4 cxd4', 3, 50]] },
  black_e4: { n: 6, first: [['c5', 6, 42]], lines: [] },
  black_d4: { n: 5, first: [['Nf6', 5, 60]], lines: [] },
  black_other: { n: 0, first: [], lines: [] },
  recent: [{ date: '2026.04.12', event: 'TMM Landesliga', vs: 'Kleissl, Helmut', vs_elo: '2238', color: 'w', score: 0.5, opening: '1.e4 c5 2.Nf3 d6' }],
  accounts: [{ site: 'lichess', user: 'patrik', url: 'https://lichess.org/@/patrik', conf: 'sicher' }],
};

describe('PlayerCardComponent', () => {
  let fixture: ComponentFixture<PlayerCardComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  const el = () => fixture.nativeElement as HTMLElement;
  const headings = () => Array.from(el().querySelectorAll('h3')).map(h => h.textContent ?? '');

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['card', 'pgn']);
    api.card.and.resolveTo(CARD);
    TestBed.configureTestingModule({ imports: [PlayerCardComponent], providers: [{ provide: LeagueApiService, useValue: api }] });
    fixture = TestBed.createComponent(PlayerCardComponent);
    fixture.detectChanges();
  });

  afterEach(() => fixture.componentInstance.close());

  it('aus einer Brett-Zeile geöffnet: nur die Farbe an diesem Brett, umschaltbar', async () => {
    await fixture.componentInstance.open('1606921', 'w', 3, null);
    fixture.detectChanges();
    expect(api.card).toHaveBeenCalledWith('1606921', null);
    expect(el().querySelector('.hint')?.textContent).toContain('An Brett 3 spielt Oberschmid mit Weiß');
    expect(headings().some(h => h.startsWith('Mit Weiß'))).toBeTrue();
    expect(headings().some(h => h.startsWith('Mit Schwarz'))).toBeFalse();
    // Namen der Eröffnungen, deutsch notiert
    expect(el().textContent).toContain('1.e4');
    expect(el().textContent).toContain('2.Sf3');

    const both = Array.from(el().querySelectorAll<HTMLButtonElement>('.seg button')).find(b => b.textContent === 'Beide')!;
    both.click();
    fixture.detectChanges();
    expect(headings().some(h => h.startsWith('Mit Weiß'))).toBeTrue();
    expect(headings().some(h => h.startsWith('Mit Schwarz gegen 1.e4'))).toBeTrue();
    expect(el().textContent).toContain('Sizilianisch');
  });

  it('aus der Meldeliste (ohne Brett) zeigt beide Farben, Online-Konto mit Vermerk', async () => {
    await fixture.componentInstance.open('1606921', null, null, 'TOKEN');
    fixture.detectChanges();
    expect(api.card).toHaveBeenCalledWith('1606921', 'TOKEN');
    expect(el().querySelector('.hint')).toBeNull();
    expect(headings().some(h => h.startsWith('Mit Schwarz gegen 1.d4'))).toBeTrue();
    expect(el().querySelector('.acc')?.textContent).toContain('lichess: patrik');
    // Schwarz gegen andere: ohne Partien kein Abschnitt
    expect(headings().some(h => h.includes('andere'))).toBeFalse();
  });

  it('ohne Karte eine klare Meldung', async () => {
    api.card.and.rejectWith(new Error('404'));
    await fixture.componentInstance.open('9', 's', 1, null);
    fixture.detectChanges();
    expect(el().textContent).toContain('Keine Partien gefunden.');
  });
});
