import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { DirectoryCalendarDay, DirectoryEntry, isPlaceholderLocation } from './tournament-directory.model';
import { DirectoryNamePipe } from './directory-name.pipe';

interface CalendarCell {
  date: string;
  dayOfMonth: number;
  inMonth: boolean;
  isToday: boolean;
  entries: DirectoryEntry[];
}

interface AgendaDay {
  date: string;
  label: string;
  entries: DirectoryEntry[];
}

/** Eine Kalenderwoche der Handy-Agenda (Montag–Sonntag, auf den Monat beschnitten). */
interface AgendaWeek {
  /** Montag der Woche, `yyyy-MM-dd` — zugleich Sprungziel `#cal-week-<key>`. */
  key: string;
  /** ISO-Kalenderwoche. */
  week: number;
  /** Tage der Woche im Monat, z. B. „5.–11. Okt.“ (Intl, Sprache des Nutzers). */
  range: string;
  days: AgendaDay[];
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
  imports: [CommonModule, MatButtonModule, MatIconModule, TranslatePipe, DirectoryNamePipe],
  templateUrl: './tournament-calendar.component.html',
  styleUrls: ['./tournament-calendar.component.scss'],
})
export class TournamentCalendarComponent implements OnChanges {
  @Input() days: DirectoryCalendarDay[] = [];
  @Input() year!: number;
  @Input() month!: number;
  @Input() locale = 'de';
  @Input() loading = false;
  /** Der Monat kam nicht — dann steht darueber die Fehlerzeile, und „kein Turnier" waere falsch. */
  @Input() failed = false;

  @Output() monthChanged = new EventEmitter<{ year: number; month: number }>();
  @Output() entrySelected = new EventEmitter<DirectoryEntry>();

  weeks: CalendarCell[][] = [];
  weekdayLabels: string[] = [];
  monthLabel = '';

  /** Unter 768px ist das Raster unlesbar — dort zeigt das Template diese Agenda-Liste. */
  agenda: AgendaDay[] = [];
  /** Dieselbe Agenda nach Kalenderwochen, mit Sprungleiste darüber (d7-t-kalender-handy-seite-zu-lang). */
  agendaWeeks: AgendaWeek[] = [];

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

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

  /**
   * Wie viele Turniere eine Zelle zeigt, bevor sie zusammenfasst. Vier passen in die Zellenhoehe;
   * bei drei stand schon bei vier Terminen ein „+1 weitere", obwohl noch Platz war.
   */
  static readonly VisiblePerDay = 4;

  /** Tage, die der Nutzer aufgeklappt hat — dort stehen ALLE Turniere. */
  private expanded = new Set<string>();

  visibleEntries(cell: CalendarCell): DirectoryEntry[] {
    return this.expanded.has(cell.date)
      ? cell.entries
      : cell.entries.slice(0, TournamentCalendarComponent.VisiblePerDay);
  }

  hiddenCount(cell: CalendarCell): number {
    if (this.expanded.has(cell.date)) return 0;
    return Math.max(0, cell.entries.length - TournamentCalendarComponent.VisiblePerDay);
  }

  /** „+n weitere" klappt den Tag auf, statt die restlichen Turniere unerreichbar zu lassen. */
  expand(date: string): void {
    this.expanded.add(date);
  }

  /**
   * Deckel fuer die Handy-Agenda. Die listete jedes Turnier an jedem seiner Tage ohne Grenze —
   * mit dem Standardfilter (kein Umkreis) waren das fuer einen Monat rund 2750 Knopf-Zeilen
   * untereinander: kilometerlanges Scrollen, kein Ueberblick, spuerbar traege beim Rendern.
   * Drei je Tag: mit sechs war die Agenda eines vollen Monats 14 850 px bzw. 17,6 Handy-Bildschirme lang
   * (UI-Sweep d7, DESIGN 4.6 Regel 11) — „+n weitere“ klappt den Tag auf, die Wochen-Sprungleiste gliedert.
   */
  static readonly AgendaVisiblePerDay = 3;

  /** Sprungleiste: zur Woche scrollen. */
  jumpToWeek(week: AgendaWeek): void {
    this.host.nativeElement.querySelector(`#cal-week-${week.key}`)?.scrollIntoView({ block: 'start' });
  }

  trackByWeek = (_: number, week: AgendaWeek) => week.key;

  visibleAgendaEntries(day: AgendaDay): DirectoryEntry[] {
    return this.expanded.has(day.date)
      ? day.entries
      : day.entries.slice(0, TournamentCalendarComponent.AgendaVisiblePerDay);
  }

  hiddenAgendaCount(day: AgendaDay): number {
    if (this.expanded.has(day.date)) return 0;
    return Math.max(0, day.entries.length - TournamentCalendarComponent.AgendaVisiblePerDay);
  }

  trackByDate = (_: number, cell: CalendarCell) => cell.date;
  trackById = (_: number, entry: DirectoryEntry) => entry.id;

  /** Ort ist nur ein Platzhalter wie „various" (UI-Sweep 2026-10-11, r2-t-various). */
  isVarious(entry: DirectoryEntry): boolean { return isPlaceholderLocation(entry.location); }

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
    // „Heute“ ist der Kalendertag des Betrachters (Ortszeit), nicht der UTC-Tag — sonst steht der Rahmen in
    // Mitteleuropa bis 1 bzw. 2 Uhr nachts auf dem Vortag (UX-076). Das Raster selbst bleibt UTC (Date.UTC).
    const todayIso = localIsoDate(new Date());

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
    // Ein Monatswechsel beginnt wieder eingeklappt — sonst bleibt ein aufgeklappter 14. haengen.
    this.expanded.clear();

    const dayFormatter = new Intl.DateTimeFormat(this.locale,
      { weekday: 'short', day: 'numeric', month: 'short' });
    // Nur Tage des angezeigten Monats: beim Blaettern setzt der Elter Jahr/Monat sofort, `days`
    // aber erst mit der Antwort (Mobilfunk 1–3 s, nach einem Ladefehler gar nicht). Bis dahin
    // standen unter „Oktober 2026" die September-Turniere. Das Raster hat das Problem nicht,
    // weil es seine Zellen per Datum fuellt — die Agenda uebernimmt die Tage 1:1.
    const monthPrefix = `${this.year}-${pad(this.month)}`;
    this.agenda = this.days
      .filter(d => d.date.startsWith(monthPrefix) && d.items.length > 0)
      .map(d => ({
        date: d.date.slice(0, 10),
        label: dayFormatter.format(new Date(d.date.slice(0, 10) + 'T00:00:00Z')),
        entries: d.items,
      }));
    this.agendaWeeks = this.groupByWeek(this.agenda);
  }

  private groupByWeek(days: AgendaDay[]): AgendaWeek[] {
    const rangeFormatter = new Intl.DateTimeFormat(this.locale, { day: 'numeric', month: 'short', timeZone: 'UTC' });
    const monthStart = new Date(Date.UTC(this.year, this.month - 1, 1));
    const monthEnd = new Date(Date.UTC(this.year, this.month, 0));
    const weeks: AgendaWeek[] = [];
    for (const day of days) {
      const date = new Date(day.date + 'T00:00:00Z');
      const monday = new Date(date);
      monday.setUTCDate(monday.getUTCDate() - (date.getUTCDay() + 6) % 7);
      const key = isoDate(monday);
      let week = weeks[weeks.length - 1];
      if (!week || week.key !== key) {
        const sunday = new Date(monday);
        sunday.setUTCDate(sunday.getUTCDate() + 6);
        const from = monday < monthStart ? monthStart : monday;
        const to = sunday > monthEnd ? monthEnd : sunday;
        week = { key, week: isoWeek(monday), range: formatRange(rangeFormatter, from, to), days: [] };
        weeks.push(week);
      }
      week.days.push(day);
    }
    return weeks;
  }
}

/** ISO-8601-Kalenderwoche (Woche mit dem ersten Donnerstag ist KW 1). */
export function isoWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  d.setUTCDate(d.getUTCDate() + 3 - (d.getUTCDay() + 6) % 7);   // Donnerstag derselben Woche
  const firstThursday = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
  return 1 + Math.round(((d.getTime() - firstThursday.getTime()) / 86_400_000 - 3 + (firstThursday.getUTCDay() + 6) % 7) / 7);
}

function formatRange(formatter: Intl.DateTimeFormat, from: Date, to: Date): string {
  if (from.getTime() === to.getTime()) return formatter.format(from);
  return typeof formatter.formatRange === 'function'
    ? formatter.formatRange(from, to)
    : `${formatter.format(from)} – ${formatter.format(to)}`;
}

function isoDate(date: Date): string {
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

/** Kalendertag in der Ortszeit des Browsers als `yyyy-MM-dd` (für die Heute-Markierung). */
export function localIsoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value: number): string {
  return value < 10 ? `0${value}` : `${value}`;
}
