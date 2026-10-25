import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { MapLegendComponent } from './map-legend.component';
import { PinColourBy } from './pin-palette';

describe('MapLegendComponent', () => {
  let fixture: ComponentFixture<MapLegendComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MapLegendComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    fixture = TestBed.createComponent(MapLegendComponent);
    host = fixture.nativeElement;
  });

  const rows = () => [...host.querySelectorAll('.legend li')].map(n => n.textContent?.trim());
  const open = () => { host.querySelector<HTMLButtonElement>('.legend-toggle')!.click(); fixture.detectChanges(); };

  it('steht zugeklappt und klappt per Knopf auf und wieder zu', () => {
    fixture.detectChanges();
    const toggle = host.querySelector<HTMLButtonElement>('.legend-toggle')!;
    expect(host.querySelector('.legend')).toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    open();
    expect(rows().length).toBeGreaterThan(0);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(host.querySelector('.chevron.open')).not.toBeNull();

    open();
    expect(host.querySelector('.legend')).toBeNull();
  });

  it('listet die Klassen des gewählten Merkmals, dann „gemerkt" und „ungefähr"', () => {
    fixture.componentRef.setInput('colourBy', 'kind');
    fixture.detectChanges();
    open();

    expect(rows()[0]).toContain('tournamentDirectory.kind.');
    expect(rows().at(-2)).toContain('tournamentDirectory.map.legend.bookmarked');
    expect(rows().at(-1)).toContain('tournamentDirectory.map.legend.approximate');
    expect(rows().join()).not.toContain('tournamentDirectory.map.legend.mixed');
  });

  it('erklärt „gemischt" nur, wenn es auf der Karte vorkommt', () => {
    fixture.componentRef.setInput('mixed', true);
    fixture.detectChanges();
    open();

    expect(rows().join()).toContain('tournamentDirectory.map.legend.mixed');
  });

  it('zeigt die Wahl in der Auswahl und meldet eine neue nach draußen', () => {
    const picked: PinColourBy[] = [];
    fixture.componentInstance.colourPicked.subscribe(c => picked.push(c));
    // Erst zeichnen, dann umstellen: beim ERSTEN Durchlauf setzt [value] die Auswahl, bevor die
    // Optionen stehen, und der Browser zeigt die erste — so schon vor der Zerlegung (offener Punkt).
    fixture.detectChanges();
    fixture.componentRef.setInput('colourBy', 'gender');
    fixture.detectChanges();
    const select = host.querySelector<HTMLSelectElement>('.colour-by select')!;
    expect(select.value).toBe('gender');

    select.value = 'kind';
    select.dispatchEvent(new Event('change'));

    expect(picked).toEqual(['kind']);
  });
});
