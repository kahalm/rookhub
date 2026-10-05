import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../core/league-api.service';
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
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['createShare', 'deleteShare', 'card', 'pgn', 'forecastStats']);
    api.forecastStats.and.resolveTo({
      season: '2026/27', total: { fixtures: 4, top1: 10, top2: 18, top3: 22, of: 30, e1: 9000, e2: 16500, e3: 21000 },
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
      providers: [{ provide: LeagueApiService, useValue: api }, { provide: AuthService, useValue: { has: () => false } }],
    });
    fixture = TestBed.createComponent(FixtureViewComponent);
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
    expect(lines[1]).toBe('Prognosei');                                                    // nur „Prognose" + (i) (0.658.2)
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
    expect(el.querySelector('.cal-h')?.textContent).toContain('im Schnitt 1,9 Prozentpunkte daneben');
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
