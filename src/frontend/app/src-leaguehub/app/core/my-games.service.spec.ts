import { TestBed } from '@angular/core/testing';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ClubApiService } from './club-api.service';
import { MyGamesService } from './my-games.service';

describe('MyGamesService', () => {
  let club: jasmine.SpyObj<ClubApiService>;
  let handoff: { rookHubUrl: string | null; jumpToRookHub: jasmine.Spy };
  let auth: { isLoggedIn: boolean };

  beforeEach(() => {
    club = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['addToMyGames', 'savedGame']);
    handoff = { rookHubUrl: 'https://rookhub.example', jumpToRookHub: jasmine.createSpy('jump').and.resolveTo() };
    auth = { isLoggedIn: true };
    TestBed.configureTestingModule({ providers: [
      { provide: ClubApiService, useValue: club }, { provide: HandoffService, useValue: handoff }, { provide: AuthService, useValue: auth },
    ] });
  });

  it('nur angemeldet und mit einem RookHub dazu', () => {
    const s = TestBed.inject(MyGamesService);
    expect(s.available).toBeTrue();
    auth.isLoggedIn = false;
    expect(s.available).toBeFalse();
    auth.isLoggedIn = true;
    handoff.rookHubUrl = null;                     // localhost / IP: kein RookHub
    expect(s.available).toBeFalse();
  });

  it('ablegen → Id (auch eine schon vorhandene), Link über den Teilen-Token, Sprung auf die Partie', async () => {
    const s = TestBed.inject(MyGamesService);
    club.addToMyGames.and.resolveTo({ imported: 0, duplicates: 1, ids: [41] });
    expect(await s.save('1. e4 *')).toBe(41);
    club.addToMyGames.and.resolveTo({ imported: 0, duplicates: 0, ids: [] });
    expect(await s.save('kein Zug')).toBeNull();
    club.savedGame.and.resolveTo({ pgn: '', white: null, black: null, shareToken: 'tok123' });
    expect(await s.shareUrl(41)).toBe('https://rookhub.example/g/tok123');
    await s.open(41);
    expect(handoff.jumpToRookHub).toHaveBeenCalledWith('games/41');
  });
});
