import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@rh/core/auth.service';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '@lh/core/league-api.service';
import { MyGamesService } from '@lh/core/my-games.service';
import { PlayerCard, ProfileView } from '@lh/core/league.models';
import { TREE_FILTER_KEY } from './tree-filter';
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

  let perms: Set<string>;

  beforeEach(() => {
    perms = new Set();
    localStorage.removeItem(TREE_FILTER_KEY);
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['card', 'pgn', 'recent', 'tree', 'profile', 'playerSuggestions']);
    api.playerSuggestions.and.resolveTo({ items: [] });
    api.card.and.resolveTo(CARD);
    myGames = Object.assign(jasmine.createSpyObj<MyGamesService>('MyGamesService', ['save', 'shareUrl', 'open']),
      { available: false, rookHubUrl: null as string | null });
    TestBed.configureTestingModule({ imports: [PlayerCardComponent],
      providers: [provideTranslateService({ fallbackLang: 'de' }), { provide: LeagueApiService, useValue: api },
        { provide: MyGamesService, useValue: myGames }, { provide: AuthService, useValue: { has: (p: string) => perms.has(p) } }] });
    fixture = TestBed.createComponent(PlayerCardComponent);
    fixture.detectChanges();
  });

  afterEach(() => { fixture.componentInstance.close(); localStorage.removeItem(TREE_FILTER_KEY); });

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

  it('oben nach Farbe gefiltert: die letzten Partien NUR dieser Farbe (vom Server), direkt nachspielbar (0.592.0)', async () => {
    const W = (vs: string) => ({ date: '2025.??.??', vs, color: 'w' as const, event: 'TMM', vs_elo: '', score: 1, opening: '1.d4 d5',
      pgn: `[White "Oberschmid, Patrik"]\n[Black "${vs}"]\n[Result "1-0"]\n\n1. d4 d5 1-0\n` });
    api.recent.and.resolveTo({ fide: '1606921', games: [W('Schwaz'), W('Binder, Moriz')] });
    await fixture.componentInstance.open('1606921', 'w', 3, null);
    fixture.detectChanges();                                                     // Effekt: Farbe gewählt → holen
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.recent).toHaveBeenCalledWith('1606921', null, 'w');
    const rows = () => Array.from(el().querySelectorAll('table.recent tr')).map(r => r.textContent ?? '');
    expect(rows().length).toBe(2);
    expect(rows()[1]).toContain('Binder, Moriz');
    expect(headings().some(h => h.startsWith('Letzte Partien mit Weiß'))).toBeTrue();

    (el().querySelectorAll('table.recent tr')[1] as HTMLElement).click();       // PGN ist schon da — kein zweiter Abruf
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.recent).toHaveBeenCalledTimes(1);
    expect(el().querySelector('.replay-head')?.textContent).toContain('Oberschmid, Patrik – Binder, Moriz');

    (Array.from(el().querySelectorAll('button')).find(b => b.textContent?.includes('Zurück zur Karte')) as HTMLButtonElement).click();
    fixture.detectChanges();
    (Array.from(el().querySelectorAll<HTMLButtonElement>('.seg button')).find(b => b.textContent === 'Beide')!).click();
    fixture.detectChanges();
    expect(rows().length).toBe(1);                                               // die gemischte Liste der Karte
    expect(rows()[0]).toContain('Kleissl, Helmut');
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
    expect(el().querySelector('.acc')?.textContent).toContain('Lichess: patrik');
    expect(el().querySelector('.acc .tag-sure')?.textContent).toContain('gesichert');
    expect(el().querySelector('.acc-add')).not.toBeNull();                          // über den Link: nur hinzufügen (0.630.0)
    expect(Array.from(el().querySelectorAll('lh-online-accounts button')).map(x => x.textContent!.trim())).not.toContain('Entfernen');
    // Schwarz gegen andere: ohne Partien kein Abschnitt
    expect(headings().some(h => h.includes('andere'))).toBeFalse();
  });

  it('Verwalter pflegen die Online-Konten; über einen Teilen-Link nur hinzufügen, ohne Bearbeiten (0.605.0 / 0.630.0)', async () => {
    perms.add('league.manage');
    await fixture.componentInstance.open('1606921', null, null, null);
    fixture.detectChanges();
    expect(fixture.componentInstance.canEdit()).toBeTrue();
    expect(el().querySelector('.acc-add')).not.toBeNull();
    // Wunsch 2026-10-01: „Hinzufügen von Online-Accounts soll auch für nicht registrierte User möglich sein".
    await fixture.componentInstance.open('1606921', null, null, 'TOKEN');
    fixture.detectChanges();
    expect(fixture.componentInstance.canEdit()).toBeFalse();
    expect(el().querySelector('.acc-add')).not.toBeNull();
    expect(Array.from(el().querySelectorAll('lh-online-accounts button')).map(b => b.textContent!.trim()))
      .not.toContain('Bearbeiten');
  });

  it('ohne Konto und ohne Recht kein Abschnitt Online-Konten', async () => {
    api.card.and.resolveTo({ ...CARD, accounts: [] });
    await fixture.componentInstance.open('1606921', null, null, null);
    fixture.detectChanges();
    expect(headings().some(h => h.startsWith('Online-Konten'))).toBeFalse();
  });

  it('nur Online-Partien: Eröffnungsbaum ja, Brett-Statistik nein; nach einer Konto-Änderung frisch geladen', async () => {
    perms.add('league.manage');
    const onlineOnly: PlayerCard = { ...CARD, n: 0, online: 42, recent: [], accounts: [{ ...CARD.accounts[0], id: 7, games: 42 }] };
    api.card.and.resolveTo(onlineOnly);
    await fixture.componentInstance.open('1606921', null, null, null);
    fixture.detectChanges();
    expect(el().querySelector('.tree-toggle')).not.toBeNull();
    expect(Array.from(el().querySelectorAll('button')).some(b => b.textContent?.includes('PGN herunterladen'))).toBeFalse();
    expect(headings().some(h => h.startsWith('Mit Weiß'))).toBeFalse();
    api.card.and.resolveTo({ ...onlineOnly, online: 50 });
    await fixture.componentInstance.reloadCard();
    expect(fixture.componentInstance.card()?.online).toBe(50);
    expect(api.card).toHaveBeenCalledTimes(2);
  });

  it('ohne Karte eine klare Meldung', async () => {
    api.card.and.rejectWith(new Error('404'));
    await fixture.componentInstance.open('9', 's', 1, null);
    fixture.detectChanges();
    expect(el().textContent).toContain('Keine Partien gefunden.');
  });

  it('PGN-Download, den der Browser nicht annimmt, sagt es — nicht stumm nichts (F8-006)', async () => {
    api.pgn.and.resolveTo(new Blob(['1. e4 *'], { type: 'application/x-chess-pgn' }));
    spyOn(URL, 'createObjectURL').and.throwError('gesperrt');
    await fixture.componentInstance.download(CARD);
    fixture.detectChanges();
    expect(api.pgn).toHaveBeenCalledWith('1606921', null);
    expect(fixture.componentInstance.error()).toBe('Die PGN-Datei konnte nicht geladen werden.');
  });

  it('Filter auf der Karte: das Eröffnungsprofil kommt gefiltert vom Server, ohne Filter die gespeicherte Karte (0.617.0)', async () => {
    const view: ProfileView = { fide: '1606921', n: 31, board: 20, online: 11, years: ['2019', '2026'],
      white: { n: 14, first: [['d4', 10, 55], ['e4', 4, 50]], lines: [] },
      black_e4: { n: 9, first: [['c5', 9, 44]], lines: [] }, black_d4: { n: 8, first: [], lines: [] }, black_other: { n: 0, first: [], lines: [] } };
    api.profile.and.resolveTo(view);
    api.card.and.resolveTo({ ...CARD, online: 11, onlineUnsure: 3 });
    await fixture.componentInstance.open('1606921', null, null, null);
    fixture.detectChanges();
    expect(api.profile).not.toHaveBeenCalled();                                     // Vorgabe: gespeicherte Karte
    expect(headings().find(h => h.startsWith('Mit Weiß'))).toContain('(9 Partien)');
    (Array.from(el().querySelectorAll<HTMLButtonElement>('.tree-filter .seg button')).find(b => b.textContent?.trim() === 'Brett + online')!).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.profile).toHaveBeenCalledWith('1606921', null, { source: 'both', speeds: [], years: null, withUnsure: false });
    expect(headings().find(h => h.startsWith('Mit Weiß'))).toContain('(14 Partien)');
    expect(el().querySelector('.filtered')?.textContent).toContain('Gefiltert: 31 Partien (20 am Brett, 11 online)');
    expect(JSON.parse(localStorage.getItem(TREE_FILTER_KEY)!).source).toBe('both');   // gemerkt, gilt auch für den Baum
    (Array.from(el().querySelectorAll<HTMLButtonElement>('.tree-filter .seg button')).find(b => b.textContent?.trim() === 'Brett')!).click();
    fixture.detectChanges();
    expect(headings().find(h => h.startsWith('Mit Weiß'))).toContain('(9 Partien)');   // zurück: ohne neuen Abruf
    expect(api.profile).toHaveBeenCalledTimes(1);
  });
});
