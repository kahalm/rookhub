import { Component, LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { IsoDateFieldComponent } from './iso-date-field.component';
import { formatYmd, parseYmd } from './locale-date-adapter';

@Component({
  standalone: true,
  imports: [IsoDateFieldComponent],
  template: `<app-iso-date-field label="Datum" [max]="max" [(value)]="date" />`,
})
class HostComponent {
  date = signal('2026-06-05');
  max = '2026-12-31';
}

// b1-rk-edit-datum-us-format: Datum in der App-Sprache statt im Browserformat; nach außen "YYYY-MM-DD".
describe('IsoDateFieldComponent', () => {
  beforeAll(() => registerLocaleData(localeDe));

  it('zeigt das Datum in der App-Sprache und gibt Getipptes als YYYY-MM-DD zurück', async () => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideNoopAnimations(), { provide: LOCALE_ID, useValue: 'de' }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    expect(input.type).not.toBe('date');
    expect(input.value).toBe('05.06.2026');
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-label')!.textContent).toContain('Datum');

    input.value = '16.07.2026';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.componentInstance.date()).toBe('2026-07-16');

    input.value = '31.02.2026';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.componentInstance.date()).toBe('');
  });

  it('parseYmd/formatYmd wandeln verlustfrei, Leeres bleibt leer', () => {
    expect(formatYmd(parseYmd('2026-10-10'))).toBe('2026-10-10');
    expect(parseYmd('')).toBeNull();
    expect(parseYmd('10/10/2026')).toBeNull();
    expect(formatYmd(null)).toBe('');
  });
});
