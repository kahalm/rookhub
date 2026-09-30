import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { DiscordLinkService, DISCORD_LINK_STASH_KEY, DiscordLinkOutcome, discordIdentityFromToken } from './discord-link.service';
import { ConfirmService } from '../shared/confirm-dialog/confirm-dialog.component';
import { SnackbarService } from './snackbar.service';

/** Token wie vom Bot: base64url(JSON {id,u,exp}) + "." + Signatur (die prüft nur der Server). */
function botToken(payload: Record<string, unknown>): string {
  const bytes = new TextEncoder().encode(JSON.stringify(payload));
  const body = btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${body}.c2lnbmF0dXJl`;
}

describe('DiscordLinkService', () => {
  const TOKEN = botToken({ id: '4711', u: 'fremder_jürgen', exp: 9999999999 });
  let svc: DiscordLinkService;
  let http: HttpTestingController;
  let ask: jasmine.Spy;
  let snackbar: { info: jasmine.Spy };

  /** Beantwortet das GET /api/profile, das confirmAndLink vor der Rückfrage stellt (bestehende Verknüpfung). */
  function profile(discordId: string | null = null, discordUsername: string | null = null): void {
    const req = http.expectOne(r => r.method === 'GET' && r.url === '/api/profile');
    req.flush({ username: 'ich', discordId, discordUsername });
  }

  beforeEach(() => {
    sessionStorage.removeItem(DISCORD_LINK_STASH_KEY);
    localStorage.removeItem(DISCORD_LINK_STASH_KEY);
    ask = jasmine.createSpy('ask').and.returnValue(of(true));
    snackbar = { info: jasmine.createSpy('info') };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { ask } },
        { provide: SnackbarService, useValue: snackbar },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    svc = TestBed.inject(DiscordLinkService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.removeItem(DISCORD_LINK_STASH_KEY);
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

  describe('discordIdentityFromToken', () => {
    it('liest ID und Namen (UTF-8) aus dem Payload', () => {
      expect(discordIdentityFromToken(TOKEN)).toEqual({ id: '4711', name: 'fremder_jürgen' });
    });

    it('ohne Namen bleibt die ID; unlesbar ist null', () => {
      expect(discordIdentityFromToken(botToken({ id: '4711', u: '', exp: 1 }))).toEqual({ id: '4711', name: null });
      expect(discordIdentityFromToken('kein-token')).toBeNull();
      expect(discordIdentityFromToken('%%%.sig')).toBeNull();
      expect(discordIdentityFromToken(botToken({ u: 'ohne-id' }))).toBeNull();
    });
  });

  // F1-002: ein fremder ?dl=-Link darf das Konto nicht still mit dem Discord-Konto des Absenders verknüpfen.
  describe('confirmAndLink (Rückfrage vor dem Verknüpfen)', () => {
    it('fragt mit dem Discord-Namen aus dem Token und schickt ohne „OK" nichts', () => {
      ask.and.returnValue(of(false));
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      expect(ask).not.toHaveBeenCalled();           // erst die bestehende Verknüpfung lesen
      profile();
      expect(ask).toHaveBeenCalledWith('profile.discord.confirmLink', { discord: 'fremder_jürgen' });
      http.expectNone('/api/profile/discord/link');
      expect(outcome).toBe('declined');
    });

    it('nach „OK": POST und Erfolgsmeldung', () => {
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      profile();
      const req = http.expectOne('/api/profile/discord/link');
      expect(req.request.body).toEqual({ token: TOKEN });
      req.flush({});
      expect(outcome).toBe('linked');
      expect(snackbar.info).toHaveBeenCalledWith('profile.discord.linked');
    });

    it('409 meldet den Konflikt und gilt als endgültig', () => {
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      profile();
      http.expectOne('/api/profile/discord/link').flush({ message: 'conflict' }, { status: 409, statusText: 'Conflict' });
      expect(outcome).toBe('rejected');
      expect(snackbar.info).toHaveBeenCalledWith('profile.discord.linkConflict', { duration: 4000 });
    });

    it('ohne Namen im Token zeigt die Rückfrage die Discord-ID', () => {
      svc.confirmAndLink(botToken({ id: '4711', exp: 9999999999 })).subscribe();
      profile();
      expect(ask).toHaveBeenCalledWith('profile.discord.confirmLink', { discord: '4711' });
      http.expectOne('/api/profile/discord/link').flush({});
    });

    it('ein unlesbarer Token kommt weder in die Rückfrage noch an den Server', () => {
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink('kein-token').subscribe(o => outcome = o);
      expect(ask).not.toHaveBeenCalled();
      http.expectNone('/api/profile');
      http.expectNone('/api/profile/discord/link');
      expect(outcome).toBe('rejected');
      expect(snackbar.info).toHaveBeenCalledWith('profile.discord.linkFailed', { duration: 4000 });
    });

    // Der Bot hängt ?dl= an jeden hideBoard-Rätsellink: ein schon verknüpftes Konto darf nicht bei jedem
    // Rätselklick den Warn-Dialog sehen (gewöhnt ans blinde Bestätigen).
    it('dieselbe Discord-ID schon verknüpft → ask nicht aufgerufen, kein POST, keine Meldung', () => {
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      profile('4711', 'fremder_jürgen');
      expect(ask).not.toHaveBeenCalled();
      http.expectNone('/api/profile/discord/link');
      expect(outcome).toBe('linked');
      expect(snackbar.info).not.toHaveBeenCalled();
    });

    it('dieselbe Discord-ID mit geändertem Namen → still nachziehen (POST ohne Rückfrage und ohne Meldung)', () => {
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      profile('4711', 'alter_name');
      expect(ask).not.toHaveBeenCalled();
      const req = http.expectOne('/api/profile/discord/link');
      expect(req.request.body).toEqual({ token: TOKEN });
      req.flush({}, { status: 500, statusText: 'Server Error' });
      expect(outcome).toBe('linked');
      expect(snackbar.info).not.toHaveBeenCalled();
    });

    it('mit einem ANDEREN Discord-Konto verknüpft → die Rückfrage nennt die Verknüpfung, die ersetzt wird', () => {
      ask.and.returnValue(of(false));
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      profile('1234', 'mein_discord');
      expect(ask).toHaveBeenCalledOnceWith('profile.discord.confirmLinkReplace',
        { discord: 'fremder_jürgen', current: 'mein_discord' });
      http.expectNone('/api/profile/discord/link');
      expect(outcome).toBe('declined');
    });

    it('anderes Discord-Konto ohne gespeicherten Namen → die Rückfrage nennt dessen ID', () => {
      svc.confirmAndLink(TOKEN).subscribe();
      profile('1234', null);
      expect(ask).toHaveBeenCalledOnceWith('profile.discord.confirmLinkReplace',
        { discord: 'fremder_jürgen', current: '1234' });
      http.expectOne('/api/profile/discord/link').flush({});
    });

    it('Profil nicht lesbar → wie bisher nachfragen (nie still verknüpfen)', () => {
      ask.and.returnValue(of(false));
      let outcome: DiscordLinkOutcome | undefined;
      svc.confirmAndLink(TOKEN).subscribe(o => outcome = o);
      http.expectOne(r => r.method === 'GET' && r.url === '/api/profile')
        .flush({ message: 'boom' }, { status: 503, statusText: 'Unavailable' });
      expect(ask).toHaveBeenCalledOnceWith('profile.discord.confirmLink', { discord: 'fremder_jürgen' });
      http.expectNone('/api/profile/discord/link');
      expect(outcome).toBe('declined');
    });
  });

  it('stash() merkt den Token nur für diesen Tab vor (sessionStorage, nicht localStorage)', () => {
    svc.stash('tok123');
    expect(sessionStorage.getItem(DISCORD_LINK_STASH_KEY)).toBe('tok123');
    expect(localStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed() does nothing without a stashed token', () => {
    svc.consumeStashed();
    expect(ask).not.toHaveBeenCalled();
    http.expectNone('/api/profile/discord/link');
  });

  it('consumeStashed() fragt erst nach, dann verknüpft es und räumt den Stash', () => {
    svc.stash(TOKEN);
    svc.consumeStashed();
    profile();
    expect(ask).toHaveBeenCalledWith('profile.discord.confirmLink', { discord: 'fremder_jürgen' });
    const req = http.expectOne('/api/profile/discord/link');
    expect(req.request.body).toEqual({ token: TOKEN });
    req.flush({});
    expect(sessionStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed(): abgelehnt = kein POST, Stash weg (der Nächste bekommt ihn nicht)', () => {
    ask.and.returnValue(of(false));
    svc.stash(TOKEN);
    svc.consumeStashed();
    profile();
    http.expectNone('/api/profile/discord/link');
    expect(sessionStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed(): Konto schon mit derselben Discord-ID verknüpft → keine Rückfrage, Stash weg', () => {
    svc.stash(TOKEN);
    svc.consumeStashed();
    profile('4711', 'fremder_jürgen');
    expect(ask).not.toHaveBeenCalled();
    http.expectNone('/api/profile/discord/link');
    expect(sessionStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed() clears the stash on a 409 conflict', () => {
    svc.stash(TOKEN);
    svc.consumeStashed();
    profile();
    const req = http.expectOne('/api/profile/discord/link');
    req.flush({ message: 'conflict' }, { status: 409, statusText: 'Conflict' });
    expect(sessionStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });

  it('consumeStashed() keeps the stash on a transient (500) error', () => {
    svc.stash(TOKEN);
    svc.consumeStashed();
    profile();
    const req = http.expectOne('/api/profile/discord/link');
    req.flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });
    expect(sessionStorage.getItem(DISCORD_LINK_STASH_KEY)).toBe(TOKEN);
  });

  it('eine alte Vormerkung im localStorage wird nie eingelöst, nur weggeräumt', () => {
    localStorage.setItem(DISCORD_LINK_STASH_KEY, TOKEN);
    svc.consumeStashed();
    expect(ask).not.toHaveBeenCalled();
    http.expectNone('/api/profile');
    http.expectNone('/api/profile/discord/link');
    expect(localStorage.getItem(DISCORD_LINK_STASH_KEY)).toBeNull();
  });
});
