import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '../core/league-api.service';
import { AccountSuggestion } from '../core/league.models';
import { AccountSuggestionsComponent, lastActiveText, suggestionFacts } from './account-suggestions.component';

const S = (id: number, over: Partial<AccountSuggestion> = {}): AccountSuggestion => ({
  id, fide: '222', site: 'chess.com', user: 'Max_Muster', url: 'https://www.chess.com/member/Max_Muster', score: 2,
  evidence: 'Nutzername aus dem Namen; Land Österreich', profileName: null, location: null, lastActive: null, ...over,
});

describe('AccountSuggestionsComponent', () => {
  let fixture: ComponentFixture<AccountSuggestionsComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let decided: { id: number; accepted: boolean }[];
  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = (li: Element) => Array.from(li.querySelectorAll<HTMLButtonElement>('button'));

  beforeEach(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['acceptSuggestion', 'rejectSuggestion', 'accountChecks']);
    TestBed.configureTestingModule({ imports: [AccountSuggestionsComponent], providers: [{ provide: LeagueApiService, useValue: api }] });
    fixture = TestBed.createComponent(AccountSuggestionsComponent);
    decided = [];
    fixture.componentInstance.decided.subscribe(d => decided.push({ id: d.suggestion.id, accepted: d.accepted }));
  });

  it('(i) je Vorschlag lädt die Prüfung des Vorschlags; ein verborgener hat keins', async () => {
    api.accountChecks.and.resolveTo({ site: 'chess.com', user: 'Max_Muster', url: 'u', player: 'Muster, Max', elo: null, checkedAt: 'x',
      profileLoaded: false, items: [{ key: 'name', label: 'Name im Profil', status: 'none', text: 'nicht geprüft — chess.com nicht erreichbar' }] });
    fixture.componentRef.setInput('items', [S(1), S(2, { hidden: true, site: null, user: null, url: null })]);
    fixture.detectChanges();
    const infos = el().querySelectorAll<HTMLButtonElement>('.chk-btn');
    expect(infos.length).toBe(1);
    infos[0].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.accountChecks).toHaveBeenCalledWith('suggestion', 1);
    expect(el().textContent).toContain('nicht geprüft — chess.com nicht erreichbar');
    expect(el().textContent).toContain('Das Profil war gerade nicht abrufbar');
  });

  it('Hilfen: zuletzt aktiv, Profilangaben', () => {
    expect(lastActiveText('2026-09-01T10:00:00Z')).toBe('zuletzt aktiv 09/2026');
    expect(lastActiveText(null)).toBeNull();
    expect(suggestionFacts(S(1, { profileName: 'Max Muster', location: 'Schwaz', lastActive: '2025-03-10T00:00:00Z' })))
      .toBe('Profil: Max Muster, Schwaz, zuletzt aktiv 03/2025');
    expect(suggestionFacts(S(1))).toBe('');
  });

  it('übernehmen und verwerfen: Aufruf, Satz statt Knöpfen, Meldung nach oben', async () => {
    api.acceptSuggestion.and.resolveTo({ site: 'chess.com', user: 'Max_Muster', url: 'u', conf: 'wahrscheinlich' });
    api.rejectSuggestion.and.resolveTo();
    fixture.componentRef.setInput('items', [S(1), S(2, { site: 'lichess', user: 'MaxMuster', url: 'https://lichess.org/@/MaxMuster' })]);
    fixture.detectChanges();
    const rows = el().querySelectorAll('.sugg-list li');
    expect(rows[0].querySelector('a')?.textContent).toContain('chess.com: Max_Muster');
    expect(rows[0].querySelector('.sugg-evidence')?.textContent).toContain('Land Österreich');
    expect(rows[1].querySelector('a')?.getAttribute('href')).toBe('https://lichess.org/@/MaxMuster');

    buttons(rows[0]).find(b => b.textContent?.includes('unsicher'))!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.acceptSuggestion).toHaveBeenCalledWith(1, false);
    expect(el().querySelectorAll('.sugg-list li')[0].textContent).toContain('Als unsicher übernommen.');
    expect(el().querySelectorAll('.sugg-list li')[0].querySelector('.sugg-actions')).toBeNull();

    buttons(el().querySelectorAll('.sugg-list li')[1]).find(b => b.textContent?.includes('Verwerfen'))!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.rejectSuggestion).toHaveBeenCalledWith(2);
    expect(el().querySelectorAll('.sugg-list li')[1].textContent).toContain('wird nicht wieder vorgeschlagen');
    expect(decided).toEqual([{ id: 1, accepted: true }, { id: 2, accepted: false }]);
  });

  it('schon erledigt (404) und andere Absagen', async () => {
    fixture.componentRef.setInput('items', [S(1), S(2)]);
    fixture.detectChanges();
    api.acceptSuggestion.and.rejectWith(new HttpErrorResponse({ status: 404 }));
    await fixture.componentInstance.accept(S(1), true);
    expect(fixture.componentInstance.gone().get(1)).toBe('Schon erledigt.');
    api.acceptSuggestion.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'tooMany' } }));
    await fixture.componentInstance.accept(S(2), true);
    expect(fixture.componentInstance.error()).toContain('20 Konten');
    expect(fixture.componentInstance.gone().has(2)).toBeFalse();
    expect(decided).toEqual([]);
  });

  it('in der Übersicht mit Spieler: Klick auf den Namen meldet die FIDE-ID', () => {
    const opened: string[] = [];
    fixture.componentInstance.openPlayer.subscribe(f => opened.push(f));
    fixture.componentRef.setInput('items', [S(1, { name: 'Muster, Max', team: 'Kufstein 1' })]);
    fixture.componentRef.setInput('showPlayer', true);
    fixture.detectChanges();
    (el().querySelector('.sugg-player') as HTMLButtonElement).click();
    expect(opened).toEqual(['222']);
  });

  it('Vorschlag für einen Minderjährigen: ohne Konto, nur die Hinweise — übernehmen geht trotzdem (0.610.0)', async () => {
    api.acceptSuggestion.and.resolveTo({ site: null, user: null, url: null, conf: 'wahrscheinlich', hidden: true });
    fixture.componentRef.setInput('items', [S(4, { hidden: true, site: null, user: null, url: null })]);
    fixture.detectChanges();
    const row = el().querySelector('.sugg-list li')!;
    expect(row.querySelector('a')).toBeNull();
    expect(row.textContent).toContain('Online-Konto (verborgen – minderjährig)');
    expect(row.querySelector('.sugg-evidence')?.textContent).toContain('Land Österreich');
    await fixture.componentInstance.accept(S(4, { hidden: true }), false);
    expect(api.acceptSuggestion).toHaveBeenCalledWith(4, false);
  });
});
