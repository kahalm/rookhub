import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@rh/core/auth.service';
import { provideTranslateService } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { LeagueApiService } from '../core/league-api.service';
import { LineupsApiService } from '../core/lineups';
import { Fixture } from '../core/league.models';
import { FixtureViewComponent } from './fixture-view.component';

const OPEN: Fixture = {
  opp: 'Spg Kufstein/Wörgl', home: true, date: 'Sa 03.10.2026', time: '14:00', venue: 'Kursaal, 6323 Bad Häring',
  status: 'open', phase: 'R2+', hit: 5.2,
  boards: [
    { board: 1, opp_color: 's', other: 0.05, cand: [
      { n: 'Polterauer, Chiara', elo: 2112, rb: 1, p: 0.7, fide: '111', g: 12 },
      { n: 'Kleissl, Helmut', elo: 2238, rb: 2, p: 0.2, fide: '222' },
      { n: 'Tabernig, Bernhard', elo: null, rb: 3, p: 0.05, fide: null }] },
    { board: 2, opp_color: 'w', other: 0, cand: [{ n: 'Kleissl, Helmut', elo: 2238, rb: 2, p: 0.6, fide: '222' }] },
  ],
  roster: [
    { rb: 1, n: 'Polterauer, Chiara', elo: 2112, p: 0.7, prev: '7/7', cur: '1/1', fide: '111', g: 12,
      acc: [{ site: 'lichess', user: 'chiara', url: 'https://lichess.org/@/chiara', conf: 'sicher' }] },
  ],
};

describe('FixtureViewComponent', () => {
  let fixture: ComponentFixture<FixtureViewComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let lineupsApi: jasmine.SpyObj<LineupsApiService>;

  function render(e: Fixture | undefined, opts: { tnr?: number | null; token?: string | null } = {}): HTMLElement {
    fixture.componentRef.setInput('leagueName', 'Landesliga');
    fixture.componentRef.setInput('round', 1);
    fixture.componentRef.setInput('team', 'Schwaz');
    fixture.componentRef.setInput('fixture', e);
    fixture.componentRef.setInput('tnr', opts.tnr ?? null);
    fixture.componentRef.setInput('shareToken', opts.token ?? null);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['createShare', 'deleteShare', 'card', 'pgn', 'forecastStats', 'fixtureGames']);
    api.fixtureGames.and.resolveTo([]);
    lineupsApi = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['lineups', 'saveMoves']);
    lineupsApi.lineups.and.resolveTo({ tnr: 1479345, round: 1, date: null, canEdit: false, matches: [] });
    api.forecastStats.and.resolveTo({
      season: '2026/27', total: { fixtures: 4, top1: 10, top2: 18, top3: 22, of: 30, e1: 9000, e2: 16500, e3: 21000, pa: 6300, pb: 1800 },
      rounds: [{ round: 1, fixtures: 3, top1: 6, top2: 13, top3: 15, of: 22, e1: 6600, e2: 12000, e3: 15400 },
        { round: 2, fixtures: 1, top1: 4, top2: 5, top3: 7, of: 8, e1: 2400, e2: 4500, e3: 5600 }],
      leagues: [
        { tnr: 10, name: 'Landesliga', fixtures: 3, top1: 9, top2: 15, top3: 18, of: 24, e1: 7200, e2: 13000, e3: 17000,
          rounds: [{ round: 1, fixtures: 2, top1: 5, top2: 10, top3: 11, of: 16 }, { round: 2, fixtures: 1, top1: 4, top2: 5, top3: 7, of: 8 }] },
        { tnr: 11, name: '1. Klasse Ost', fixtures: 1, top1: 1, top2: 3, top3: 4, of: 6, rounds: [{ round: 1, fixtures: 1, top1: 1, top2: 3, top3: 4, of: 6 }] },
      ],
      calibration: [
        { from: 0, n: 100, p: 5000, hits: 6 }, { from: 10, n: 0, p: 0, hits: 0 }, { from: 50, n: 20, p: 11000, hits: 10 },
        { from: 80, n: 10, p: 8500, hits: 9 },
      ],
    });
    TestBed.configureTestingModule({
      imports: [FixtureViewComponent],
      providers: [{ provide: LeagueApiService, useValue: api }, { provide: LineupsApiService, useValue: lineupsApi }, { provide: AuthService, useValue: { has: () => false } },
        provideTranslateService({ fallbackLang: 'de' }), provideRouter([])],
    });
    fixture = TestBed.createComponent(FixtureViewComponent);
  });

  it('gespielte Runde: Paarungen unter dem Ergebnis, „Partie" nur mit PGN, klappt das Nachspielen auf (0.673.0)', async () => {
    api.fixtureGames.and.resolveTo([
      { board: 1, white: 'Hess, Max', whiteElo: 2040, black: 'Binder, Moriz', blackElo: 2100, result: '½ - ½', forfeit: false,
        pgn: '[White "Hess, Max"]\n[Black "Schwaz"]\n\n1. e4 e5 1/2-1/2', source: 'club', clubGameId: 10, canEdit: true },
      { board: 2, white: 'Gruber, Michael', whiteElo: null, black: 'Ciolek, Andreas', blackElo: 1900, result: '0 - 1', forfeit: false,
        pgn: null, source: null, clubGameId: null },
    ]);
    fixture.componentRef.setInput('leagueTnr', 1479345);
    render({ ...OPEN, status: 'played', score: '3 : 3' });
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(api.fixtureGames).toHaveBeenCalledWith(1479345, 1, 'Schwaz', null);
    const rows = el.querySelectorAll('.pairings tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Hess, Max');
    expect(rows[0].textContent).toContain('½ - ½');
    expect(rows[1].querySelector('.pg button')).toBeNull();
    // 0.679.2: „Analyse" nur als Symbol (ohne RookHub-Adresse in Tests nicht da), alles andere im ⋮
    expect(rows[0].querySelectorAll('.pg .icon-btn').length).toBe(0);
    (rows[0].querySelector('.pg .more-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const items = Array.from(document.querySelectorAll('.mat-mdc-menu-item'));
    expect(items.map(i => i.textContent?.trim())).toEqual(['Nachspielen', 'Bearbeiten (Namen, Ergebnis)', 'Korrigieren (Züge)']);
    expect(items.slice(1).map(a => a.getAttribute('href'))).toEqual(['/verein?bearbeiten=10', '/verein/partie/10/korrigieren']);
    (items[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.pairings lh-game-replay')).not.toBeNull();
  });

  it('gespielte Runde: erste Züge je Brett aus den Aufstellungen, „Züge eingeben" wo erlaubt (2026-10-08)', async () => {
    api.fixtureGames.and.resolveTo([
      { board: 1, white: 'Ackermann, Anna', whiteElo: 2040, black: 'Brunner, Bert', blackElo: 2100, result: '1 - 0', forfeit: false,
        pgn: null, source: null, clubGameId: null },
      { board: 2, white: 'Clauss, Carl', whiteElo: null, black: 'Dorn, Dora', blackElo: 1900, result: '0 - 1', forfeit: false,
        pgn: null, source: null, clubGameId: null },
    ]);
    lineupsApi.lineups.and.resolveTo({ tnr: 1479345, round: 1, date: null, canEdit: true, matches: [
      { matchNo: 4, home: 'Spg Kufstein/Wörgl', away: 'Schwaz', homePts: 3, awayPts: 3, own: true, boards: [
        { board: 1, homePlayer: 'Brunner, Bert', homeTitle: null, homeElo: 2100, awayPlayer: 'Ackermann, Anna', awayTitle: null, awayElo: 2040,
          homeColor: 's', result: '0 - 1', forfeit: 0, moves: 'd4 Nf6 c4', canEditMoves: false },
        { board: 2, homePlayer: 'Clauss, Carl', homeTitle: null, homeElo: null, awayPlayer: 'Dorn, Dora', awayTitle: null, awayElo: 1900,
          homeColor: 'w', result: '0 - 1', forfeit: 0, moves: null, canEditMoves: true },
      ] }] });
    fixture.componentRef.setInput('leagueTnr', 1479345);
    render({ ...OPEN, status: 'played', score: '3 : 3' });
    for (let i = 0; i < 3; i++) { await fixture.whenStable(); fixture.detectChanges(); }
    const el = fixture.nativeElement as HTMLElement;
    expect(lineupsApi.lineups).toHaveBeenCalledWith(1479345, 1);
    const rows = el.querySelectorAll('.pairings .moves-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('1.d4 Sf6 2.c4');
    expect(rows[0].querySelector('.bm-edit')).toBeNull();
    expect(rows[1].querySelector('.bm-edit')?.textContent).toContain('Züge eingeben');
  });

  it('0.724.0: an Brettern MIT Partie keine Zug-Zeile mit „Züge eingeben", alter Eintrag nur „ersetzt"', async () => {
    api.fixtureGames.and.resolveTo([
      { board: 1, white: 'Ackermann, Anna', whiteElo: 2040, black: 'Brunner, Bert', blackElo: 2100, result: '1 - 0', forfeit: false,
        pgn: '[White "Ackermann, Anna"]\n\n1. e4 e5 1-0', source: 'club', clubGameId: 168 },
      { board: 2, white: 'Clauss, Carl', whiteElo: null, black: 'Dorn, Dora', blackElo: 1900, result: '0 - 1', forfeit: false,
        pgn: null, source: null, clubGameId: null },
    ]);
    lineupsApi.lineups.and.resolveTo({ tnr: 1479345, round: 1, date: null, canEdit: true, matches: [
      { matchNo: 4, home: 'Spg Kufstein/Wörgl', away: 'Schwaz', homePts: 3, awayPts: 3, own: true, boards: [
        { board: 1, homePlayer: 'Brunner, Bert', homeTitle: null, homeElo: 2100, awayPlayer: 'Ackermann, Anna', awayTitle: null, awayElo: 2040,
          homeColor: 's', result: '0 - 1', forfeit: 0, moves: 'e4 e5', canEditMoves: false, canDeleteMoves: true,
          game: { source: 'club', clubGameId: 168, plies: 2, result: '1-0', white: 'Ackermann, Anna', black: 'Brunner, Bert',
            firstMoves: ['e4', 'e5'], canEdit: false } },
        { board: 2, homePlayer: 'Clauss, Carl', homeTitle: null, homeElo: null, awayPlayer: 'Dorn, Dora', awayTitle: null, awayElo: 1900,
          homeColor: 'w', result: '0 - 1', forfeit: 0, moves: null, canEditMoves: true },
      ] }] });
    fixture.componentRef.setInput('leagueTnr', 1479345);
    render({ ...OPEN, status: 'played', score: '3 : 3' });
    for (let i = 0; i < 3; i++) { await fixture.whenStable(); fixture.detectChanges(); }
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('.pairings .moves-row');
    expect(rows.length).toBe(2);
    expect(rows[0].classList).toContain('replaced');
    expect(rows[0].querySelector('.bm-edit')).toBeNull();
    expect(rows[0].textContent).toContain('ersetzt durch die Partie');
    expect(rows[1].querySelector('.bm-edit')?.textContent).toContain('Züge eingeben');
  });

  it('über einen Teilen-Link keine Züge (keine Abfrage der Aufstellungen)', async () => {
    fixture.componentRef.setInput('leagueTnr', 1479345);
    render({ ...OPEN, status: 'played', score: '3 : 3' }, { token: 'abc' });
    await fixture.whenStable();
    expect(lineupsApi.lineups).not.toHaveBeenCalled();
  });

  it('offene Runde ohne zugeordnete Partie: keine Paarungen', async () => {
    fixture.componentRef.setInput('leagueTnr', 1479345);
    const el = render(OPEN);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('.pairings')).toBeNull();
  });

  // 0.739.0, Wunsch 2026-10-10: die laufende Runde aus den zugeordneten Vereinspartien
  it('offene Runde mit zugeordneten Partien: vorläufige Aufstellung mit Hinweis, leere Bretter „offen"', async () => {
    fixture.componentRef.setInput('leagueTnr', 1479345);
    api.fixtureGames.and.resolveTo([
      { board: 1, white: 'Kobold, Jonathan', whiteElo: 1935, black: 'Niedermayer, Anton', blackElo: 1987, result: '1 - 0', forfeit: false,
        pgn: '1. e4 1-0', source: 'club', clubGameId: 171, provisional: true },
      { board: 5, white: null, whiteElo: null, black: null, blackElo: null, result: '', forfeit: true, pgn: null, source: null, clubGameId: null },
    ]);
    const el = render(OPEN);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('.provisional-note')?.textContent).toContain('vorläufig');
    const rows = el.querySelectorAll('.pairings tbody tr');
    expect(rows[0].textContent).toContain('Kobold, Jonathan');
    expect(rows[0].textContent).toContain('Niedermayer, Anton');
    expect(el.querySelector('.pairings')!.textContent).toContain('offen');
  });

  it('offene Runde über einen Teilen-Link: keine Abfrage', () => {
    fixture.componentRef.setInput('leagueTnr', 1479345);
    render(OPEN, { token: 'abc' });
    expect(api.fixtureGames).not.toHaveBeenCalled();
  });

  it('zeigt je Brett die Kandidaten, den ersten hervorgehoben, Balken nach Prozent', () => {
    const el = render(OPEN);
    expect(el.querySelector('.match')?.textContent).toContain('Schwaz');
    expect(el.querySelector('.match')?.textContent).toContain('Spg Kufstein/Wörgl');
    const boards = el.querySelectorAll('.board');
    expect(boards.length).toBe(2);
    const first = boards[0].querySelector('.cand.first')!;
    expect(first.textContent).toContain('Polterauer, Chiara');
    expect(first.querySelector('.pct')?.textContent?.trim()).toBe('70 %');
    expect((first.querySelector('.bar i') as HTMLElement).style.width).toBe('70%');
    expect(boards[0].querySelector('.sq.s')).not.toBeNull();
    expect(boards[1].querySelector('.sq.w')).not.toBeNull();
    // ohne FIDE-ID kein Knopf zur Spielerkarte
    expect(boards[0].querySelectorAll('button.pl').length).toBe(2);
    // der Erklärtext steht hinter dem (i) „Prognose" (0.650.0)
    expect(el.querySelector('.note')).toBeNull();
    (el.querySelector('button[aria-label="Wie die Prognose zustande kommt"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.info-panel .note')?.textContent).toContain('5,2 von 2');
    // Partien im Bestand in Klammer hinter dem Namen (0.649.0); ohne Partien nichts
    expect(first.querySelector('.name .g')?.textContent?.trim()).toBe('(12)');
    expect(boards[0].querySelectorAll('.name .g').length).toBe(1);
  });

  it('0.730.0: Spieler ohne FIDE-ID öffnen die Karte über ihren n-Schlüssel (Meldeliste und Kandidat)', async () => {
    api.card.and.resolveTo({ fide: '', key: 'n-0123456789abcd', name: 'Tabernig, Bernhard', n: 0, accounts: [] } as never);
    const el = render({ ...OPEN,
      boards: [{ ...OPEN.boards![0], cand: [{ n: 'Tabernig, Bernhard', elo: null, rb: 3, p: 0.4, fide: null, key: 'n-0123456789abcd' }] }],
      roster: [{ rb: 3, n: 'Tabernig, Bernhard', elo: null, p: 0.4, prev: '', cur: '', fide: null, key: 'n-0123456789abcd', g: 0,
        acc: [{ site: 'chess.com', user: 'tabi', url: 'https://www.chess.com/member/tabi', conf: 'wahrscheinlich' }] }] });
    expect(el.querySelector('.board button.pl')?.textContent?.trim()).toBe('Tabernig, Bernhard');
    const rosterButton = [...el.querySelectorAll('button.pl')].find(b => !b.closest('.board')) as HTMLButtonElement;
    rosterButton.click();
    await fixture.whenStable();
    expect(api.card).toHaveBeenCalledWith('n-0123456789abcd', null);
  });

  it('zwei (i) am Anfang: Partien der Begegnung und Prognose mit Treffern je Runde, Liga, gesamt (0.650.0)', async () => {
    fixture.componentRef.setInput('sources', {
      board: [{ key: 'Lumbra', label: 'Lumbra', games: 900 }], boardTotal: 900, online: [], onlineTotal: 300, countedAt: '',
      opponent: { players: 8, board: { Lumbra: 120 }, boardTotal: 120, online: { lichess: { games: 1500, accounts: 2 } }, onlineTotal: 1500, onlineAccounts: 2 },
    });
    let el = render(OPEN);
    await fixture.whenStable();
    fixture.detectChanges();
    const lines = Array.from(el.querySelectorAll('.info-line')).map(x => x.textContent!.replace(/\s+/g, ' ').trim());
    expect(lines[0]).toContain('Partien Spg Kufstein/Wörgl: 1.620');
    expect(lines[1]).toBe('Prognose (Top 3: 73 %)i');                                    // 0.659.4: Top 3 statt „% korrekt"
    expect(el.querySelector('lh-game-sources')).toBeNull();                                  // Tabelle erst hinter dem (i)
    (el.querySelector('button[aria-label="Partien je Quelle"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.info-panel lh-game-sources')).not.toBeNull();
    (el.querySelector('button[aria-label="Wie die Prognose zustande kommt"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('lh-game-sources')).toBeNull();                                  // immer nur ein (i) offen
    const rows = Array.from(el.querySelectorAll('.stats-tbl:not(.cal-tbl) tbody tr'))
      .map(r => Array.from(r.children).map(c => c.textContent!.replace(/\s+/g, ' ').trim()).join(' '));
    expect(rows).toContain('Runde 1 3 27 %erw. 30 % 59 %erw. 55 % 68 %erw. 70 %');
    expect(rows.find(r => r.startsWith('Landesliga'))).toBe('LandesligaR1 31 % · R2 50 % 3 38 %erw. 30 % 63 %erw. 54 % 75 %erw. 71 %');
    expect(rows[rows.length - 1]).toBe('Gesamt 4 33 %erw. 30 % 60 %erw. 55 % 73 %erw. 70 %');
    // Kalibrierung: angesagt gegen eingetroffen je Stufe, leere Stufen fallen weg, mittlere Abweichung nach Fällen gewichtet
    const cal = Array.from(el.querySelectorAll('.cal-tbl tbody tr'))
      .map(r => Array.from(r.children).map(c => c.textContent!.trim()).join(' '));
    expect(cal).toEqual(['0–10 % 100 5 % 6 %', '50–60 % 20 55 % 50 %', '80–90 % 10 85 % 90 %']);
    // nach angesagter Wahrscheinlichkeit gewichtet: (1·5000 + 5·11000 + 5·8500) / 24500 = 4,2 Prozentpunkte
    expect(el.querySelector('.cal-h')?.textContent).toContain('Im Schnitt liegen angesagt und eingetroffen 4,2 Prozentpunkte');
    expect(el.querySelector('.cal-h')?.textContent).toContain('gab die Prognose im Schnitt 21 %; Raten über die Meldeliste gäbe 6 %');
    expect(el.querySelector('.stats-tbl tr.mine')?.textContent).toContain('Landesliga');   // eigene Liga hervorgehoben
    expect(api.forecastStats).toHaveBeenCalledOnceWith(null);
    // gespielt: diese Begegnung mit ihren Treffern
    el = render({ ...OPEN, status: 'played', eval: { top1: 3, top2: 5, top3: 6, of: 8 } });
    fixture.componentInstance.openInfo.set('forecast');                                   // (i) ist schon offen bzw. bleibt offen
    fixture.detectChanges();
    expect(el.querySelector('.info-panel')!.textContent).toContain('an 3 von 8 Brettern genau der erste Vorschlag');
  });

  it('über den Teilen-Link holt die Statistik über den Link', async () => {
    render(OPEN, { token: 'TOK' });
    await fixture.whenStable();
    expect(api.forecastStats).toHaveBeenCalledWith('TOK');
  });

  it('spätere Runde: vorläufige Prognose mit Hinweis und Brettern (2026-10-06)', () => {
    const el = render({ ...OPEN, provisional: true, unlock_after: 3 });
    expect(el.querySelector('.note.provisional')?.textContent).toContain('sobald Runde 3 gespielt ist');
    expect(el.querySelectorAll('.board').length).toBe(2);
  });

  it('gesperrte Runde nennt, wann die Prognose kommt, und zeigt keine Bretter', () => {
    const el = render({ opp: 'Wörgl', home: false, status: 'locked', unlock_after: 3 });
    expect(el.querySelector('.note')?.textContent).toContain('sobald Runde 3 gespielt ist');
    expect(el.querySelector('.boards')).toBeNull();
    expect(el.textContent).not.toContain('WhatsApp');
  });

  it('spielfrei und fehlende Daten', () => {
    expect(render({ bye: true }).textContent).toContain('Schwaz ist spielfrei.');
    expect(render(undefined).textContent).toContain('keine Daten');
  });

  it('„Link teilen" nur im Admin-Bereich, nicht auf einem geteilten Link', () => {
    expect(render(OPEN, { tnr: 42 }).textContent).toContain('Link teilen');
    expect(render(OPEN, { tnr: null }).textContent).not.toContain('Link teilen');
    expect(render(OPEN, { tnr: 42, token: 'abc' }).textContent).not.toContain('Link teilen');
  });

  it('WhatsApp-Text: am PC in die Zwischenablage und darunter zum Markieren', async () => {
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);
    const write = jasmine.createSpy('writeText').and.resolveTo();
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as unknown as Clipboard);
    render(OPEN);
    await fixture.componentInstance.shareWhatsApp();
    fixture.detectChanges();
    const text = write.calls.mostRecent().args[0] as string;
    expect(text).toContain('*Landesliga R1');
    expect(text).toContain('Polterauer');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.share-text')?.textContent).toBe(text);
    expect(el.querySelector('.share-out a')?.getAttribute('href')).toContain('https://wa.me/?text=');
  });

  it('Teilen-Link: legt ihn an, zeigt Adresse und Ablauf, Widerruf ersetzt die Anzeige', async () => {
    api.createShare.and.resolveTo({ token: 'TOKEN123', expires: '2026-10-10' });
    api.deleteShare.and.resolveTo({});
    render(OPEN, { tnr: 42 });
    await fixture.componentInstance.shareLink();
    fixture.detectChanges();
    expect(api.createShare).toHaveBeenCalledWith(42, 1, 'Schwaz');
    const el = fixture.nativeElement as HTMLElement;
    expect((el.querySelector('.linkrow input') as HTMLInputElement).value).toBe(`${location.origin}/s/TOKEN123`);
    expect(el.textContent).toContain('läuft am 10.10.2026 ab');
    await fixture.componentInstance.revoke('TOKEN123');
    fixture.detectChanges();
    expect(api.deleteShare).toHaveBeenCalledWith('TOKEN123');
    expect(el.textContent).toContain('Link widerrufen');
    expect(el.querySelector('.linkrow')).toBeNull();
  });

  it('Wechsel auf eine andere Begegnung räumt den Teilen-Kasten weg', async () => {
    api.createShare.and.resolveTo({ token: 'TOKEN123', expires: '2026-10-10' });
    render(OPEN, { tnr: 42 });
    await fixture.componentInstance.shareLink();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.linkrow')).not.toBeNull();
    fixture.componentRef.setInput('team', 'Absam');     // der Link gehört zu Schwaz, nicht zu Absam
    fixture.detectChanges();
    expect(el.querySelector('.share-out')).toBeNull();
  });

  it('Teilen-Link: Absage des Servers steht als Fehler da', async () => {
    api.createShare.and.rejectWith({ error: { message: 'Runde ist gesperrt' } });
    render(OPEN, { tnr: 42 });
    await fixture.componentInstance.shareLink();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.share-out .err')?.textContent).toContain('Runde ist gesperrt');
  });
});
