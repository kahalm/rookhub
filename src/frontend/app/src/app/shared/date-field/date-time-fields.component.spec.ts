import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { DateTimeFieldsComponent, splitLocalDateTime } from './date-time-fields.component';

@Component({
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DateTimeFieldsComponent],
  template: `<app-date-time-fields [(value)]="value" dateLabel="Freigabe" timeLabel="Uhrzeit" [required]="true" />`,
})
class HostComponent { value = signal(''); }

/** d8-rk-ausgabe-datetime-us-format (DESIGN 5.13): Datum + Uhrzeit in der App-Sprache statt datetime-local. */
describe('DateTimeFieldsComponent', () => {
  function make(value = '') {
    TestBed.configureTestingModule({ providers: [provideNoopAnimations()] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.value.set(value);
    fixture.detectChanges();
    const cmp = fixture.debugElement.children[0].componentInstance as DateTimeFieldsComponent;
    return { fixture, host: fixture.componentInstance, cmp, el: fixture.nativeElement as HTMLElement };
  }
  afterEach(() => TestBed.resetTestingModule());

  it('zerlegt „YYYY-MM-DDTHH:mm“ und verwirft Unsinn', () => {
    expect(splitLocalDateTime('2026-09-14T18:00')).toEqual(['2026-09-14', '18:00']);
    expect(splitLocalDateTime('kaputt')).toEqual(['', '']);
    expect(splitLocalDateTime(null)).toEqual(['', '']);
  });

  it('zwei Outline-Felder (Datum mit Datepicker, Uhrzeit mit Timepicker), kein natives datetime-local, Pflicht-Sternchen', () => {
    const { el } = make('2026-09-14T18:00');
    const fields = el.querySelectorAll('mat-form-field');
    expect(fields.length).toBe(2);
    fields.forEach(f => expect(f.classList).toContain('mat-form-field-appearance-outline'));
    expect(el.querySelector('input[type="datetime-local"]')).toBeNull();
    expect(el.querySelector('mat-datepicker-toggle')).not.toBeNull();
    expect(el.querySelector('mat-timepicker-toggle')).not.toBeNull();
    expect(el.querySelectorAll('.mat-mdc-form-field-required-marker').length).toBe(2);
    expect(Array.from(el.querySelectorAll('mat-label'), l => l.textContent!.trim())).toEqual(['Freigabe', 'Uhrzeit']);
  });

  it('füllt aus dem Wert vor und meldet Datum + Uhrzeit wieder als „YYYY-MM-DDTHH:mm“', () => {
    const { cmp, host } = make('2026-09-14T18:00');
    expect(cmp.dateValue!.getDate()).toBe(14);
    expect(cmp.timeValue!.getHours()).toBe(18);
    cmp.onTime(new Date(2000, 0, 1, 9, 15));
    expect(host.value()).toBe('2026-09-14T09:15');
    cmp.onDate(new Date(2026, 9, 3));
    expect(host.value()).toBe('2026-10-03T09:15');
  });

  it('nur ein Datum gewählt: Uhrzeit 00:00 steht sichtbar da; Datum geleert → leerer Wert', () => {
    const { cmp, host } = make('');
    cmp.onDate(new Date(2026, 9, 3));
    expect(host.value()).toBe('2026-10-03T00:00');
    expect(cmp.timeValue!.getHours()).toBe(0);
    cmp.onDate(null);
    expect(host.value()).toBe('');
  });
});
