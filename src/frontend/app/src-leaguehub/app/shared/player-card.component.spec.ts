import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '../core/league-api.service';
import { MyGamesService } from '../core/my-games.service';
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
  let myGames: jasmine.SpyObj<MyGamesService> & { available: boolean; rookHubUrl: string | null };
  const el = () => fixture.nativeElement as HTMLElement;
  const headings = () => Array.from(el().querySelectorAll('h3')).map(h => h.textContent ?? '');

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['card', 'pgn', 'recent']);
    api.card.and.resolveTo(CARD);
    myGames = Object.assign(jasmine.createSpyObj<MyGamesService>('MyGamesService', ['save', 'shareUrl', 'open']),
      { available: false, rookHubUrl: null as string | null });
    TestBed.configureTestingModule({ imports: [PlayerCardComponent],
      providers: [provideTranslateService({ fallbackLang: 'de' }), { provide: LeagueApiService, useValue: api },
        { provide: MyGamesService, useValue: myGames }] });
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

  it('eine der letzten Partien anklicken spielt sie nach; zurück zur Karte', async () => {
    api.recent.and.resolveTo({ fide: '1606921', games: [{ date: '2026.04.12', vs: 'Kleissl, Helmut', color: 'w',
      pgn: '[White "Oberschmid, Patrik"]\n[Black "Kleissl, Helmut"]\n[Result "1/2-1/2"]\n\n1. e4 c5 2. Nf3 d6 1/2-1/2\n' }] });
    await fixture.componentInstance.open('1606921', null, null, 'TOKEN');
    fixture.detectChanges();
    (el().querySelector('table.recent tr') as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.recent).toHaveBeenCalledWith('1606921', 'TOKEN');
    expect(el().querySelector('.replay-head')?.textContent).toContain('Oberschmid, Patrik – Kleissl, Helmut');
    expect(el().querySelector('.replay-moves')?.textContent).toContain('Sf3');
    expect(el().querySelector('table.recent')).toBeNull();                        // statt der Karte
    (Array.from(el().querySelectorAll('button')).find(b => b.textContent?.includes('Zurück zur Karte')) as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('table.recent')).not.toBeNull();
    (el().querySelector('table.recent tr') as HTMLElement).click();              // zweites Mal: ohne neuen Abruf
    await fixture.whenStable();
    expect(api.recent).toHaveBeenCalledTimes(1);
    // Ohne Anmeldung (Teilen-Link) gibt es nichts abzulegen.
    fixture.detectChanges();
    expect(buttonText('Zu meinen Partien')).toBeUndefined();
    expect(buttonText('Partie teilen')).toBeUndefined();
  });

  const PGN = '[White "Oberschmid, Patrik"]\n[Black "Kleissl, Helmut"]\n[Result "1/2-1/2"]\n\n1. e4 c5 2. Nf3 d6 1/2-1/2\n';
  const buttonText = (t: string) => Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.includes(t));

  async function replayOpen(): Promise<void> {
    myGames.available = true;
    myGames.rookHubUrl = 'https://rookhub.example';
    api.recent.and.resolveTo({ fide: '1606921', games: [{ date: '2026.04.12', vs: 'Kleissl, Helmut', color: 'w', pgn: PGN }] });
    await fixture.componentInstance.open('1606921', null, null, null);
    fixture.detectChanges();
    (el().querySelector('table.recent tr') as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('„Zu meinen Partien" legt die Partie in RookHub ab und springt dorthin (Wunsch 2026-09-28)', async () => {
    await replayOpen();
    myGames.save.and.resolveTo(41);
    myGames.open.and.resolveTo();
    buttonText('Zu meinen Partien')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(myGames.save).toHaveBeenCalledWith(PGN);
    expect(myGames.open).toHaveBeenCalledWith(41);
    expect(el().querySelector('.game-note')?.textContent).toContain('Liegt in deinen Partien');
  });

  it('„Partie teilen" legt einmal ab und kopiert den öffentlichen RookHub-Link', async () => {
    await replayOpen();
    myGames.save.and.resolveTo(41);
    myGames.shareUrl.and.resolveTo('https://rookhub.example/g/tok123');
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);   // PC: Zwischenablage
    const clip = spyOn(navigator.clipboard, 'writeText').and.resolveTo();
    buttonText('Partie teilen')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(clip).toHaveBeenCalledWith('https://rookhub.example/g/tok123');
    expect(el().querySelector('.game-note')?.textContent).toContain('Link kopiert: https://rookhub.example/g/tok123');
    // Zweites Teilen: schon abgelegt, kein zweiter Import.
    buttonText('Partie teilen')!.click();
    await fixture.whenStable();
    expect(myGames.save).toHaveBeenCalledTimes(1);
    expect(myGames.shareUrl).toHaveBeenCalledTimes(2);
  });

  it('eine Partie, die RookHub nicht übernimmt, sagt es — kein Sprung', async () => {
    await replayOpen();
    myGames.save.and.resolveTo(null);
    buttonText('Zu meinen Partien')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(myGames.open).not.toHaveBeenCalled();
    expect(el().querySelector('.game-note.err')?.textContent).toContain('lässt sich nicht übernehmen');
  });

  it('eine Partie, die es nicht mehr gibt, sagt es', async () => {
    api.recent.and.resolveTo({ fide: '1606921', games: [] });
    await fixture.componentInstance.open('1606921', null, null, null);
    fixture.detectChanges();
    (el().querySelector('table.recent tr') as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().querySelector('.err')?.textContent).toContain('nicht mehr da');
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
