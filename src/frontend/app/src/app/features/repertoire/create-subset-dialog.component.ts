import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { RepertoireLine } from './repertoire-viewer.service';
import { groupByChapter, SubsetChapter } from './repertoire-subset.util';
import { pgnHeaderText } from '../../shared/pgn-viewer/pgn-header.util';

export interface CreateSubsetDialogData {
  /** Name des Quell-Repertoires (Vorschlag für den neuen Namen). */
  repertoireName: string;
  /** Alle Linien des Repertoires (auch Info-Linien — sie gehören zum Kapitel). */
  lines: RepertoireLine[];
  /** Vorgewählt: die markierten Linien (Linien-Schlüssel). */
  markedKeys: string[];
}

export interface CreateSubsetDialogResult {
  name: string;
  gameIndices: number[];
}

/**
 * „Neues Repertoire aus Auswahl": Kapitel ankreuzen (= alle seine Linien) und/oder einzelne Linien; vorgewählt sind die
 * markierten Linien. Ergebnis: Name + die gewählten Abschnitte (gameIndex) — das Anlegen macht die Detailseite.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-create-subset-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatIconModule, MatCheckboxModule, MatFormFieldModule, MatInputModule,
    MatDialogModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'repertoire.subset.title' | translate }}</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="name-field">
        <mat-label>{{ 'repertoire.dialog.name' | translate }}</mat-label>
        <input matInput [(ngModel)]="name" name="subsetName" maxlength="200" required />
      </mat-form-field>
      <div class="pick-bar">
        <span class="count">{{ 'repertoire.subset.count' | translate:{ n: picked.size, total: data.lines.length } }}</span>
        <span class="spacer"></span>
        <button mat-button type="button" (click)="setAll(true)">{{ 'repertoire.subset.all' | translate }}</button>
        <button mat-button type="button" (click)="setAll(false)">{{ 'repertoire.subset.none' | translate }}</button>
      </div>
      <div class="chapters">
        @for (c of chapters; track c.chapter) {
          <div class="chapter">
            <div class="chapter-row">
              <button mat-icon-button type="button" (click)="toggleOpen(c)"
                      [attr.aria-expanded]="open.has(c.chapter)"
                      [attr.aria-label]="'repertoire.lines.toggleChapter' | translate">
                <mat-icon>{{ open.has(c.chapter) ? 'expand_more' : 'chevron_right' }}</mat-icon>
              </button>
              <mat-checkbox [checked]="state(c) === 'all'" [indeterminate]="state(c) === 'some'"
                            (change)="setChapter(c, $event.checked)">
                <span class="chapter-name">{{ label(c.chapter) || ('repertoire.lines.noChapter' | translate) }}</span>
              </mat-checkbox>
              <span class="chapter-count">{{ pickedIn(c) }}/{{ c.lines.length }}</span>
            </div>
            @if (open.has(c.chapter)) {
              <div class="lines">
                @for (l of c.lines; track l.gameIndex; let i = $index) {
                  <mat-checkbox class="line" [checked]="picked.has(l.gameIndex)" (change)="setLine(l, $event.checked)">
                    <span class="line-title">{{ title(l) || ('repertoire.lines.lineN' | translate:{ n: i + 1 }) }}</span>
                    <span class="line-summary">{{ l.summary }}</span>
                  </mat-checkbox>
                }
              </div>
            }
          </div>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!name.trim() || picked.size === 0" (click)="confirm()">
        {{ 'repertoire.subset.create' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .name-field { width: 100%; margin-top: 4px; }
    .pick-bar { display: flex; align-items: center; gap: 4px; margin: -8px 0 4px; }
    .pick-bar .count { font-size: 13px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .spacer { flex: 1; }
    .chapters { max-height: min(60vh, 520px); overflow-y: auto; }
    .chapter-row { display: flex; align-items: center; gap: 2px; }
    .chapter-name { font-weight: 500; }
    .chapter-count { margin-left: auto; padding-left: 8px; font-size: 12px; color: color-mix(in srgb, currentColor 60%, transparent); }
    .lines { display: flex; flex-direction: column; padding-left: 48px; }
    .line .line-title { display: block; }
    .line .line-summary { display: block; font-family: 'Roboto Mono', monospace; font-size: 12px;
      color: color-mix(in srgb, currentColor 60%, transparent); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 52ch; }
  `],
})
export class CreateSubsetDialogComponent {
  readonly chapters: SubsetChapter[];
  readonly picked = new Set<number>();
  readonly open = new Set<string>();
  name: string;

  constructor(
    public dialogRef: MatDialogRef<CreateSubsetDialogComponent, CreateSubsetDialogResult>,
    @Inject(MAT_DIALOG_DATA) public data: CreateSubsetDialogData,
  ) {
    this.chapters = groupByChapter(data.lines);
    const marked = new Set(data.markedKeys);
    for (const l of data.lines) if (marked.has(l.lineKey)) this.picked.add(l.gameIndex);
    // Kapitel mit teilweiser Auswahl gleich aufklappen — dort sieht man, was gewählt ist.
    for (const c of this.chapters) if (this.state(c) === 'some') this.open.add(c.chapter);
    this.name = `${(data.repertoireName || '').trim()} (2)`.trim().slice(0, 200);
  }

  label(chapter: string): string { return pgnHeaderText(chapter); }

  title(l: RepertoireLine): string {
    const white = pgnHeaderText(l.white);
    const black = pgnHeaderText(l.black);
    if (l.chapter && l.black === l.chapter) return white || black;
    if (!white || !black) return white || black;
    return `${white} vs ${black}`;
  }

  pickedIn(c: SubsetChapter): number { return c.lines.filter(l => this.picked.has(l.gameIndex)).length; }

  state(c: SubsetChapter): 'none' | 'some' | 'all' {
    const n = this.pickedIn(c);
    return n === 0 ? 'none' : n === c.lines.length ? 'all' : 'some';
  }

  setChapter(c: SubsetChapter, on: boolean): void {
    for (const l of c.lines) on ? this.picked.add(l.gameIndex) : this.picked.delete(l.gameIndex);
  }

  setLine(l: RepertoireLine, on: boolean): void {
    on ? this.picked.add(l.gameIndex) : this.picked.delete(l.gameIndex);
  }

  setAll(on: boolean): void {
    for (const c of this.chapters) this.setChapter(c, on);
  }

  toggleOpen(c: SubsetChapter): void {
    this.open.has(c.chapter) ? this.open.delete(c.chapter) : this.open.add(c.chapter);
  }

  confirm(): void {
    const name = this.name.trim();
    if (!name || this.picked.size === 0) return;
    this.dialogRef.close({ name, gameIndices: [...this.picked].sort((a, b) => a - b) });
  }
}
