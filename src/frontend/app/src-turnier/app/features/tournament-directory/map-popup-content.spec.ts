import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import type * as L from 'leaflet';
import { groupByPoint } from './map-pin-groups';
import { MapPopupContent, MapPopupEvents } from './map-popup-content';
import { DirectoryEntry } from './tournament-directory.model';

function entry(id: string, over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    id, chessResultsId: id, name: `Turnier ${id}`, federation: 'AUT', state: null,
    startDate: '2026-10-10', endDate: '2026-10-12', location: 'Salzburg', timeControl: null,
    speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: null, playerCount: null, lat: 47.8, lon: 13.04, geoSource: 'City', geoPlaceName: null,
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [], ...over,
  };
}

/** Liefert den ViewContainerRef, in dem die Kurzansicht entsteht — wie die Karte. */
@Component({ selector: 'app-popup-probe', standalone: true, template: '' })
class ProbeComponent {
  readonly viewContainer = inject(ViewContainerRef);
}

describe('MapPopupContent', () => {
  let content: MapPopupContent;
  let selected: DirectoryEntry[];
  let updates: number;
  const popup = { update: () => updates++ } as unknown as L.Popup;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProbeComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
      ],
    }).compileComponents();
    const probe = TestBed.createComponent(ProbeComponent);
    selected = [];
    updates = 0;
    const events: MapPopupEvents = {
      selected: e => selected.push(e), ignored: () => undefined, subscribed: () => undefined,
    };
    content = new MapPopupContent(probe.componentInstance.viewContainer, key => key, () => 'en', events);
  });

  afterEach(() => content.forget());

  it('zeigt für EIN Turnier direkt die Kurzansicht und reicht die Auswahl weiter', () => {
    const [group] = groupByPoint([entry('1')]);

    const host = content.build(group);

    expect(host.className).toBe('tm-group');
    expect(host.querySelector('.tc-name')?.textContent?.trim()).toBe('Turnier 1');
    host.querySelector<HTMLButtonElement>('.tc-name')!.click();
    expect(selected.map(e => e.id)).toEqual(['1']);
  });

  it('gibt für dasselbe Bündel denselben Knoten zurück, bis das Popup vergessen ist', () => {
    const [group] = groupByPoint([entry('1'), entry('2')]);

    const first = content.build(group);
    expect(content.build(group)).toBe(first);

    content.forget();
    expect(content.build(group)).not.toBe(first);
  });

  it('wechselt im Bündel zwischen Liste und Kurzansicht und vermisst das offene Popup neu', () => {
    const [group] = groupByPoint([entry('1'), entry('2')]);
    const host = content.build(group);
    content.opened(popup);

    host.querySelectorAll<HTMLButtonElement>('.tm-group-row')[1].click();
    expect(host.querySelector('.tc-name')?.textContent?.trim()).toBe('Turnier 2');
    expect(updates).toBe(1);

    host.querySelector<HTMLButtonElement>('.tm-group-back')!.click();
    expect(host.querySelectorAll('.tm-group-row').length).toBe(2);
    expect(host.querySelector('.tc')).withContext('Kurzansicht nicht abgeräumt').toBeNull();
    expect(updates).toBe(2);
  });

  it('räumt beim Schließen die Kurzansicht ab und vermisst danach nichts mehr', () => {
    const [group] = groupByPoint([entry('1'), entry('2')]);
    const host = content.build(group);
    content.opened(popup);
    host.querySelectorAll<HTMLButtonElement>('.tm-group-row')[0].click();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const card = (content as any).card as { onDestroy(cb: () => void): void };
    let destroyed = false;
    card.onDestroy(() => (destroyed = true));

    content.closed();

    expect(destroyed).withContext('Kurzansicht nach dem Schließen nicht abgeräumt').toBeTrue();
    expect(content.build(group)).not.toBe(host);
    expect(updates).toBe(1);
  });
});
