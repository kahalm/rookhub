import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { WorksheetService, WorksheetSummary } from './worksheet.service';

/**
 * Übersicht der AUFGABENBLÄTTER (`/worksheets`): oben die Zwischenablage — der Sammelkorb, in dem
 * standardmäßig alles landet, was man aus Kursen und Puzzles „an ein Aufgabenblatt schickt" —,
 * darunter die benannten Blätter, die man später wieder öffnet, ändert und druckt.
 *
 * <p>Aus der Ablage wird hier (oder im Blatt selbst) per Namen ein festes Blatt; die Stellungen
 * wandern dabei mit, die Ablage ist danach wieder leer.</p>
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-worksheet-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatMenuModule, MatProgressSpinnerModule, MatTooltipModule,
    TranslatePipe,
  ],
  templateUrl: './worksheet-list.component.html',
  styleUrls: ['./worksheet-list.component.scss'],
})
export class WorksheetListComponent implements OnInit {
  sheets: WorksheetSummary[] = [];
  loading = true;
  error = false;
  busy = false;

  /** Name für „Zwischenablage als Aufgabenblatt speichern" bzw. für ein neues leeres Blatt. */
  newName = '';

  /** Aktives Themen-Filter; `null` = alle Blätter. */
  themeFilter: string | null = null;

  private worksheets = inject(WorksheetService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  get clipboard(): WorksheetSummary | undefined { return this.sheets.find(s => s.isClipboard); }

  /** Die benannten Blätter, ggf. auf ein Thema eingedampft. */
  get named(): WorksheetSummary[] {
    const sheets = this.sheets.filter(s => !s.isClipboard);
    if (!this.themeFilter) return sheets;
    return sheets.filter(s => s.themes.some(t => t.toLowerCase() === this.themeFilter!.toLowerCase()));
  }

  /** Alle vergebenen Themen (häufigste zuerst) — die Filterzeile über der Liste. */
  get allThemes(): string[] {
    const counts = new Map<string, number>();
    for (const sheet of this.sheets) {
      if (sheet.isClipboard) continue;
      for (const theme of sheet.themes) counts.set(theme, (counts.get(theme) ?? 0) + 1);
    }
    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .map(([theme]) => theme);
  }

  /** Ein zweiter Klick auf dasselbe Thema hebt den Filter wieder auf. */
  toggleThemeFilter(theme: string): void {
    this.themeFilter = this.themeFilter === theme ? null : theme;
  }

  ngOnInit(): void { this.load(); }

  private load(): void {
    this.loading = true;
    this.worksheets.list().subscribe({
      next: list => { this.sheets = list; this.loading = false; this.error = false; this.cdr.markForCheck(); },
      error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
    });
  }

  /** Zwischenablage unter einem Namen sichern — danach steht man im neuen Blatt. */
  saveClipboard(): void {
    const name = this.newName.trim();
    const clip = this.clipboard;
    if (!name || this.busy || !clip || clip.itemCount === 0) return;
    this.busy = true;
    this.worksheets.saveClipboardAs(name).subscribe({
      next: sheet => {
        this.busy = false;
        this.newName = '';
        this.router.navigate(['/worksheets', sheet.id]);
      },
      error: () => {
        this.busy = false;
        this.snackbar.warn(this.translate.instant('worksheets.list.saveError'));
        this.cdr.markForCheck();
      },
    });
  }

  /** Leeres Blatt anlegen (wenn man von vornherein weiß, wie es heißen soll). */
  createEmpty(): void {
    const name = this.newName.trim();
    if (!name || this.busy) return;
    this.busy = true;
    this.worksheets.create(name).subscribe({
      next: sheet => { this.busy = false; this.newName = ''; this.router.navigate(['/worksheets', sheet.id]); },
      error: () => {
        this.busy = false;
        this.snackbar.warn(this.translate.instant('worksheets.list.saveError'));
        this.cdr.markForCheck();
      },
    });
  }

  clearClipboard(): void {
    const clip = this.clipboard;
    if (!clip || clip.itemCount === 0) return;
    if (!confirm(this.translate.instant('worksheets.list.clearConfirm', { n: clip.itemCount }))) return;
    this.worksheets.clear(clip.id).subscribe({ next: () => this.load(), error: () => this.failed() });
  }

  remove(sheet: WorksheetSummary): void {
    if (!confirm(this.translate.instant('worksheets.list.deleteConfirm', { name: sheet.name }))) return;
    this.worksheets.remove(sheet.id).subscribe({ next: () => this.load(), error: () => this.failed() });
  }

  private failed(): void {
    this.snackbar.warn(this.translate.instant('worksheets.list.saveError'));
    this.cdr.markForCheck();
  }
}
