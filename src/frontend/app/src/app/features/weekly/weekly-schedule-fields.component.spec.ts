import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import { MAT_DATE_LOCALE } from '@angular/material/core';
import { provideTranslateService } from '@ngx-translate/core';
import { LocaleNativeDateAdapter, WEEKLY_DATE_FORMATS, WeeklyScheduleFieldsComponent, dateFieldOrder } from './weekly-schedule-fields.component';

describe('dateFieldOrder', () => {
  it('kennt die Reihenfolge je Sprache', () => {
    expect(dateFieldOrder('de')).toEqual(['day', 'month', 'year']);
    expect(dateFieldOrder('en-US')).toEqual(['month', 'day', 'year']);
    expect(dateFieldOrder('hu')).toEqual(['year', 'month', 'day']);
  });
});

describe('LocaleNativeDateAdapter', () => {
  function adapter(locale: string): LocaleNativeDateAdapter {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [LocaleNativeDateAdapter, { provide: MAT_DATE_LOCALE, useValue: locale }] });
    return TestBed.inject(LocaleNativeDateAdapter);
  }

  it('liest TT.MM.JJJJ in Deutsch und schreibt zweistellig', () => {
    const a = adapter('de');
    const d = a.parse('16.07.2026')!;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 6, 16]);
    expect(a.format(d, WEEKLY_DATE_FORMATS.display.dateInput)).toBe('16.07.2026');
  });

  it('nimmt in Englisch die US-Reihenfolge', () => {
    const d = adapter('en-US').parse('07/16/2026')!;
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 6, 16]);
  });

  it('verwirft unmögliche und halb getippte Daten', () => {
    const a = adapter('de');
    expect(a.isValid(a.parse('31.02.2026')!)).toBeFalse();
    expect(a.isValid(a.parse('16.07.202')!)).toBeFalse();
  });

  it('zeigt die Uhrzeit in Deutsch mit 24 h', () => {
    const a = adapter('de');
    expect(a.format(new Date(2026, 6, 16, 21, 0), WEEKLY_DATE_FORMATS.display.timeInput)).toBe('21:00');
  });
});

describe('WeeklyScheduleFieldsComponent', () => {
  beforeAll(() => registerLocaleData(localeDe));

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WeeklyScheduleFieldsComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), { provide: LOCALE_ID, useValue: 'de' }],
    });
  });

  it('zeigt Datum und Uhrzeit in der App-Sprache', async () => {
    const f = TestBed.createComponent(WeeklyScheduleFieldsComponent);
    f.componentRef.setInput('date', '2026-07-16');
    f.componentRef.setInput('time', '21:00');
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    const inputs = f.nativeElement.querySelectorAll('input') as NodeListOf<HTMLInputElement>;
    expect(inputs[0].value).toBe('16.07.2026');
    expect(inputs[1].value).toBe('21:00');
  });

  it('gibt Datum und Uhrzeit als YYYY-MM-DD / HH:mm zurück', () => {
    const c = TestBed.createComponent(WeeklyScheduleFieldsComponent).componentInstance;
    c.onDate(new Date(2026, 6, 9));
    c.onTime(new Date(2000, 0, 1, 7, 5));
    expect(c.date()).toBe('2026-07-09');
    expect(c.time()).toBe('07:05');
    c.onDate(null);
    expect(c.date()).toBe('');
  });

  it('behält beim Tippen dasselbe Date-Objekt (kein Zurückschreiben ins Feld)', () => {
    const c = TestBed.createComponent(WeeklyScheduleFieldsComponent).componentInstance;
    const d = new Date(2026, 6, 9);
    c.onDate(d);
    expect(c.dateValue).toBe(d);
  });
});
