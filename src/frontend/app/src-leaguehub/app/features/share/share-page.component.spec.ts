import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { LeagueApiService } from '../../core/league-api.service';
import { SharePageComponent } from './share-page.component';

describe('SharePageComponent', () => {
  let api: jasmine.SpyObj<LeagueApiService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [SharePageComponent],
      providers: [
        { provide: LeagueApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'TOKEN123' }) } } },
      ],
    });
    const f = TestBed.createComponent(SharePageComponent);
    f.detectChanges();
    return f;
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['shared', 'card', 'pgn', 'createShare', 'deleteShare']);
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
    expect(TestBed.inject(Title).getTitle()).toBe('Schwaz – Runde 2 | LeagueHub');
  });

  it('abgelaufen oder widerrufen: „Link ungültig"', async () => {
    api.shared.and.rejectWith(new Error('404'));
    const f = create();
    await f.whenStable();
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Link ungültig');
  });
});
