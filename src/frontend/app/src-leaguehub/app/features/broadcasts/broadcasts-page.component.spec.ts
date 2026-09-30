import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../../core/league-api.service';
import { Broadcast } from '../../core/league.models';
import { BroadcastsPageComponent, broadcastDates, broadcastErrorText, broadcastStatus } from './broadcasts-page.component';

const B = (over: Partial<Broadcast> = {}): Broadcast => ({
  tourId: 'FX5cToFp', name: 'Schach Tirol Open 2026', location: 'Innsbruck, Austria', url: 'https://lichess.org/broadcast/-/FX5cToFp',
  startsAt: '2026-08-22', endsAt: '2026-08-29', manual: false, importedAt: '2026-09-30T10:00:00Z', finished: true, games: 88, error: null,
  ...over,
});

describe('BroadcastsPageComponent', () => {
  let fixture: ComponentFixture<BroadcastsPageComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let perms: Set<string>;
  const el = () => fixture.nativeElement as HTMLElement;

  function create(): void {
    TestBed.configureTestingModule({
      imports: [BroadcastsPageComponent],
      providers: [{ provide: LeagueApiService, useValue: api }, { provide: AuthService, useValue: { has: (p: string) => perms.has(p) } }],
    });
    fixture = TestBed.createComponent(BroadcastsPageComponent);
    fixture.detectChanges();
  }

  beforeEach(() => {
    perms = new Set(['league.manage']);
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['broadcasts', 'addBroadcast']);
  });

  it('Hilfen: Termin, Stand, Absagen', () => {
    expect(broadcastDates(B())).toBe('22.08.–29.08.2026');
    expect(broadcastDates(B({ endsAt: '2026-08-22' }))).toBe('22.08.2026');
    expect(broadcastDates(B({ startsAt: null }))).toBe('');
    expect(broadcastStatus(B())).toBe('fertig — 88 Partien mit Ligaspielern');
    expect(broadcastStatus(B({ finished: false, games: 1 }))).toBe('läuft — 1 Partie mit Ligaspielern, alle 6 Stunden nachgeholt');
    expect(broadcastStatus(B({ finished: false, importedAt: null, startsAt: '2099-01-01' }))).toBe('beginnt noch');
    expect(broadcastStatus(B({ finished: false, importedAt: null }))).toBe('wird demnächst eingespielt');
    expect(broadcastStatus(B({ error: 'Übertragung nicht gefunden' }))).toBe('Fehler: Übertragung nicht gefunden');
    expect(broadcastErrorText('invalidUrl')).toContain('lichess.org/broadcast');
    expect(broadcastErrorText(undefined)).toContain('nicht erreichbar');
  });

  it('ohne Verwalter-Recht nur der Hinweis', () => {
    perms.clear();
    create();
    expect(el().textContent).toContain('Nicht freigeschaltet');
    expect(api.broadcasts).not.toHaveBeenCalled();
  });

  it('listet die Übertragungen und fügt eine per Link hinzu', async () => {
    api.broadcasts.and.resolveTo([B(), B({ tourId: 'favpBItT', name: 'Kufstein Open', manual: true, games: 45 })]);
    create();
    await fixture.whenStable();
    fixture.detectChanges();
    const rows = el().querySelectorAll('.bc-list li');
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('https://lichess.org/broadcast/-/FX5cToFp');
    expect(rows[1].textContent).toContain('per Link');

    api.addBroadcast.and.resolveTo({ tourId: 'abcdefgh', name: 'Open Bozen', games: 12, finished: true, error: null });
    fixture.componentInstance.link.set('https://lichess.org/broadcast/open-bozen/abcdefgh');
    await fixture.componentInstance.add();
    fixture.detectChanges();
    expect(api.addBroadcast).toHaveBeenCalledWith('https://lichess.org/broadcast/open-bozen/abcdefgh');
    expect(el().querySelector('[role=status]')?.textContent).toContain('„Open Bozen“ eingespielt: 12 Partien mit Ligaspielern.');
    expect(api.broadcasts).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.link()).toBe('');

    api.addBroadcast.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'invalidUrl' } }));
    fixture.componentInstance.link.set('quatsch');
    await fixture.componentInstance.add();
    expect(fixture.componentInstance.note()).toEqual({ text: broadcastErrorText('invalidUrl'), err: true });
  });
});
