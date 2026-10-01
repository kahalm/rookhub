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
      { n: 'Polterauer, Chiara', elo: 2112, rb: 1, p: 0.7, fide: '111' },
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
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['createShare', 'deleteShare', 'card', 'pgn']);
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
    expect(el.querySelector('.note')?.textContent).toContain('5,2 von 2');
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
