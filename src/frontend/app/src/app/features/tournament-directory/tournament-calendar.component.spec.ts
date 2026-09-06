import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SimpleChange, SimpleChanges } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentCalendarComponent } from './tournament-calendar.component';
import { DirectoryCalendarDay, DirectoryEntry } from './tournament-directory.model';

function entry(id: string, name: string): DirectoryEntry {
  return {
    chessResultsId: id, name, federation: 'AUT', state: null,
    startDate: null, endDate: null, location: null, timeControl: null, speed: 'Standard',
    organizer: null, director: null, chiefArbiter: null, rounds: null, playerCount: null,
    lat: null, lon: null, geoSource: 'None', geoPlaceName: null, distanceKm: null,
    cancelled: false, subscribed: false,
  };
}

function day(date: string, ...names: string[]): DirectoryCalendarDay {
  return { date, items: names.map((n, i) => entry(`${date}-${i}`, n)) };
}

describe('TournamentCalendarComponent', () => {
  let fixture: ComponentFixture<TournamentCalendarComponent>;
  let component: TournamentCalendarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentCalendarComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();

    fixture = TestBed.createComponent(TournamentCalendarComponent);
    component = fixture.componentInstance;
    component.year = 2026;
    component.month = 10;
    component.locale = 'de';
  });

  /**
   * Direkt am Objekt gesetzte Inputs loesen KEIN ngOnChanges aus — das macht nur ein
   * Template-Binding. Der Aufruf hier steht stellvertretend fuer den Elternteil.
   */
  function apply(changed: Partial<Record<'days' | 'year' | 'month' | 'locale', unknown>> = {}) {
    const changes: SimpleChanges = {};
    for (const key of Object.keys(changed)) changes[key] = new SimpleChange(null, changed[key as never], false);
    component.ngOnChanges(changes);
    fixture.detectChanges();
  }

  it('baut ein Raster aus vollen Wochen, das am Montag beginnt', () => {
    component.days = [];
    apply({ days: [] });

    expect(component.weekdayLabels.length).toBe(7);
    for (const week of component.weeks) expect(week.length).toBe(7);
    // Oktober 2026 beginnt an einem Donnerstag → die erste Zelle ist der 28. September.
    expect(component.weeks[0][0].date).toBe('2026-09-28');
    expect(component.weeks[0][0].inMonth).toBeFalse();
  });

  it('markiert die Tage des Vor- und Folgemonats als ausserhalb', () => {
    component.days = [];
    apply({ days: [] });

    const inMonth = component.weeks.flat().filter(c => c.inMonth);
    expect(inMonth.length).toBe(31);
    expect(inMonth[0].date).toBe('2026-10-01');
    expect(inMonth[30].date).toBe('2026-10-31');
  });

  it('hängt die Turniere an ihren Tag', () => {
    component.days = [day('2026-10-10', 'Open Braunau'), day('2026-10-11', 'Blitzcup', 'Jugend')];
    apply({ days: component.days });

    const cells = component.weeks.flat();
    expect(cells.find(c => c.date === '2026-10-10')!.entries.length).toBe(1);
    expect(cells.find(c => c.date === '2026-10-11')!.entries.length).toBe(2);
    expect(cells.find(c => c.date === '2026-10-12')!.entries.length).toBe(0);
  });

  it('verträgt Tagesangaben mit Zeitanteil', () => {
    // Der Server serialisiert DateOnly je nach Einstellung auch mal mit T00:00:00.
    component.days = [{ date: '2026-10-10T00:00:00', items: [entry('1', 'Mit Zeitanteil')] }];
    apply({ days: component.days });

    expect(component.weeks.flat().find(c => c.date === '2026-10-10')!.entries.length).toBe(1);
  });

  it('füllt die Agenda nur mit Tagen, an denen etwas stattfindet', () => {
    component.days = [day('2026-10-10', 'Open'), day('2026-10-11')];
    apply({ days: component.days });

    expect(component.agenda.length).toBe(1);
    expect(component.agenda[0].date).toBe('2026-10-10');
  });

  it('blättert über den Jahreswechsel hinweg', () => {
    component.year = 2026;
    component.month = 12;
    apply({ month: 12 });

    const emitted: { year: number; month: number }[] = [];
    component.monthChanged.subscribe(e => emitted.push(e));

    component.nextMonth();
    expect(emitted[0]).toEqual({ year: 2027, month: 1 });

    component.month = 1;
    component.year = 2027;
    component.previousMonth();
    expect(emitted[1]).toEqual({ year: 2026, month: 12 });
  });

  it('meldet beim Klick auf einen Eintrag genau diesen Eintrag', () => {
    component.days = [day('2026-10-10', 'Open Braunau')];
    apply({ days: component.days });

    let selected: DirectoryEntry | null = null;
    component.entrySelected.subscribe(e => (selected = e));
    component.entrySelected.emit(component.weeks.flat().find(c => c.date === '2026-10-10')!.entries[0]);

    expect(selected!['name']).toBe('Open Braunau');
  });

  it('nimmt die Monats- und Wochentagsnamen aus der eingestellten Sprache', () => {
    component.locale = 'en';
    component.days = [];
    apply({ days: [] });

    expect(component.monthLabel).toContain('October');
    expect(component.weekdayLabels[0]).toBe('Mon');
  });
});
