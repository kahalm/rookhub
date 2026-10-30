import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateAdapter, MAT_DATE_FORMATS, MatDateFormats } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTimepickerModule } from '@angular/material/timepicker';
import { LOCALE_DATE_FORMATS, LocaleNativeDateAdapter, formatYmd, parseYmd } from './locale-date-adapter';

/** Datum in der App-Sprache, Uhrzeit zweistellig (24 h, wo die Sprache es so schreibt). */
export const DATE_TIME_FORMATS: MatDateFormats = {
  ...LOCALE_DATE_FORMATS,
  display: {
    ...LOCALE_DATE_FORMATS.display,
    timeInput: { hour: '2-digit', minute: '2-digit' },
    timeOptionLabel: { hour: '2-digit', minute: '2-digit' },
  },
};

const p2 = (n: number) => n.toString().padStart(2, '0');
const valid = (d: Date | null): d is Date => !!d && !isNaN(d.getTime());

/** "YYYY-MM-DDTHH:mm" → [Datum "YYYY-MM-DD", Uhrzeit "HH:mm"]; ungültig → ['', '']. */
export function splitLocalDateTime(s: string | null | undefined): [string, string] {
  const m = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(s || '');
  return m ? [m[1], m[2]] : ['', ''];
}

/**
 * Zeitpunkt als Paar aus Datum + Uhrzeit in der App-Sprache (d8-rk-ausgabe-datetime-us-format, DESIGN 5.13) — statt
 * dem nativen datetime-local-Feld, das nach der Browsersprache formatiert („mm/dd/yyyy, --:-- --“ mit 12-h-Uhr).
 * Nach außen bleibt es beim Lokalzeit-String "YYYY-MM-DDTHH:mm" ('' = leer/unvollständig) wie beim nativen Feld.
 * Wird ein Datum gewählt und ist noch keine Uhrzeit da, steht 00:00 sichtbar im Uhrzeitfeld. Beschriftungen kommen
 * bereits übersetzt.
 */
@Component({
  selector: 'app-date-time-fields',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatDatepickerModule, MatTimepickerModule],
  providers: [
    { provide: DateAdapter, useClass: LocaleNativeDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: DATE_TIME_FORMATS },
  ],
  template: `
    <mat-form-field appearance="outline" class="dtf-date" subscriptSizing="dynamic">
      <mat-label>{{ dateLabel() }}</mat-label>
      <input matInput [matDatepicker]="dp" [ngModel]="dateValue" (ngModelChange)="onDate($event)" [required]="required()"
             [max]="maxDate" />
      <mat-datepicker-toggle matIconSuffix [for]="dp" />
      <mat-datepicker #dp />
      @if (hint()) { <mat-hint>{{ hint() }}</mat-hint> }
    </mat-form-field>
    <mat-form-field appearance="outline" class="dtf-time" subscriptSizing="dynamic">
      <mat-label>{{ timeLabel() }}</mat-label>
      <input matInput [matTimepicker]="tp" [ngModel]="timeValue" (ngModelChange)="onTime($event)" [required]="required()" />
      <mat-timepicker-toggle matIconSuffix [for]="tp" />
      <mat-timepicker #tp interval="15m" />
    </mat-form-field>
  `,
  styles: [`
    :host { display: flex; flex-wrap: wrap; gap: 12px; min-width: 0; }
    /* d9-rk-datepicker-label-eng: das Datum bekommt den Platz, die Uhrzeit (5 Zeichen) bleibt schmal — die Beschriftung
       stieß sonst ans Kalender-Symbol. Zusätze wie „optional“ stehen als Hinweis darunter, nicht im Label. */
    .dtf-date { flex: 2 1 200px; min-width: 0; }
    .dtf-time { flex: 0 1 120px; min-width: 110px; }
  `],
})
export class DateTimeFieldsComponent {
  /** Lokalzeit "YYYY-MM-DDTHH:mm"; '' = leer/unvollständig. */
  readonly value = model('');
  readonly dateLabel = input('');
  readonly timeLabel = input('');
  readonly required = input(false);
  /** Hinweis unter dem Datum (bereits übersetzt), z. B. „Optional“ — statt den Zusatz ins Label zu schreiben. */
  readonly hint = input('');
  /** Spätestes Datum (Lokalzeit "YYYY-MM-DDTHH:mm" oder "YYYY-MM-DD"), nur fürs Datum. */
  readonly max = input<string | null>(null);

  private date = '';
  private time = '';
  private seen: string | null = null;
  private dateObj: Date | null = null;
  private timeObj: Date | null = null;

  private sync(): void {
    const v = this.value();
    if (v === this.seen) return;
    this.seen = v;
    const [d, t] = splitLocalDateTime(v);
    // Ein halb ausgefülltes Paar (nur Datum oder nur Uhrzeit) liegt außen als '' — dann nichts überschreiben.
    if (!v && (this.date || this.time) && !(this.date && this.time)) return;
    this.date = d; this.time = t;
    this.dateObj = parseYmd(d);
    const m = /^(\d{2}):(\d{2})$/.exec(t);
    this.timeObj = m ? new Date(2000, 0, 1, +m[1], +m[2]) : null;
  }

  get dateValue(): Date | null { this.sync(); return this.dateObj; }
  get timeValue(): Date | null { this.sync(); return this.timeObj; }
  get maxDate(): Date | null { return parseYmd((this.max() ?? '').slice(0, 10)); }

  onDate(d: Date | null): void {
    this.date = formatYmd(valid(d) ? d : null);
    this.dateObj = valid(d) ? d : null;
    if (this.date && !this.time) { this.time = '00:00'; this.timeObj = new Date(2000, 0, 1, 0, 0); }
    this.emit();
  }

  onTime(d: Date | null): void {
    this.time = valid(d) ? `${p2(d.getHours())}:${p2(d.getMinutes())}` : '';
    this.timeObj = valid(d) ? d : null;
    this.emit();
  }

  private emit(): void {
    const v = this.date && this.time ? `${this.date}T${this.time}` : '';
    this.seen = v;
    this.value.set(v);
  }
}
