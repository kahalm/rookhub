import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SimpleChange, SimpleChanges } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { isoWeek, localIsoDate, TournamentCalendarComponent } from './tournament-calendar.component';
import { DirectoryCalendarDay, DirectoryEntry } from './tournament-directory.model';

function entry(id: string, name: string): DirectoryEntry {
  return {
    id, chessResultsId: id, name, federation: 'AUT', state: null,
    startDate: null, endDate: null, location: null, timeControl: null, speed: 'Standard',
    organizer: null, director: null, chiefArbiter: null, rounds: null, playerCount: null,
    lat: null, lon: null, geoSource: 'None', geoPlaceName: null, distanceKm: null,
    cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [],
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

  it('zeigt in der Agenda nur Tage des angezeigten Monats', () => {
    // Beim Blaettern wechseln Jahr/Monat sofort, `days` erst mit der Antwort — bis dahin
    // standen die Turniere des alten Monats unter der neuen Ueberschrift.
    component.days = [day('2026-09-12', 'Alt'), day('2026-09-13', 'Auch alt')];
    apply({ days: component.days, month: 10 });

    expect(component.agenda.length).toBe(0);
  });

  it('deckelt die Agenda je Tag und klappt sie mit „+n weitere" auf', () => {
    const names = Array.from({ length: 8 }, (_, i) => `Turnier ${i + 1}`);
    component.days = [day('2026-10-10', ...names)];
    apply({ days: component.days });

    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelectorAll('.agenda-day .agenda-entry').length)
      .toBe(TournamentCalendarComponent.AgendaVisiblePerDay);
    const more = host.querySelectorAll<HTMLButtonElement>('.agenda-day .cal-more');
    expect(more.length).toBe(1);

    // Klick im DOM statt expand() direkt: OnPush rendert nur nach einem Ereignis neu.
    more[0].click();
    fixture.detectChanges();

    expect(host.querySelectorAll('.agenda-day .agenda-entry').length).toBe(8);
    expect(host.querySelectorAll('.agenda-day .cal-more').length).toBe(0);
  });

  // d7-t-kalender-handy-seite-zu-lang: am Handy war ein voller Monat 17,6 Bildschirme lang (6 je Tag, keine Gliederung)
  it('Handy-Agenda: höchstens 3 je Tag, nach Kalenderwochen gegliedert, Sprungleiste springt zur Woche', () => {
    const six = Array.from({ length: 6 }, (_, i) => `Turnier ${i + 1}`);
    component.days = [day('2026-10-01', ...six), day('2026-10-04', ...six), day('2026-10-07', ...six), day('2026-10-31', ...six)];
    apply({ days: component.days });

    const host: HTMLElement = fixture.nativeElement;
    expect(TournamentCalendarComponent.AgendaVisiblePerDay).toBe(3);
    for (const d of Array.from(host.querySelectorAll('.agenda-day'))) {
      expect(d.querySelectorAll('.agenda-entry').length).toBeLessThanOrEqual(3);
    }
    // Okt. 2026: 1.–4. = KW 40 (am Monatsanfang beschnitten), 5.–11. = KW 41, 26.–31. = KW 44
    expect(component.agendaWeeks.map(w => [w.week, w.days.length])).toEqual([[40, 2], [41, 1], [44, 1]]);
    expect(component.agendaWeeks[0].range).toBe(new Intl.DateTimeFormat('de', { day: 'numeric', month: 'short', timeZone: 'UTC' })
      .formatRange(new Date(Date.UTC(2026, 9, 1)), new Date(Date.UTC(2026, 9, 4))));
    expect(host.querySelectorAll('.agenda-week').length).toBe(3);
    expect(host.querySelectorAll('.agenda-week-head').length).toBe(3);

    const chips = host.querySelectorAll<HTMLButtonElement>('.week-jump .week-chip');
    expect(chips.length).toBe(3);
    const target = host.querySelector<HTMLElement>('#cal-week-2026-10-26')!;
    const scrolled = spyOn(target, 'scrollIntoView');
    chips[2].click();
    expect(scrolled).toHaveBeenCalledWith({ block: 'start' });
  });

  it('isoWeek: Jahreswechsel nach ISO 8601', () => {
    expect(isoWeek(new Date(Date.UTC(2026, 9, 5)))).toBe(41);
    expect(isoWeek(new Date(Date.UTC(2026, 0, 1)))).toBe(1);    // Do
    expect(isoWeek(new Date(Date.UTC(2027, 0, 1)))).toBe(53);   // Fr → KW 53 von 2026
    expect(isoWeek(new Date(Date.UTC(2024, 11, 30)))).toBe(1);  // Mo → KW 1 von 2025
  });

  it('eine einzige Woche braucht keine Sprungleiste', () => {
    component.days = [day('2026-10-10', 'Open Braunau')];
    apply({ days: component.days });
    expect((fixture.nativeElement as HTMLElement).querySelector('.week-jump')).toBeNull();
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

  describe('Heute-Markierung in Ortszeit (UX-076)', () => {
    afterEach(() => jasmine.clock().uninstall());

    it('markiert um 00:30 Ortszeit am Monatsersten den Ersten, nicht den Vortag', () => {
      jasmine.clock().install();
      // Lokale Uhr: 1. Oktober 2026, 00:30 — in Mitteleuropa ist das in UTC noch der 30. September.
      jasmine.clock().mockDate(new Date(2026, 9, 1, 0, 30));
      component.days = [];
      apply({ days: [] });

      const today = component.weeks.flat().filter(c => c.isToday);
      expect(today.map(c => c.date)).toEqual(['2026-10-01']);
      expect(today[0].inMonth).toBeTrue();
    });

    it('localIsoDate nimmt die Ortszeit-Bestandteile, auch wenn der UTC-Tag abweicht', () => {
      // Zeitzonen-unabhängig festgenagelt: ein Datum, dessen UTC-Tag noch der Vortag ist (z. B. CEST 00:30).
      const fake = {
        getFullYear: () => 2026, getMonth: () => 9, getDate: () => 1,
        getUTCFullYear: () => 2026, getUTCMonth: () => 8, getUTCDate: () => 30,
      } as unknown as Date;
      expect(localIsoDate(fake)).toBe('2026-10-01');
    });
  });
});
