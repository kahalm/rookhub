import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { LeagueApiService } from '@lh/core/league-api.service';
import { Account, AccountSuggestion } from '@lh/core/league.models';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { useRealTranslations } from '../../testing/real-translations';
import { OnlineAccountsComponent } from './online-accounts.component';
import { AccountSuggestionsComponent } from './account-suggestions.component';
import { AccountChecksComponent } from './account-checks.component';

const ACC: Account = { id: 7, site: 'lichess', user: 'patrik', url: 'https://lichess.org/@/patrik', conf: 'sicher',
  comment: null, games: 1234, syncedAt: '2026-09-30T08:00:00Z', error: null };
const SUGG: AccountSuggestion = { id: 1, fide: '222', site: 'chess.com', user: 'Max_Muster', url: 'https://www.chess.com/member/Max_Muster',
  score: 2, evidence: '', profileName: null, location: null, lastActive: null };

/** Deutsche Wörter, die in einer englischen Oberfläche nicht stehen dürfen. */
const GERMAN = /Was geprüft wurde|Bearbeiten|Entfernen|Noch kein|Konto hinzufügen|Vorschläge der|Jetzt suchen|Keine offenen|Unsicher|Gesichert|Verwerfen|geholt|Prüfe|spricht|stutzig|Elo laut/;

/** Codereview F3-011: die Konto-Bausteine der Spielerkarte folgen in RookHub (Spielervorbereitung) der Oberflächensprache. */
describe('Konto-Bausteine der Spielerkarte auf Englisch (F3-011)', () => {
  let api: jasmine.SpyObj<LeagueApiService>;
  let ask: jasmine.Spy;

  beforeEach(async () => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService',
      ['playerSuggestions', 'scanSuggestions', 'acceptSuggestion', 'rejectSuggestion', 'accountChecks', 'deleteAccount']);
    api.playerSuggestions.and.resolveTo({ items: [] });
    api.accountChecks.and.resolveTo({ site: 'lichess', user: 'patrik', url: 'u', player: 'x', elo: 2100, checkedAt: 'x', profileLoaded: false,
      items: [{ key: 'name', label: 'Name', status: 'ok', text: '' }, { key: 'land', label: 'Land', status: 'warn', text: '' }] });
    ask = jasmine.createSpy('ask').and.returnValue(of(false));
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: LeagueApiService, useValue: api },
        { provide: ConfirmService, useValue: { ask } }],
    });
    await useRealTranslations('en');
  });

  it('Online-Konten: Knöpfe, Stand, Leerzustand und Rückfrage englisch', async () => {
    const f = TestBed.createComponent(OnlineAccountsComponent);
    f.componentRef.setInput('fide', '1606921');
    f.componentRef.setInput('accounts', [ACC]);
    f.componentRef.setInput('canEdit', true);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('1,234 online games fetched');
    expect(el.textContent).toContain('Search now');
    expect(el.textContent).not.toMatch(GERMAN);
    expect(el.querySelector('.chk-btn')?.getAttribute('aria-label')).toBe('What was checked');
    await f.componentInstance.remove(ACC);
    expect(ask.calls.mostRecent().args[0]).toBe('Remove the Lichess account “patrik”? The fetched games go with it.');

    f.componentRef.setInput('accounts', []);
    f.detectChanges();
    expect(el.textContent).toContain('No online account added yet.');
  });

  it('Vorschläge und (i)-Prüfung englisch', async () => {
    const f = TestBed.createComponent(AccountSuggestionsComponent);
    f.componentRef.setInput('items', [SUGG]);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(Array.from(el.querySelectorAll('.sa-btn')).map(b => b.textContent?.trim())).toEqual(['Unsure', 'Confirmed', 'Discard']);
    (el.querySelector('.chk-btn') as HTMLButtonElement).click();
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(el.textContent).toContain('1 speaks for it, 1 raises doubts.');
    expect(el.textContent).toContain('Rating in the entry list: 2100.');
    expect(el.textContent).not.toMatch(GERMAN);
  });

  it('ohne Sprache (LeagueHub-Specs) bleibt die deutsche Vorgabe', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: LeagueApiService, useValue: api }] });
    const f = TestBed.createComponent(AccountChecksComponent);
    f.componentRef.setInput('kind', 'account');
    f.componentRef.setInput('id', 7);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Prüfe …');
  });
});
