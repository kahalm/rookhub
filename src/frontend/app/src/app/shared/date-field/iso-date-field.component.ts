import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateAdapter, MAT_DATE_FORMATS } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule, SubscriptSizing } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { LOCALE_DATE_FORMATS, LocaleNativeDateAdapter, formatYmd, parseYmd } from './locale-date-adapter';

/**
 * Datumsfeld in der App-Sprache (b1-rk-edit-datum-us-format, DESIGN 5.13): Material-Datepicker mit
 * {@link LocaleNativeDateAdapter} statt `<input type="date">` — das native Feld formatiert nach der Browsersprache
 * („10/10/2026" in der deutschen Oberfläche). Nach außen bleibt es beim String "YYYY-MM-DD" ('' = leer/ungültig),
 * `min`/`max` ebenso. Die Beschriftung kommt bereits übersetzt.
 */
@Component({
  selector: 'app-iso-date-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatDatepickerModule],
  providers: [
    { provide: DateAdapter, useClass: LocaleNativeDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: LOCALE_DATE_FORMATS },
  ],
  template: `
    <mat-form-field appearance="outline" [subscriptSizing]="subscriptSizing()" class="idf">
      <mat-label>{{ label() }}</mat-label>
      <input matInput [matDatepicker]="dp" [ngModel]="dateValue" (ngModelChange)="onDate($event)"
             [min]="minDate()" [max]="maxDate()" />
      <mat-datepicker-toggle matIconSuffix [for]="dp" />
      <mat-datepicker #dp />
    </mat-form-field>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .idf { width: 100%; }
  `],
})
export class IsoDateFieldComponent {
  /** "YYYY-MM-DD"; '' = leer/ungültig. */
  readonly value = model('');
  readonly label = input('');
  readonly min = input<string | null | undefined>(null);
  readonly max = input<string | null | undefined>(null);
  readonly subscriptSizing = input<SubscriptSizing>('dynamic');

  // Das Date-Objekt bleibt dasselbe, solange der String von außen nicht wechselt — sonst schriebe ngModel bei jedem
  // Tastendruck den formatierten Wert zurück ins Feld (Muster weekly-schedule-fields).
  private dateObj: Date | null = null;
  private dateSeen: string | null = null;

  get dateValue(): Date | null {
    const s = this.value();
    if (s !== this.dateSeen) { this.dateSeen = s; this.dateObj = parseYmd(s); }
    return this.dateObj;
  }

  readonly minDate = computed(() => parseYmd(this.min()));
  readonly maxDate = computed(() => parseYmd(this.max()));

  onDate(d: Date | null): void {
    const s = formatYmd(d);
    this.dateObj = d;
    this.dateSeen = s;
    this.value.set(s);
  }
}
