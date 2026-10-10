import { ChangeDetectionStrategy, Component, Injectable, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateAdapter, MAT_DATE_FORMATS, MAT_NATIVE_DATE_FORMATS, MatDateFormats, NativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTimepickerModule } from '@angular/material/timepicker';
import { TranslatePipe } from '@ngx-translate/core';

/** Reihenfolge von Tag/Monat/Jahr in der Locale (de: day, month, year; en: month, day, year; hu: year, month, day). */
export function dateFieldOrder(locale: string): string[] {
  return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date(2017, 0, 2))
    .map(p => p.type)
    .filter(t => t === 'year' || t === 'month' || t === 'day');
}

/**
 * NativeDateAdapter, der getippte Daten in der Reihenfolge der App-Sprache liest („16.07.2026" in de) —
 * der eingebaute nimmt `Date.parse` und verstünde nur das US-Format.
 */
@Injectable()
export class LocaleNativeDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value === 'string') {
      const nums = value.match(/\d+/g);
      if (nums && nums.length === 3) {
        const order = dateFieldOrder(this.locale);
        const part = (k: string) => Number(nums[order.indexOf(k)]);
        let year = part('year');
        if (year < 100) year += 2000;
        const month = part('month') - 1, day = part('day');
        const d = new Date(year, month, day);
        return year >= 1000 && d.getMonth() === month && d.getDate() === day ? d : this.invalid();
      }
    }
    return super.parse(value, parseFormat);
  }
}

/** TT.MM.JJJJ bzw. 24 h, wo die Sprache es so schreibt (zweistellig wie im Termin-Text der Liste). */
export const WEEKLY_DATE_FORMATS: MatDateFormats = {
  ...MAT_NATIVE_DATE_FORMATS,
  display: {
    ...MAT_NATIVE_DATE_FORMATS.display,
    dateInput: { year: 'numeric', month: '2-digit', day: '2-digit' },
    timeInput: { hour: '2-digit', minute: '2-digit' },
    timeOptionLabel: { hour: '2-digit', minute: '2-digit' },
  },
};

/** "YYYY-MM-DD" → lokales Datum (null bei leer/ungültig). */
function parseYmd(s: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || '');
  return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
}

/** "HH:mm" → Zeitpunkt (Datum egal, nur Stunde/Minute zählen). */
function parseHm(s: string): Date | null {
  const m = /^(\d{1,2}):(\d{2})$/.exec(s || '');
  return m ? new Date(2000, 0, 1, +m[1], +m[2]) : null;
}

const p2 = (n: number) => n.toString().padStart(2, '0');
const valid = (d: Date | null): d is Date => !!d && !isNaN(d.getTime());

/**
 * Datum + Uhrzeit eines Wochenpost-Termins als Material-Felder in der App-Sprache. Nach außen bleibt es bei den
 * Strings "YYYY-MM-DD" und "HH:mm" (Wandzeit) — daraus macht weeklyScheduledAtUtc wie bisher den UTC-Termin.
 */
@Component({
  selector: 'app-weekly-schedule-fields',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatDatepickerModule, MatTimepickerModule, TranslatePipe],
  providers: [
    { provide: DateAdapter, useClass: LocaleNativeDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: WEEKLY_DATE_FORMATS },
  ],
  template: `
    <mat-form-field appearance="outline" class="f-date">
      <mat-label>{{ 'weekly.fields.date' | translate }}</mat-label>
      <input matInput [matDatepicker]="dp" [ngModel]="dateValue" (ngModelChange)="onDate($event)">
      <mat-datepicker-toggle matIconSuffix [for]="dp" />
      <mat-datepicker #dp />
    </mat-form-field>
    <mat-form-field appearance="outline" class="f-time">
      <mat-label>{{ 'weekly.fields.time' | translate }}</mat-label>
      <input matInput [matTimepicker]="tp" [ngModel]="timeValue" (ngModelChange)="onTime($event)">
      <mat-timepicker-toggle matIconSuffix [for]="tp" />
      <mat-timepicker #tp interval="15m" />
    </mat-form-field>
  `,
  styles: [`
    :host { display: contents; }
    .f-date { width: 170px; }
    .f-time { width: 130px; }
    @media (max-width: 600px) { .f-date, .f-time { width: 100%; } }
  `],
})
export class WeeklyScheduleFieldsComponent {
  /** "YYYY-MM-DD" (Wandzeit); '' = leer/ungültig. */
  readonly date = model('');
  /** "HH:mm" (Wandzeit); '' = leer/ungültig. */
  readonly time = model('');

  // Das Date-Objekt bleibt dasselbe, solange der String von außen nicht wechselt — sonst schriebe ngModel
  // bei jedem Tastendruck den formatierten Wert zurück ins Feld.
  private dateObj: Date | null = null;
  private dateSeen: string | null = null;
  private timeObj: Date | null = null;
  private timeSeen: string | null = null;

  get dateValue(): Date | null {
    const s = this.date();
    if (s !== this.dateSeen) { this.dateSeen = s; this.dateObj = parseYmd(s); }
    return this.dateObj;
  }

  get timeValue(): Date | null {
    const s = this.time();
    if (s !== this.timeSeen) { this.timeSeen = s; this.timeObj = parseHm(s); }
    return this.timeObj;
  }

  onDate(d: Date | null): void {
    const s = valid(d) ? `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())}` : '';
    this.dateObj = d;
    this.dateSeen = s;
    this.date.set(s);
  }

  onTime(d: Date | null): void {
    const s = valid(d) ? `${p2(d.getHours())}:${p2(d.getMinutes())}` : '';
    this.timeObj = d;
    this.timeSeen = s;
    this.time.set(s);
  }
}
