import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { DirectoryFilterPanelComponent } from './directory-filter-panel.component';
import { DirectoryFilter } from './tournament-directory.model';

describe('DirectoryFilterPanelComponent', () => {
  let fixture: ComponentFixture<DirectoryFilterPanelComponent>;
  let host: HTMLElement;
  let patches: Partial<DirectoryFilter>[];
  let events: string[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DirectoryFilterPanelComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations()],
    }).compileComponents();
    fixture = TestBed.createComponent(DirectoryFilterPanelComponent);
    host = fixture.nativeElement;
    patches = [];
    events = [];
    const c = fixture.componentInstance;
    c.rangePresetChange.subscribe(v => events.push(`range:${v}`));
    c.radiusChange.subscribe(v => events.push(`radius:${v}`));
    c.searchTextChange.subscribe(v => events.push(`text:${v}`));
    c.kindsChange.subscribe(v => events.push(`kinds:${v}`));
    c.adultsOnlyChange.subscribe(v => events.push(`adults:${v}`));
    c.includeIgnoredChange.subscribe(v => events.push(`ignored:${v}`));
    c.speedChange.subscribe(v => events.push(`speed:${v}`));
    c.reload.subscribe(() => events.push('reload'));
    c.reset.subscribe(() => events.push('reset'));
    c.patch.subscribe(p => patches.push(p));
    fixture.componentRef.setInput('rangePreset', 'quarter');
  });

  const render = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };

  it('ordnet das Feld in Zeilen nach Sinn', async () => {
    await render();
    const rows = [...host.querySelectorAll('.filter-extra > .extra-row')].map(r => r.classList[1]);
    expect(rows).toEqual(['period-row', 'search-row', 'chip-row', 'toggle-row', 'end-row']);
    expect(host.querySelectorAll('.period-row .extra-narrow').length).toBe(2);
    expect(host.querySelector('.date-field')).withContext('Datumsfelder ohne eigenen Zeitraum').toBeNull();
  });

  it('zeigt Datumsfelder nur beim eigenen Zeitraum und schreibt sie direkt in den Filter', async () => {
    fixture.componentRef.setInput('rangePreset', 'custom');
    await render();
    const from = host.querySelectorAll<HTMLInputElement>('.date-field input')[0];

    from.value = '2026-11-01';
    from.dispatchEvent(new Event('input'));
    from.dispatchEvent(new Event('change'));

    expect(patches).toEqual([{ from: '2026-11-01' }]);
    expect(events).toEqual(['reload']);
  });

  it('bietet „Ausgeblendete zeigen" nur mit Konto', async () => {
    await render();
    const toggles = () => [...host.querySelectorAll('mat-slide-toggle')].map(t => t.textContent!.trim());
    expect(toggles()).not.toContain('tournamentDirectory.card.showIgnored');

    fixture.componentRef.setInput('loggedIn', true);
    await render();
    expect(toggles()).toContain('tournamentDirectory.card.showIgnored');
  });

  it('meldet Schalter, die der Elternteil nachbearbeitet, und schreibt die übrigen direkt', async () => {
    fixture.componentRef.setInput('loggedIn', true);
    await render();
    const toggle = (label: string) => [...host.querySelectorAll('mat-slide-toggle')]
      .find(t => t.textContent!.includes(label))!.querySelector<HTMLButtonElement>('button')!;

    toggle('tournamentDirectory.adultsOnly').click();
    toggle('tournamentDirectory.hideLeagues').click();
    toggle('tournamentDirectory.card.showIgnored').click();

    expect(events).toEqual(['adults:true', 'reload', 'ignored:true']);
    expect(patches).withContext('nur die frueher direkt geschriebenen').toEqual([{ hideLeagues: true }]);
  });

  it('meldet Art und Bedenkzeit als Auswahl', async () => {
    await render();
    const chip = (label: string) => [...host.querySelectorAll<HTMLElement>('mat-chip-option')]
      .find(c => c.textContent!.includes(label))!.querySelector<HTMLElement>('.mdc-evolution-chip__action')!;

    chip('tournamentDirectory.kind.Team').click();
    chip('tournamentDirectory.speed.Rapid').click();

    expect(events).toEqual(['kinds:Team', 'speed:Rapid']);
  });

  it('leert die Suche und lädt dann neu; Enter lädt ebenfalls', async () => {
    fixture.componentRef.setInput('searchText', 'Open');
    await render();
    const input = host.querySelector<HTMLInputElement>('.search-field input')!;

    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    host.querySelector<HTMLButtonElement>('.search-field button')!.click();

    expect(events).toEqual(['reload', 'text:', 'reload']);
  });

  it('zeigt Werte, die der Elternteil in place geändert hat (OnPush, ein Eingang je Feld)', async () => {
    await render();
    const adults = () => [...host.querySelectorAll('mat-slide-toggle')]
      .find(t => t.textContent!.includes('tournamentDirectory.adultsOnly'))!.querySelector('button')!;
    expect(adults().getAttribute('aria-checked')).toBe('false');

    fixture.componentRef.setInput('adultsOnly', true);
    await render();

    expect(adults().getAttribute('aria-checked')).toBe('true');
  });

  it('setzt per Knopf zurück', async () => {
    await render();
    host.querySelector('.end-row')!.lastElementChild!.dispatchEvent(new MouseEvent('click'));
    expect(events).toEqual(['reset']);
  });
});
