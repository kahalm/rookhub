import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DiscordLinkService, DISCORD_LINK_STASH_KEY } from './discord-link.service';

describe('DiscordLinkService', () => {
  let svc: DiscordLinkService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem(DISCORD_LINK_STASH_KEY);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(DiscordLinkService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.removeItem(DISCORD_LINK_STASH_KEY);
  });

  it('POSTs the token on link()', () => {
    svc.link('abc.def').subscribe();
    const req = http.expectOne('/api/profile/discord/link');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ token: 'abc.def' });
    req.flush({});
  });

  it('DELETEs on unlink()', () => {
    svc.unlink().subscribe();
    const req = http.expectOne('/api/profile/discord');
    expect(req.request.method).toBe('DELETE');
    req.flush({});
  });

  it('stash() persists the token', () => {
    svc.stash('tok123');
    expect(localStorage.getItem(DISCORD_LINK_STASH_KEY)).toBe('tok123');
  });

  it('consumeStashed() does nothing without a stashed token', () => {
    svc.consumeStashed();
    http.expectNone('/api/profile/discord/link');
  });

  it('consumeStashed() links and clears the stash on success', () => {
    svc.stash('tok123');
    svc.consumeStashed();
    const req = http.expectOne('/api/profile/discord/link');
    expect(req.request.body).toEqual({ token: 'tok123' });
    req.flush({});
    expect(localStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed() clears the stash on a 409 conflict', () => {
    svc.stash('tok123');
    svc.consumeStashed();
    const req = http.expectOne('/api/profile/discord/link');
    req.flush({ message: 'conflict' }, { status: 409, statusText: 'Conflict' });
    expect(localStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed() keeps the stash on a transient (500) error', () => {
    svc.stash('tok123');
    svc.consumeStashed();
    const req = http.expectOne('/api/profile/discord/link');
    req.flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });
    expect(localStorage.getItem(DISCORD_LINK_STASH_KEY)).toBe('tok123');
  });
});
