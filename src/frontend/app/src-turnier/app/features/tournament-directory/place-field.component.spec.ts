import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PlaceFieldComponent } from './place-field.component';
import { GeoPlaceSuggestion } from './tournament-directory.model';

const HALLEIN: GeoPlaceSuggestion = { label: '5400 Hallein (AT)', country: 'AT', postalCode: '5400', lat: 47.68, lon: 13.1 };

describe('PlaceFieldComponent', () => {
  let fixture: ComponentFixture<PlaceFieldComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlaceFieldComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations()],
    }).compileComponents();
    fixture = TestBed.createComponent(PlaceFieldComponent);
    host = fixture.nativeElement;
  });

  const set = (inputs: Record<string, unknown>) => {
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  };
  const buttons = () => [...host.querySelectorAll<HTMLButtonElement>('button[matSuffix]')];
  const openPanel = async () => {
    const input = host.querySelector('input')!;
    input.dispatchEvent(new Event('focusin'));
    fixture.detectChanges();
    await fixture.whenStable();
    return [...document.querySelectorAll('.mat-mdc-autocomplete-panel mat-option')]
      .map(o => ({ text: o.textContent?.trim(), disabled: o.getAttribute('aria-disabled') === 'true', note: o.classList.contains('place-note') }));
  };

  it('meldet jede Eingabe nach draußen', () => {
    const typed: string[] = [];
    fixture.componentInstance.labelChange.subscribe(v => typed.push(v));
    set({});
    const input = host.querySelector('input')!;

    input.value = 'Hal';
    input.dispatchEvent(new Event('input'));

    expect(typed).toEqual(['Hal']);
  });

  it('zeigt bei laufender Suche die Sanduhr statt des Leeren-Knopfs', () => {
    set({ label: 'Hallein', searching: true });
    expect(host.querySelector('mat-spinner')).not.toBeNull();
    expect(buttons().length).toBe(0);

    set({ searching: false });
    expect(host.querySelector('mat-spinner')).toBeNull();
    expect(buttons().map(b => b.getAttribute('aria-label'))).toEqual(['common.clear']);
  });

  it('leert per x und bietet den Standort nur an, wo der Browser ihn kennt', () => {
    let cleared = 0;
    let located = 0;
    fixture.componentInstance.cleared.subscribe(() => cleared++);
    fixture.componentInstance.locate.subscribe(() => located++);
    set({ label: 'Hallein', geolocationSupported: true });

    const [clear, locate] = buttons();
    clear.click();
    locate.click();
    expect([cleared, located]).toEqual([1, 1]);
    expect(locate.getAttribute('aria-label')).toBe('tournamentDirectory.place.useCurrent');
    expect(locate.textContent?.trim()).toBe('my_location');

    set({ locating: true });
    expect(buttons()[1].disabled).toBeTrue();
    expect(buttons()[1].textContent?.trim()).toBe('hourglass_top');

    set({ geolocationSupported: false, label: '' });
    expect(buttons().length).toBe(0);
  });

  it('listet die Vorschläge und meldet den gewählten', async () => {
    const chosen: GeoPlaceSuggestion[] = [];
    fixture.componentInstance.chosen.subscribe(p => chosen.push(p));
    set({ suggestions: [HALLEIN] });

    const options = await openPanel();
    expect(options).toEqual([{ text: '5400 Hallein (AT)', disabled: false, note: false }]);

    document.querySelector<HTMLElement>('.mat-mdc-autocomplete-panel mat-option')!.click();
    expect(chosen).toEqual([HALLEIN]);
  });

  it('sagt „kein Ort gefunden" bzw. „Suche gescheitert" als nicht wählbare Zeile', async () => {
    set({ noMatch: true, searchFailed: true });
    expect(await openPanel())
      .toEqual([{ text: 'tournamentDirectory.place.noMatch', disabled: true, note: true }]);

    set({ noMatch: false });
    expect(await openPanel())
      .toEqual([{ text: 'tournamentDirectory.place.searchFailed', disabled: true, note: true }]);
  });
});
