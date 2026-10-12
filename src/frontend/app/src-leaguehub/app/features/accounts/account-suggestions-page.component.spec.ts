import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../../core/league-api.service';
import { MyGamesService } from '../../core/my-games.service';
import { AccountSuggestion } from '../../core/league.models';
import { AccountSuggestionsPageComponent, groupByPlayer } from './account-suggestions-page.component';

const S = (id: number, fide: string, name: string, score: number): AccountSuggestion => ({
  id, fide, name, team: 'Kufstein 1', site: 'lichess', user: 'u' + id, url: 'https://lichess.org/@/u' + id, score,
  evidence: 'Nutzername aus dem Namen', profileName: null, location: null, lastActive: null,
});

describe('AccountSuggestionsPageComponent', () => {
  let fixture: ComponentFixture<AccountSuggestionsPageComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;
  let perms: Set<string>;
  const el = () => fixture.nativeElement as HTMLElement;

  function create(): void {
    TestBed.configureTestingModule({
      imports: [AccountSuggestionsPageComponent],
      providers: [provideRouter([]), { provide: LeagueApiService, useValue: api }, { provide: AuthService, useValue: { has: (p: string) => perms.has(p) } },
        { provide: MyGamesService, useValue: { available: false } }],
    });
    fixture = TestBed.createComponent(AccountSuggestionsPageComponent);
    fixture.detectChanges();
  }

  beforeEach(() => {
    perms = new Set(['league.manage']);
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['suggestions', 'card', 'acceptSuggestion', 'rejectSuggestion', 'playerSuggestions']);
  });

  it('gruppiert nach Spieler in der Reihenfolge des stärksten Vorschlags', () => {
    const g = groupByPlayer([S(1, '222', 'Muster, Max', 6), S(2, '333', 'Huber, Franz', 5), S(3, '222', 'Muster, Max', 2)]);
    expect(g.map(x => [x.fide, x.items.map(i => i.id)])).toEqual([['222', [1, 3]], ['333', [2]]]);
  });

  it('ohne Verwalter-Recht nur der Hinweis, kein Abruf', () => {
    perms.clear();
    create();
    expect(el().textContent).toContain('Nicht freigeschaltet');
    // UI-Sweep 2026-10-11 (r2-l-gate-text): Weg zurück zu den Prognosen, kein Rechte-Schlüssel im Text
    expect(el().querySelector('.gate a[href="/"]')?.textContent).toContain('Zu den Prognosen');
    expect(el().textContent).not.toContain('league.manage');
    expect(api.suggestions).not.toHaveBeenCalled();
  });

  it('zeigt die Vorschläge je Spieler und den Stand der Suche; erledigte zählen mit', async () => {
    api.suggestions.and.resolveTo({ items: [S(1, '222', 'Muster, Max', 6), S(2, '333', 'Huber, Franz', 5)], scanned: 120, total: 400 });
    create();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(Array.from(el().querySelectorAll('.sugg-name')).map(b => b.textContent?.trim()))
      .toEqual(['Muster, Max', 'Huber, Franz']);
    expect(el().querySelector('[role=status]')?.textContent).toContain('2 offen. 120 von 400 Spielern der Saison abgesucht');
    fixture.componentInstance.onDecided();
    fixture.detectChanges();
    expect(el().querySelector('[role=status]')?.textContent).toContain('1 offen, 1 erledigt.');
  });

  it('alles abgesucht, nichts offen', async () => {
    api.suggestions.and.resolveTo({ items: [], scanned: 400, total: 400 });
    create();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().textContent).toContain('Keine offenen Vorschläge.');
    expect(el().textContent).toContain('Alle 400 Spieler der Saison sind abgesucht.');
  });

  // UI-Sweep 2026-10-11 (r2-l-same-account): übernommen → die Vorschläge desselben Kontos bei anderen Spielern verschwinden.
  it('übernommen mit „auch vorgeschlagen für": die anderen fallen aus der Übersicht', async () => {
    const a = { ...S(1, '301', 'Schmidt, Hans', 10), alsoFor: [{ id: 2, fide: '302', name: 'Schmidt, Hans Christoph', team: 'SC Haar 1', clubFits: false }] };
    const b = { ...S(2, '302', 'Schmidt, Hans Christoph', 11), alsoFor: [{ id: 1, fide: '301', name: 'Schmidt, Hans', team: 'SC Garching 4', clubFits: true }] };
    api.suggestions.and.resolveTo({ items: [b, a, S(3, '333', 'Huber, Franz', 5)], scanned: 1, total: 1 });
    create();
    await fixture.whenStable();
    fixture.componentInstance.onDecided({ suggestion: a, accepted: true });
    fixture.detectChanges();
    expect(fixture.componentInstance.items().map(x => x.id)).toEqual([1, 3]);
    expect(fixture.componentInstance.groups().map(g => g.fide)).toEqual(['301', '333']);
  });

  // z2-l-konten-spalten: die Spieler stehen in .sugg-groups; wer mehrere Vorschläge hat, trägt .multi (volle Breite)
  it('Spieler mit mehreren Vorschlägen sind als .multi markiert, alle stehen in .sugg-groups', async () => {
    api.suggestions.and.resolveTo({ items: [S(1, '222', 'Muster, Max', 6), S(2, '222', 'Muster, Max', 5), S(3, '333', 'Huber, Franz', 4)],
      scanned: 1, total: 1 });
    create();
    await fixture.whenStable();
    fixture.detectChanges();
    const groups = Array.from(el().querySelectorAll('.sugg-groups > .sugg-group'));
    expect(groups.length).toBe(2);
    expect(groups.map(g => g.classList.contains('multi'))).toEqual([true, false]);
  });

  // UI-Sweep 2026-10-11 (d2-l-konten-endlos): 20 Spieler je Seite, „Weitere", Suche nach Spieler oder Verein
  it('zeigt erst 20 Spieler, „Weitere" holt die nächsten; die Suche filtert nach Name und Mannschaft', async () => {
    const many = Array.from({ length: 45 }, (_, i) => ({ ...S(i + 1, String(1000 + i), `Spieler ${i + 1}`, 50 - i),
      team: i === 30 ? 'SC Haar 1' : 'Kufstein 1' }));
    api.suggestions.and.resolveTo({ items: many, scanned: 1, total: 1 });
    create();
    await fixture.whenStable();
    fixture.detectChanges();
    const names = () => el().querySelectorAll('.sugg-group').length;
    const more = () => el().querySelector('.sugg-more button') as HTMLButtonElement | null;
    expect(names()).toBe(20);
    expect(more()?.textContent).toContain('Weitere 20 von 25');
    more()!.click();
    fixture.detectChanges();
    expect(names()).toBe(40);
    expect(more()?.textContent).toContain('Weitere 5 von 5');
    more()!.click();
    fixture.detectChanges();
    expect(names()).toBe(45);
    expect(more()).toBeNull();

    const search = el().querySelector('.sugg-filter input') as HTMLInputElement;
    search.value = 'haar';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(Array.from(el().querySelectorAll('.sugg-name')).map(b => b.textContent?.trim())).toEqual(['Spieler 31']);
    search.value = 'spieler';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(names()).toBe(20);   // neue Suche beginnt wieder bei der ersten Seite
    search.value = 'niemand';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el().textContent).toContain('Kein Spieler passt zur Suche.');
  });

  // UI-Sweep 2026-10-11 (d2-l-konten-vorlauf): ein Satz + (i), Suchweise und Minderjährigen-Hinweis dahinter
  it('Erklärtext zugeklappt, der Zähler steht in der Kopfzeile', async () => {
    api.suggestions.and.resolveTo({ items: [S(1, '222', 'Muster, Max', 6)], scanned: 1, total: 1 });
    create();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().querySelector('.sugg-top [role=status]')?.textContent).toContain('1 offen.');
    expect(el().textContent).not.toContain('Minderjähriger');
    (el().querySelector('.sugg-page > .info-line .chk-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.sugg-info')?.textContent).toContain('Minderjähriger');
  });
});
