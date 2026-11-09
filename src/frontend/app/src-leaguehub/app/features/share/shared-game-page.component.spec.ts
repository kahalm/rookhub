import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../../core/league-api.service';
import { SharedLeagueGame } from '../../core/league.models';
import { SharedGamePageComponent } from './shared-game-page.component';

// Erfundene Spieler.
const GAME: SharedLeagueGame = { league: 'Landesliga', season: '2026/27', round: 3, board: 2, home: 'Testdorf', away: 'Bergheim',
  white: 'Ackermann, Anna', whiteElo: 1800, black: 'Brunner, Bert', blackElo: null, result: '1 - 0', pgn: '1.e4 e5 *', expires: '2027-02-09' };

describe('SharedGamePageComponent', () => {
  let api: jasmine.SpyObj<LeagueApiService>;

  async function create() {
    TestBed.configureTestingModule({ imports: [SharedGamePageComponent], providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'de' }),
      { provide: LeagueApiService, useValue: api },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'T' }) } } }] });
    const f = TestBed.createComponent(SharedGamePageComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  beforeEach(() => { api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['sharedGame']); });

  it('zeigt die eine Partie samt Nachspielen, nur lesend', async () => {
    api.sharedGame.and.resolveTo(GAME);
    const el = await create();
    expect(api.sharedGame).toHaveBeenCalledOnceWith('T');
    expect(el.textContent).toContain('Ackermann, Anna');
    expect(el.textContent).toContain('Runde 3 · Brett 2');
    expect(el.textContent).toContain('gültig bis 09.02.2027');
    expect(el.querySelector('lh-game-replay')).not.toBeNull();
  });

  it('anonymisierte Partie: ohne Brett und Mannschaften', async () => {
    api.sharedGame.and.resolveTo({ ...GAME, board: null, home: null, away: null, white: 'Testdorf' });
    const el = await create();
    expect(el.textContent).not.toContain('Brett');
    expect(el.textContent).not.toContain('Bergheim');
  });

  it('404 = „Link ungültig", anderer Fehler bietet „Neu laden"', async () => {
    api.sharedGame.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    expect((await create()).textContent).toContain('Link ungültig');
    TestBed.resetTestingModule();
    api.sharedGame.and.rejectWith(new HttpErrorResponse({ status: 429 }));
    const el = await create();
    expect(el.textContent).not.toContain('Link ungültig');
    expect(el.textContent).toContain('Neu laden');
  });
});
