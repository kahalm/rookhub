import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '@lh/core/league-api.service';
import { AccountSuggestion } from '@lh/core/league.models';
import { PrepAccountsComponent } from './prep-accounts.component';
import { PrepApiService } from './prep-api.service';
import { PrepLeagueApi } from './prep-league-api';

const SUGG: AccountSuggestion = {
  id: 7, fide: '990777', site: 'lichess', user: 'PaulPrepmann', url: 'https://lichess.org/@/PaulPrepmann', score: 5,
  evidence: 'Nutzername aus dem Namen; Klarname im Profil („Paul Prepmann“); Land DE', profileName: 'Paul Prepmann', location: null, lastActive: null,
};

describe('PrepAccountsComponent (Online-Konten suchen, Phase 4)', () => {
  let fixture: ComponentFixture<PrepAccountsComponent>;
  let api: jasmine.SpyObj<PrepApiService>;
  let changed: number;
  const el = () => fixture.nativeElement as HTMLElement;
  const button = () => el().querySelector('.actions button') as HTMLButtonElement;

  async function create(items: AccountSuggestion[] = [], remaining = 20): Promise<void> {
    api = jasmine.createSpyObj<PrepApiService>('PrepApiService', ['suggestions', 'scanSuggestions', 'acceptSuggestion', 'rejectSuggestion', 'suggestionChecks']);
    api.suggestions.and.resolveTo({ items, perHour: 20, remaining });
    TestBed.configureTestingModule({
      imports: [PrepAccountsComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: PrepApiService, useValue: api },
        PrepLeagueApi, { provide: LeagueApiService, useExisting: PrepLeagueApi }],
    });
    fixture = TestBed.createComponent(PrepAccountsComponent);
    fixture.componentRef.setInput('playerId', 4711);
    changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
    fixture.detectChanges();
  }

  it('lädt die offenen Vorschläge; ohne welche sagt es das', async () => {
    await create();
    expect(api.suggestions).toHaveBeenCalledWith(4711);
    expect(el().textContent).toContain('prep.accounts.none');
    expect(el().querySelector('lh-account-suggestions')).toBeNull();
    expect(el().textContent).toContain('prep.accounts.remaining');
  });

  it('suchen zeigt die neuen Vorschläge und wie viele es waren', async () => {
    await create();
    api.scanSuggestions.and.resolveTo({ items: [SUGG], perHour: 20, remaining: 19, found: 1 });
    button().click();
    await settle();
    expect(api.scanSuggestions).toHaveBeenCalledOnceWith(4711);
    expect(el().querySelector('lh-account-suggestions')?.textContent).toContain('PaulPrepmann');
    expect(el().querySelector('[role=status]')?.textContent).toContain('prep.accounts.found');
  });

  it('Absagen des Servers als Satz; nach „limit" ist der Knopf aus', async () => {
    await create();
    for (const [status, reason, key] of [[409, 'busy', 'prep.accounts.busy'], [503, 'rateLimited', 'prep.accounts.rateLimited'],
      [503, 'unreachable', 'prep.accounts.unreachable'], [429, 'limit', 'prep.accounts.limit']] as const) {
      api.scanSuggestions.and.rejectWith(new HttpErrorResponse({ status, error: { reason } }));
      button().click();
      await settle();
      expect(el().querySelector('.err')?.textContent).toContain(key);
    }
    expect(button().disabled).toBeTrue();
  });

  it('der eingebundene LeagueHub-Baustein übernimmt, verwirft und prüft über /api/prep — und die Seite lädt neu', async () => {
    await create([SUGG, { ...SUGG, id: 8, user: 'Prepmann_Paul' }]);
    api.acceptSuggestion.and.resolveTo({ site: 'lichess', user: 'PaulPrepmann', url: 'u', conf: 'sicher' });
    api.rejectSuggestion.and.resolveTo(undefined);
    api.suggestionChecks.and.resolveTo({ site: 'lichess', user: 'PaulPrepmann', url: 'u', player: 'Prepmann, Paul', elo: 2210,
      checkedAt: '2026-10-02T10:00:00Z', profileLoaded: true, items: [] });
    const buttons = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.sugg-actions button'));
    buttons().find(b => b.textContent?.includes('Als gesichert übernehmen'))!.click();
    await settle();
    expect(api.acceptSuggestion).toHaveBeenCalledWith(7, true);
    expect(changed).toBe(1);
    buttons().find(b => b.textContent?.includes('Verwerfen'))!.click();
    await settle();
    expect(api.rejectSuggestion).toHaveBeenCalledWith(8);
    expect(changed).toBe(1);                                                      // verwerfen lädt die Karte nicht neu
    (el().querySelector('.chk-btn') as HTMLButtonElement).click();
    await settle();
    expect(api.suggestionChecks).toHaveBeenCalledWith(7);
  });

  it('sagt der Server die (i)-Prüfung ab (Suche läuft, Seite bremst), steht der Grund im Abschnitt; die nächste Suche räumt ihn weg', async () => {
    await create([SUGG]);
    const chk = () => el().querySelector('.chk-btn') as HTMLButtonElement;
    const note = () => el().querySelector('.check-note')?.textContent ?? null;
    for (const [status, reason, key] of [[409, 'busy', 'prep.accounts.checksBusy'], [503, 'rateLimited', 'prep.accounts.checksRateLimited']] as const) {
      api.suggestionChecks.and.rejectWith(new HttpErrorResponse({ status, error: { reason } }));
      chk().click();                                                                // öffnen → prüft
      await settle();
      expect(note()).toContain(key);
      expect(el().querySelector('lh-account-checks .err')).not.toBeNull();          // der Baustein selbst: „ließ sich nicht laden"
      chk().click();                                                                // schließen
      await settle();
    }
    api.suggestionChecks.and.rejectWith(new HttpErrorResponse({ status: 404, error: { reason: 'notFound' } }));
    chk().click();
    await settle();
    expect(note()).toBeNull();                                                      // 404 erklärt der Baustein selbst
    chk().click();
    await settle();
    api.suggestionChecks.and.rejectWith(new HttpErrorResponse({ status: 409, error: { reason: 'busy' } }));
    chk().click();
    await settle();
    expect(note()).toContain('prep.accounts.checksBusy');
    api.scanSuggestions.and.resolveTo({ items: [SUGG], perHour: 20, remaining: 19, found: 0 });
    button().click();
    await settle();
    expect(note()).toBeNull();
  });
});
