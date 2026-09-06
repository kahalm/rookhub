import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { DirectoryCalendarDay, DirectoryEntry } from './tournament-directory.model';

interface CalendarCell {
  date: string;
  dayOfMonth: number;
  inMonth: boolean;
  isToday: boolean;
  entries: DirectoryEntry[];
}

/**
 * Monatsraster von Hand statt Kalender-Bibliothek. Der Grund ist nicht Sparsamkeit: jede
 * Kalender-Bibliothek bringt ihre eigene Lokalisierung mit, die neben den 25 ngx-translate-Dateien
 * ein zweites, immer leicht abweichendes Sprachsystem waere. Wochentags- und Monatsnamen liefert
 * `Intl` ohnehin — in genau der Sprache, die der Nutzer eingestellt hat.
 */
@Component({
  selector: 'app-tournament-calendar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, MatButtonModule, MatIconModule, TranslatePipe],
  templateUrl: './tournament-calendar.component.html',
  styleUrls: ['./tournament-calendar.component.scss'],
})
export class TournamentCalendarComponent implements OnChanges {
  @Input() days: DirectoryCalendarDay[] = [];
  @Input() year!: number;
  @Input() month!: number;
  @Input() locale = 'de';
  @Input() loading = false;

  @Output() monthChanged = new EventEmitter<{ year: number; month: number }>();
  @Output() entrySelected = new EventEmitter<DirectoryEntry>();

  weeks: CalendarCell[][] = [];
  weekdayLabels: string[] = [];
  monthLabel = '';

  /** Unter 768px ist das Raster unlesbar — dort zeigt das Template diese Agenda-Liste. */
  agenda: { date: string; label: string; entries: DirectoryEntry[] }[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['locale'] || !this.weekdayLabels.length) this.buildWeekdayLabels();
    this.buildGrid();
  }

  previousMonth(): void {
    const month = this.month === 1 ? 12 : this.month - 1;
    const year = this.month === 1 ? this.year - 1 : this.year;
    this.monthChanged.emit({ year, month });
  }

  nextMonth(): void {
    const month = this.month === 12 ? 1 : this.month + 1;
    const year = this.month === 12 ? this.year + 1 : this.year;
    this.monthChanged.emit({ year, month });
  }

  today(): void {
    const now = new Date();
    this.monthChanged.emit({ year: now.getFullYear(), month: now.getMonth() + 1 });
  }

  trackByDate = (_: number, cell: CalendarCell) => cell.date;
  trackById = (_: number, entry: DirectoryEntry) => entry.chessResultsId;

  private buildWeekdayLabels(): void {
    const formatter = new Intl.DateTimeFormat(this.locale, { weekday: 'short' });
    // 2024-01-01 war ein Montag — die Woche beginnt hier bewusst montags (europäische Lesart).
    this.weekdayLabels = Array.from({ length: 7 },
      (_, i) => formatter.format(new Date(Date.UTC(2024, 0, 1 + i))));
  }

  private buildGrid(): void {
    if (!this.year || !this.month) return;

    this.monthLabel = new Intl.DateTimeFormat(this.locale, { month: 'long', year: 'numeric' })
      .format(new Date(Date.UTC(this.year, this.month - 1, 1)));

    const byDate = new Map(this.days.map(d => [d.date.slice(0, 10), d.items]));
    const todayIso = isoDate(new Date());

    const first = new Date(Date.UTC(this.year, this.month - 1, 1));
    // Montag = 0: getUTCDay() liefert Sonntag = 0, deshalb der Versatz.
    const leading = (first.getUTCDay() + 6) % 7;
    const start = new Date(first);
    start.setUTCDate(start.getUTCDate() - leading);

    const weeks: CalendarCell[][] = [];
    const cursor = new Date(start);
    // Sechs Wochen decken jeden Monat ab; die letzte wird verworfen, wenn sie leer bleibt.
    for (let week = 0; week < 6; week++) {
      const row: CalendarCell[] = [];
      for (let day = 0; day < 7; day++) {
        const iso = isoDate(cursor);
        row.push({
          date: iso,
          dayOfMonth: cursor.getUTCDate(),
          inMonth: cursor.getUTCMonth() === this.month - 1,
          isToday: iso === todayIso,
          entries: byDate.get(iso) ?? [],
        });
        cursor.setUTCDate(cursor.getUTCDate() + 1);
      }
      weeks.push(row);
      if (week >= 4 && row.every(cell => !cell.inMonth)) { weeks.pop(); break; }
    }
    this.weeks = weeks;

    const dayFormatter = new Intl.DateTimeFormat(this.locale,
      { weekday: 'short', day: 'numeric', month: 'short' });
    this.agenda = this.days
      .filter(d => d.items.length > 0)
      .map(d => ({
        date: d.date.slice(0, 10),
        label: dayFormatter.format(new Date(d.date.slice(0, 10) + 'T00:00:00Z')),
        entries: d.items,
      }));
  }
}

function isoDate(date: Date): string {
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

function pad(value: number): string {
  return value < 10 ? `0${value}` : `${value}`;
}
