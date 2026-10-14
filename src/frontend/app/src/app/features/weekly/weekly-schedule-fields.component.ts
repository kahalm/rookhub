import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateAdapter, MAT_DATE_FORMATS, MatDateFormats } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTimepickerModule } from '@angular/material/timepicker';
import { TranslatePipe } from '@ngx-translate/core';
import { LOCALE_DATE_FORMATS, LocaleNativeDateAdapter, dateFieldOrder, parseYmd } from '../../shared/date-field/locale-date-adapter';

// Adapter + Reihenfolge seit b1-rk-edit-datum-us-format geteilt (shared/date-field); hier weiter exportiert.
export { dateFieldOrder, LocaleNativeDateAdapter };

/** TT.MM.JJJJ bzw. 24 h, wo die Sprache es so schreibt (zweistellig wie im Termin-Text der Liste). */
export const WEEKLY_DATE_FORMATS: MatDateFormats = {
  ...LOCALE_DATE_FORMATS,
  display: {
    ...LOCALE_DATE_FORMATS.display,
    timeInput: { hour: '2-digit', minute: '2-digit' },
    timeOptionLabel: { hour: '2-digit', minute: '2-digit' },
  },
};

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
    @media (max-width: 599.98px) { .f-date, .f-time { width: 100%; } }
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
