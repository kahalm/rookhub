import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '@lh/core/league-api.service';
import { Account, AccountSuggestion } from '@lh/core/league.models';
import { PrepAccountsComponent } from './prep-accounts.component';
import { PrepApiService } from './prep-api.service';
import { PrepLeagueApi } from './prep-league-api';
import { PrepSuggestionList } from './prep.models';

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

  async function create(items: AccountSuggestion[] = [], remaining = 20, extra: Partial<PrepSuggestionList> = {}): Promise<void> {
    api = jasmine.createSpyObj<PrepApiService>('PrepApiService', ['suggestions', 'scanSuggestions', 'acceptSuggestion', 'rejectSuggestion',
      'suggestionChecks', 'updateAccount', 'deleteAccount']);
    api.suggestions.and.resolveTo({ items, perHour: 20, remaining, ...extra });
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
    buttons().find(b => b.textContent?.includes('Gesichert'))!.click();
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

  // ── Eingetragene Konten pflegen (0.639.0) ──

  const WEAK: Account = { id: 5, site: 'lichess', user: 'PaulFalsch', url: 'https://lichess.org/@/PaulFalsch', conf: 'wahrscheinlich',
    comment: 'Vorschlag der Konto-Suche: 2 Punkte', games: 3, syncedAt: null };
  const GOOD: Account = { id: 6, site: 'chess.com', user: 'PaulPrepmann', url: 'https://www.chess.com/member/PaulPrepmann', conf: 'sicher',
    comment: null, games: 40, syncedAt: null };
  const rows = () => Array.from(el().querySelectorAll<HTMLLIElement>('.prep-acc-list li'));
  const inRow = (i: number, sel: string) => rows()[i].querySelector(sel) as HTMLButtonElement | null;

  it('zeigt die eingetragenen Konten; umstufen ändert die Stufe, die Karte lädt neu', async () => {
    await create([], 20, { accounts: [WEAK, GOOD], leagueHub: false });
    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('PaulFalsch');
    expect(rows()[0].textContent).toContain('prep.accounts.unsure');
    expect(rows()[1].textContent).toContain('prep.accounts.sure');
    api.updateAccount.and.resolveTo({ ...WEAK, conf: 'sicher' });
    inRow(0, '.reclassify')!.click();
    await settle();
    expect(api.updateAccount).toHaveBeenCalledOnceWith(5, { sure: true });
    expect(rows()[0].querySelector('.conf')?.textContent).toContain('prep.accounts.sure');
    expect(el().querySelector('[role=status]')?.textContent).toContain('prep.accounts.madeSure');
    expect(changed).toBe(1);
  });

  it('entfernen erst nach Rückfrage — Abbrechen lässt es stehen, „Ja" entfernt es samt Meldung', async () => {
    await create([], 20, { accounts: [WEAK, GOOD], leagueHub: false });
    api.deleteAccount.and.resolveTo(undefined);
    inRow(0, '.remove')!.click();
    await settle();
    expect(rows()[0].textContent).toContain('prep.accounts.removeAsk');
    expect(api.deleteAccount).not.toHaveBeenCalled();
    Array.from(rows()[0].querySelectorAll<HTMLButtonElement>('.confirm button')).find(b => b.textContent?.includes('common.cancel'))!.click();
    await settle();
    expect(rows()[0].textContent).not.toContain('prep.accounts.removeAsk');
    expect(rows().length).toBe(2);
    inRow(0, '.remove')!.click();
    await settle();
    inRow(0, '.danger')!.click();
    await settle();
    expect(api.deleteAccount).toHaveBeenCalledOnceWith(5);
    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('PaulPrepmann');
    expect(el().querySelector('[role=status]')?.textContent).toContain('prep.accounts.removed');
    expect(changed).toBe(1);
  });

  it('auch ein Spieler von LeagueHub: Konten nur ansehen, kein Umstufen und kein Entfernen', async () => {
    await create([], 20, { accounts: [GOOD], leagueHub: true });
    expect(rows().length).toBe(1);
    expect(el().querySelector('.league-note')?.textContent).toContain('prep.accounts.leagueHub');
    expect(el().querySelector('.prep-acc-list button')).toBeNull();
  });

  it('Absagen: 409 leagueHub schaltet auf ansehen, 404 nimmt das Konto aus der Liste', async () => {
    await create([], 20, { accounts: [WEAK, GOOD], leagueHub: false });
    api.deleteAccount.and.rejectWith(new HttpErrorResponse({ status: 404, error: { reason: 'notFound' } }));
    inRow(0, '.remove')!.click();
    await settle();
    inRow(0, '.danger')!.click();
    await settle();
    expect(el().querySelector('.err')?.textContent).toContain('prep.accounts.accountGone');
    expect(rows().length).toBe(1);
    api.updateAccount.and.rejectWith(new HttpErrorResponse({ status: 409, error: { reason: 'leagueHub' } }));
    inRow(0, '.reclassify')!.click();
    await settle();
    expect(el().querySelector('.err')?.textContent).toContain('prep.accounts.leagueHubOnly');
    expect(el().querySelector('.prep-acc-list button')).toBeNull();
    expect(changed).toBe(0);
  });

  it('nach dem Übernehmen steht das neue Konto in der Liste darunter', async () => {
    await create([SUGG]);
    api.acceptSuggestion.and.resolveTo({ site: 'lichess', user: 'PaulPrepmann', url: 'u', conf: 'sicher' });
    api.suggestions.and.resolveTo({ items: [], perHour: 20, remaining: 20, accounts: [{ ...GOOD, site: 'lichess' }], leagueHub: false });
    Array.from(el().querySelectorAll<HTMLButtonElement>('.sugg-actions button')).find(b => b.textContent?.includes('Gesichert'))!.click();
    await settle();
    expect(changed).toBe(1);
    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('PaulPrepmann');
  });
});
