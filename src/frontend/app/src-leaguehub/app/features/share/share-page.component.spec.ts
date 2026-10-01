import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { LeagueApiService } from '../../core/league-api.service';
import { SharePageComponent } from './share-page.component';

describe('SharePageComponent', () => {
  let api: jasmine.SpyObj<LeagueApiService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [SharePageComponent],
      providers: [
        provideRouter([]),
        { provide: LeagueApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'TOKEN123' }) } } },
      ],
    });
    const f = TestBed.createComponent(SharePageComponent);
    f.detectChanges();
    return f;
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['shared', 'card', 'pgn', 'createShare', 'deleteShare', 'sources']);
    api.sources.and.resolveTo({
      board: [{ key: 'Lumbra', label: 'Lumbra', games: 34838 }], boardTotal: 34838,
      online: [{ key: 'chess.com', label: 'chess.com', games: 29522 }], onlineTotal: 29522, countedAt: '2026-10-01T14:30:00Z',
      league: { players: 226, board: { Lumbra: 12106 }, boardTotal: 12106, online: { 'chess.com': { games: 13132, accounts: 25 } }, onlineTotal: 13132, onlineAccounts: 25 },
      opponent: { players: 11, board: { Lumbra: 187 }, boardTotal: 187, online: { 'chess.com': { games: 12, accounts: 1 } }, onlineTotal: 12, onlineAccounts: 1 },
    });
  });

  it('zeigt die geteilte Begegnung ohne „Link teilen"', async () => {
    api.shared.and.resolveTo({
      league: 'Landesliga', season: '2026/27', round: 2, team: 'Schwaz/', generated: '27.09.2026 21:00', expires: '2026-10-11',
      fixture: { opp: 'Wörgl', home: true, status: 'open', boards: [], roster: [] },
    });
    const f = create();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(api.shared).toHaveBeenCalledWith('TOKEN123');
    expect(el.textContent).toContain('Link gültig bis 11.10.2026');
    expect(el.querySelector('.match')?.textContent).toContain('Wörgl');
    expect(el.textContent).toContain('Auf WhatsApp teilen');
    expect(el.textContent).not.toContain('Link teilen');
    // Ganz oben die Aufforderung, Partien hochzuladen — ohne Anmeldung, beide Wege.
    const cta = el.querySelector('section.cta')!;
    expect(el.firstElementChild).toBe(cta);
    expect(cta.textContent).toContain('Lade deine Partien hoch');
    const links = Array.from(cta.querySelectorAll('a')).map(a => a.getAttribute('href'));
    expect(links).toEqual(['/s/TOKEN123/hochladen?art=formular', '/s/TOKEN123/hochladen']);
    expect(TestBed.inject(Title).getTitle()).toBe('Schwaz – Runde 2 | LeagueHub');
    // Partien je Quelle wie auf der Startseite — über den Link (0.627.0).
    await f.whenStable();
    f.detectChanges();
    expect(api.sources).toHaveBeenCalledWith('TOKEN123');
    // Liga und Gegner bestimmt über den Link der Server — die Seite schickt nichts mit.
    const rows = Array.from(el.querySelectorAll('.src-tbl tbody tr')).map(r => Array.from(r.children).map(c => c.textContent!.trim()));
    expect(rows).toEqual([['Brett', '34.838', '12.106', '187'], ['Lumbra', '34.838', '12.106', '187'],
      ['Online', '29.522', '13.132', '12'], ['chess.com', '29.522', '13.132', '12']]);
    const head = el.querySelector('.src-tbl thead')!.textContent!;
    expect(head).toContain('Landesliga · 226 Spieler');
    expect(head).toContain('Wörgl · 11 Spieler');
  });

  it('abgelaufen oder widerrufen: „Link ungültig"', async () => {
    api.shared.and.rejectWith(new Error('404'));
    const f = create();
    await f.whenStable();
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Link ungültig');
    expect(api.sources).not.toHaveBeenCalled();
  });
});
